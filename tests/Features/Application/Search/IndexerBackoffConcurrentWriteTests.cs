/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System.Data.Common;
using Listenarr.Infrastructure.DependencyInjection.Search;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Search;

/// <summary>
/// The failure backoff write runs inside the bounded indexer fan-out, once per indexer whose rung
/// changed. Every indexer in one search shares a DI scope, so a write that goes through the scope's
/// one <see cref="ListenArrDbContext"/> collides with its siblings: EF refuses a second operation on
/// a context before the first completes, and the loser's backoff state is dropped with a warning.
/// </summary>
/// <remarks>
/// <para>
/// The primary assertion is which context each backoff UPDATE ran on, recorded by
/// <see cref="UpdateRecorder"/> from the command interceptor: one distinct context per write, none
/// of them the scope's. That holds or fails regardless of scheduling. Whether two operations on one
/// context actually collide does not: EF's concurrency detector only throws when the second
/// operation arrives from a different thread, so on a single CPU two writes on one shared context
/// can run back to back on one thread and succeed, and an outcome-only test passes on the broken
/// code.
/// </para>
/// <para>
/// The recorder also holds each UPDATE until the expected number have arrived, or a short timeout,
/// so on a multi-core machine the writes really do overlap and the secondary outcome assertions
/// (every rung persisted) exercise concurrent SQLite writers. Nothing asserts on that overlap.
/// </para>
/// </remarks>
[Trait("Area", "Search")]
[Trait("Name", "IndexerBackoffConcurrentWriteTests")]
[Trait("Category", "IndexerSearchWorkflow")]
public sealed class IndexerBackoffConcurrentWriteTests : BaseTests
{
    private const int IndexerCount = 4;

