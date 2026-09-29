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
/// Overlap is forced rather than hoped for. <see cref="UpdateGate"/> holds every backoff UPDATE at
/// the command interceptor until as many of them have arrived as the test expects, or until a short
/// timeout. On a shared context the second write never reaches the interceptor (EF throws first),
/// so the gate times out and the collision is certain; with a context per write all of them arrive
/// and the gate opens at once. Without the gate the writes are short enough to miss each other most
/// of the time, which is also why production saw this once in a day and not on every search.
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
        var gate = new UpdateGate(expected: IndexerCount);
        await using var provider = await BuildServiceProviderAsync(gate);
        var logger = new RecordingLogger<IndexerSearchWorkflow>();

        // When
        await using (var scope = provider.CreateAsyncScope())
        {
            var workflow = BuildWorkflow(scope.ServiceProvider, logger);
            await workflow.SearchIndexersAsync("the time machine");
        }

        // Then: every indexer is on rung 1, not just the one that won the race.
        var stored = await ReadIndexersAsync(provider);
        Assert.All(stored, indexer =>
        {
            Assert.Equal(1, indexer.EscalationLevel);
            Assert.NotNull(indexer.DisabledTill);
            Assert.Equal(nameof(IndexerQueryReason.HttpStatus), indexer.LastFailureReason);
        });
        Assert.DoesNotContain(logger.Warnings, w => w.StartsWith("Failed to record failure backoff state", StringComparison.Ordinal));

        // And: the writes really did overlap. Without this the test would also pass on a
        // workflow that happened to record outcomes one at a time.
        Assert.Equal(IndexerCount, gate.MaxInFlight);
    }

    [Fact]
    [Trait("Method", "UpdateBackoffStateAsync")]
    [Trait("Scenario", "ConcurrentWritesThroughOneRepository")]
    public async Task UpdateBackoffStateAsync_ConcurrentCallsOnOneScopedRepository_AllLand()
    {
        // Given: the DI-resolved repository, the one every indexer in a search shares.
        var gate = new UpdateGate(expected: IndexerCount);
        await using var provider = await BuildServiceProviderAsync(gate);
        var till = new DateTime(2026, 9, 14, 18, 0, 0, DateTimeKind.Utc);

        // When: one write per indexer, all at once
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IIndexerRepository>();

            // The scope has already read the indexer list, as SearchIndexersAsync does before its
            // fan-out, so the context is initialised and a collision reports as the production one.
            await repository.GetEnabledAsync(isAutomaticSearch: false);

            await Task.WhenAll(Enumerable.Range(1, IndexerCount).Select(id => Task.Run(() =>
                repository.UpdateBackoffStateAsync(
                    id,
                    new IndexerBackoffState(till.AddHours(-1), till.AddHours(-1), id, till, "Timeout")))));
        }

        // Then: each row carries its own rung, so no write was lost or applied to the wrong row.
        var stored = await ReadIndexersAsync(provider);
        Assert.Equal(Enumerable.Range(1, IndexerCount), stored.Select(i => i.EscalationLevel));
        Assert.All(stored, indexer => Assert.Equal(till, indexer.DisabledTill));
    }

    [Fact]
    [Trait("Scenario", "ControlSharedContextCollides")]
    public async Task Control_TwoGatedUpdatesOnOneContext_Collide()
    {
        // The apparatus check. If the gate did not hold a write open, the tests above would pass
        // on the shared-context code too and prove nothing. Two writes through one context under
        // the same gate must fail in exactly the way production logged.
        var gate = new UpdateGate(expected: 2);
        await using var provider = await BuildServiceProviderAsync(gate);

        await using var scope = provider.CreateAsyncScope();
        var shared = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();
        await shared.Indexers.AsNoTracking().ToListAsync();

        var writes = Enumerable.Range(1, 2).Select(id => Task.Run(() => shared.Indexers
            .Where(i => i.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.EscalationLevel, 5)))).ToArray();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.WhenAll(writes));
        Assert.Contains("A second operation was started on this context instance", failure.Message);
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
    private async Task<ServiceProvider> BuildServiceProviderAsync(UpdateGate gate)
    {
        var databasePath = Path.Combine(FileService.GetTempPath(), $"backoff-race-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<ListenArrDbContext>(
            options => options.UseSqlite(connectionString).AddInterceptors(gate),
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

        gate.Arm();
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
    /// Holds each Indexers UPDATE until <c>expected</c> of them are in flight together, or a short
    /// timeout passes. The timeout is what lets the shared-context case finish: there, only one
    /// write ever gets this far.
    /// </summary>
    private sealed class UpdateGate(int expected) : DbCommandInterceptor
    {
        private static readonly TimeSpan HoldLimit = TimeSpan.FromSeconds(2);
        private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _armed;
        private int _inFlight;
        private int _maxInFlight;

        public int MaxInFlight => Volatile.Read(ref _maxInFlight);

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
                var now = Interlocked.Increment(ref _inFlight);
                int seen;
                while ((seen = Volatile.Read(ref _maxInFlight)) < now
                       && Interlocked.CompareExchange(ref _maxInFlight, now, seen) != seen)
                {
                }

                if (now >= expected)
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

        public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => Leave(command);

        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            Leave(command);
            return Task.CompletedTask;
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Leave(command);
            return ValueTask.FromResult(result);
        }

        private void Leave(DbCommand command)
        {
            if (Volatile.Read(ref _armed) == 1
                && command.CommandText.TrimStart().StartsWith("UPDATE \"Indexers\"", StringComparison.Ordinal))
            {
                Interlocked.Decrement(ref _inFlight);
            }
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