    [Fact]
    [Trait("Method", "SearchIndexersAsync")]
    [Trait("Scenario", "EveryIndexerFailsInOneBatch")]
    public async Task SearchIndexersAsync_EveryIndexerFailsAtOnce_EveryBackoffStateIsPersisted()
    {
        // Given: four indexers, all answering 503 in the same batch, wired the way the app wires
        // them: repository, status service and workflow resolved from one scope.
        var recorder = new UpdateRecorder(expected: IndexerCount);
        await using var provider = await BuildServiceProviderAsync(recorder);
        var logger = new RecordingLogger<IndexerSearchWorkflow>();

        // When
        await using (var scope = provider.CreateAsyncScope())
        {
            var scopedContext = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();
            var workflow = BuildWorkflow(scope.ServiceProvider, logger);
            await workflow.SearchIndexersAsync("the time machine");

            // Then: each indexer's write ran on a context of its own, and none on the one every
            // indexer in this search shares.
            AssertOneOwnContextPerWrite(recorder, scopedContext);
        }

        // And: every indexer is on rung 1, not just the one that won the race.
        var stored = await ReadIndexersAsync(provider);
        Assert.All(stored, indexer =>
        {
            Assert.Equal(1, indexer.EscalationLevel);
            Assert.NotNull(indexer.DisabledTill);
            Assert.Equal(nameof(IndexerQueryReason.HttpStatus), indexer.LastFailureReason);
        });
        Assert.DoesNotContain(logger.Warnings, w => w.StartsWith("Failed to record failure backoff state", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Method", "UpdateBackoffStateAsync")]
    [Trait("Scenario", "ConcurrentWritesThroughOneRepository")]
    public async Task UpdateBackoffStateAsync_ConcurrentCallsOnOneScopedRepository_AllLand()
    {
        // Given: the DI-resolved repository, the one every indexer in a search shares.
        var recorder = new UpdateRecorder(expected: IndexerCount);
        await using var provider = await BuildServiceProviderAsync(recorder);
        var till = new DateTime(2026, 9, 14, 18, 0, 0, DateTimeKind.Utc);

        // When: one write per indexer, all at once
        await using (var scope = provider.CreateAsyncScope())
        {
            var scopedContext = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();
            var repository = scope.ServiceProvider.GetRequiredService<IIndexerRepository>();

            // The scope has already read the indexer list, as SearchIndexersAsync does before its
            // fan-out, so the context is initialised and a collision reports as the production one.
            await repository.GetEnabledAsync(isAutomaticSearch: false);

            await Task.WhenAll(Enumerable.Range(1, IndexerCount).Select(id => Task.Run(() =>
                repository.UpdateBackoffStateAsync(
                    id,
                    new IndexerBackoffState(till.AddHours(-1), till.AddHours(-1), id, till, "Timeout")))));

            // Then
            AssertOneOwnContextPerWrite(recorder, scopedContext);
        }

        // And: each row carries its own rung, so no write was lost or applied to the wrong row.
        var stored = await ReadIndexersAsync(provider);
        Assert.Equal(Enumerable.Range(1, IndexerCount), stored.Select(i => i.EscalationLevel));
        Assert.All(stored, indexer => Assert.Equal(till, indexer.DisabledTill));
    }

    [Fact]
    [Trait("Scenario", "ControlRecorderSeesTheSharedContext")]
    public async Task Control_RepositoryWithoutAFactory_WritesOnTheScopedContext()
    {
        // The apparatus check, and the shape the code had before the fix: a repository holding
        // only the scope's context. The recorder must report that context, or the assertion the
        // tests above rely on could not tell the two apart. Sequential and single-write, so
        // nothing here depends on scheduling.
        var recorder = new UpdateRecorder(expected: 1);
        await using var provider = await BuildServiceProviderAsync(recorder);

        await using var scope = provider.CreateAsyncScope();
        var scopedContext = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();
        var repository = new EfIndexerRepository(scopedContext);

        await repository.UpdateBackoffStateAsync(
            1,
            new IndexerBackoffState(null, null, 2, new DateTime(2026, 9, 14, 18, 0, 0, DateTimeKind.Utc), "Timeout"));

        var context = Assert.Single(recorder.Contexts);
        Assert.Same(scopedContext, context);
    }

    private static void AssertOneOwnContextPerWrite(UpdateRecorder recorder, ListenArrDbContext scopedContext)
    {
        var contexts = recorder.Contexts;
        Assert.Equal(IndexerCount, contexts.Count);
        Assert.Equal(IndexerCount, contexts.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.DoesNotContain(contexts, c => ReferenceEquals(c, scopedContext));
    }

    private IndexerSearchWorkflow BuildWorkflow(IServiceProvider services, ILogger<IndexerSearchWorkflow> logger) =>
        new(
            new HttpClient(),
            new Mock<IConfigurationService>().Object,
            services.GetRequiredService<IIndexerRepository>(),
            new IIndexerSearchProvider[] { new UnavailableSearchProvider() },
            new IndexerAdditionalSettingsParser(NullLogger<IndexerAdditionalSettingsParser>.Instance),
            logger,
            indexerStatusService: services.GetRequiredService<IIndexerStatusService>());

    /// <summary>
    /// The production registrations for the pieces on this path: the context factory exactly as
    /// <c>PersistenceRegistrationExtensions</c> adds it, and the repository and status service from
    /// the real search registration extensions. Over a migrated SQLite file, not the in-memory
    /// provider, because the in-memory provider cannot run ExecuteUpdate at all.
    /// </summary>
    private async Task<ServiceProvider> BuildServiceProviderAsync(UpdateRecorder recorder)
    {
        var databasePath = Path.Combine(FileService.GetTempPath(), $"backoff-race-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<ListenArrDbContext>(
            options => options.UseSqlite(connectionString).AddInterceptors(recorder),
            ServiceLifetime.Singleton);
        services.AddSearchInfrastructure();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IndexerBackoffStartupWindow>();
        services.AddScoped<IIndexerStatusService, IndexerStatusService>();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using var seed = await provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>().CreateDbContextAsync();
        await seed.Database.MigrateAsync();
        for (var id = 1; id <= IndexerCount; id++)
        {
            seed.Indexers.Add(new IndexerBuilder()
                .WithId(id)
                .WithName($"Torznab {id}")
                .WithType("Torrent")
                .WithImplementation("Torznab")
                .WithUrl($"https://indexer{id}.invalid")
                .Build());
        }
        await seed.SaveChangesAsync();

        recorder.Arm();
        return provider;
    }

    private static async Task<List<Indexer>> ReadIndexersAsync(IServiceProvider provider)
    {
        await using var context = await provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>().CreateDbContextAsync();
        return await context.Indexers.AsNoTracking().OrderBy(i => i.Id).ToListAsync();
    }

    public override async Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await base.DisposeAsync();
    }

    /// <summary>
    /// Records the context every Indexers UPDATE ran on, and holds each one until <c>expected</c>
    /// have arrived or a short timeout passes, so writes that can overlap do. The timeout is what
    /// lets a shared-context run finish when its siblings never reach this point.
    /// </summary>
    private sealed class UpdateRecorder(int expected) : DbCommandInterceptor
    {
        private static readonly TimeSpan HoldLimit = TimeSpan.FromSeconds(2);
        private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<DbContext?> _contexts = new();
        private int _armed;

        public IReadOnlyList<DbContext?> Contexts
        {
            get
            {
                lock (_contexts)
                {
                    return _contexts.ToList();
                }
            }
        }

        public void Arm() => Volatile.Write(ref _armed, 1);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _armed) == 1
                && command.CommandText.TrimStart().StartsWith("UPDATE \"Indexers\"", StringComparison.Ordinal))
            {
                int arrived;
                lock (_contexts)
                {
                    _contexts.Add(eventData.Context);
                    arrived = _contexts.Count;
                }

                if (arrived >= expected)
                {
                    _allArrived.TrySetResult();
                }

                try
                {
                    await _allArrived.Task.WaitAsync(HoldLimit, cancellationToken);
                }
                catch (TimeoutException)
                {
                    // Expected on a shared context: the siblings never got here.
                }
            }

            return result;
        }
    }

    /// <summary>Every indexer answers 503, which escalates it to rung 1.</summary>
    private sealed class UnavailableSearchProvider : IIndexerSearchProvider
    {
        public string IndexerType => "Torznab";

        public Task<IndexerQueryObservation> SearchAsync(
            Indexer indexer,
            string query,
            string? category = null,
            SearchRequest? request = null,
            CancellationToken ct = default) =>
            Task.FromResult(IndexerQueryObservation.Unavailable(
                IndexerQueryReason.HttpStatus,
                query,
                "503 Service Unavailable"));
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<string> _warnings = new();

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (_warnings)
                {
                    return _warnings.ToList();
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel != LogLevel.Warning)
            {
                return;
            }

            lock (_warnings)
            {
                _warnings.Add(formatter(state, exception));
            }
        }
    }
}
