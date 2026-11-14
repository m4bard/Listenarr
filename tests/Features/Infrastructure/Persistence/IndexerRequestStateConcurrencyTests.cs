/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using System.Collections.Concurrent;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Infrastructure.Search.RequestGate;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// Many callers using the indexer request state store, and the cache over it, at once. Each
/// case runs with and without connection pooling (production pools) and in both journal modes
/// (production switches the file to WAL at startup).
/// </summary>
[Trait("Name", "IndexerRequestStateConcurrencyTests")]
[Trait("Category", "Infrastructure")]
public sealed class IndexerRequestStateConcurrencyTests : BaseTests
{
    private const int IndexerCount = 5;
    private const int Callers = 200;
    private static readonly TimeSpan BoundedWait = TimeSpan.FromSeconds(60);
    private static readonly DateTime ClockUtc = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(false, "delete")]
    [InlineData(true, "delete")]
    [InlineData(false, "wal")]
    [InlineData(true, "wal")]
    public async Task Store_ParallelUpsertsClearsAndLoads_EveryCallSucceedsAndNoRowIsTorn(bool pooling, string journalMode)
    {
        // Given
        await using var database = await CreateDatabaseAsync(pooling, journalMode);
        var store = new EfIndexerRequestStateStore(database.Factory);
        var upserts = Enumerable.Range(0, Callers)
            .Where(caller => caller % 3 == 0)
            .Select(caller => UpsertedState(database.IndexerIds[caller % IndexerCount], caller))
            .ToList();
        var loads = new ConcurrentBag<IReadOnlyList<IndexerRequestState>>();

        // When: every third caller upserts, the next clears and the next loads.
        await RunTogetherAsync(caller => (caller % 3) switch
        {
            0 => store.UpsertAsync(UpsertedState(database.IndexerIds[caller % IndexerCount], caller)),
            1 => store.ClearConsecutiveFailuresAsync(database.IndexerIds[caller % IndexerCount], ClockUtc.AddSeconds(caller)),
            _ => LoadIntoAsync(store, loads)
        });
        var final = await store.LoadAllAsync();

        // Then: every row read, during the run or after it, is one whole upsert, optionally with
        // its count cleared.
        Assert.Equal(Callers / 3, loads.Count);
        Assert.Equal(database.IndexerIds.Order(), final.Select(row => row.IndexerId).Order());
        foreach (var row in loads.SelectMany(rows => rows).Concat(final))
        {
            Assert.Contains(
                upserts,
                upsert => upsert.IndexerId == row.IndexerId
                    && upsert.CooldownUntilUtc == row.CooldownUntilUtc
                    && upsert.CooldownReason == row.CooldownReason
                    && upsert.LastDispatchUtc == row.LastDispatchUtc
                    && (row.ConsecutiveFailures == 0 || row.ConsecutiveFailures == upsert.ConsecutiveFailures));
        }
    }

    [Theory]
    [InlineData(false, "delete")]
    [InlineData(true, "delete")]
    [InlineData(false, "wal")]
    [InlineData(true, "wal")]
    public async Task Cache_ParallelResultsAndLoads_EveryWriteSucceedsAndTheDatabaseMatchesMemory(bool pooling, string journalMode)
    {
        // Given
        await using var database = await CreateDatabaseAsync(pooling, journalMode);
        var store = new FailureRecordingStore(new EfIndexerRequestStateStore(database.Factory));
        var cache = new IndexerRequestStateCache(store, NullLogger<IndexerRequestStateCache>.Instance);
        Assert.True(await cache.EnsureLoadedAsync(_ => { }, CancellationToken.None));
        var freshCaches = Enumerable.Range(0, Callers / 4)
            .Select(_ => new IndexerRequestStateCache(store, NullLogger<IndexerRequestStateCache>.Instance))
            .ToList();

        // When: results for every indexer arrive together while new caches load.
        await RunTogetherAsync(caller => caller % 4 == 3
            ? freshCaches[caller / 4].EnsureLoadedAsync(_ => { }, CancellationToken.None)
            : cache.ApplyAsync(Transition(database.IndexerIds[caller % IndexerCount], caller)));
        var reloaded = new IndexerRequestStateCache(
            new EfIndexerRequestStateStore(database.Factory),
            NullLogger<IndexerRequestStateCache>.Instance);
        Assert.True(await reloaded.EnsureLoadedAsync(_ => { }, CancellationToken.None));

        // Then: the cache swallows store failures, so they are recorded and asserted here.
        Assert.Empty(store.Failures);
        Assert.True(store.Writes > 0);
        foreach (var indexerId in database.IndexerIds)
        {
            Assert.Equal(cache.GetCooldown(indexerId, ClockUtc), reloaded.GetCooldown(indexerId, ClockUtc));
        }
    }

    private static async Task<IndexerRequestStateTestDatabase> CreateDatabaseAsync(bool pooling, string journalMode)
    {
        var database = await IndexerRequestStateTestDatabase.CreateAsync(IndexerCount, pooling: pooling);
        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA journal_mode={journalMode}";
        Assert.Equal(journalMode, Convert.ToString(await command.ExecuteScalarAsync()));
        return database;
    }

    /// <summary>Starts every caller on its own thread pool work item, then releases them together.</summary>
    private static async Task RunTogetherAsync(Func<int, Task> call)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callers = Enumerable.Range(0, Callers)
            .Select(caller => Task.Run(async () =>
            {
                await start.Task;
                await call(caller);
            }))
            .ToList();
        start.SetResult();
        await Task.WhenAll(callers).WaitAsync(BoundedWait);
    }

    private static async Task LoadIntoAsync(
        EfIndexerRequestStateStore store,
        ConcurrentBag<IReadOnlyList<IndexerRequestState>> loads)
        => loads.Add(await store.LoadAllAsync());

    private static IndexerRequestState UpsertedState(int indexerId, int caller) => new()
    {
        IndexerId = indexerId,
        ConsecutiveFailures = caller + 1,
        CooldownUntilUtc = ClockUtc.AddMinutes(caller),
        CooldownReason = "RateLimited",
        LastDispatchUtc = ClockUtc.AddSeconds(-caller),
        UpdatedUtc = ClockUtc.AddSeconds(caller)
    };

    private static IndexerRequestTransition Transition(int indexerId, int caller)
    {
        var observedUtc = ClockUtc.AddSeconds(caller);
        return (caller % 3) switch
        {
            0 => new IndexerRequestTransition(indexerId, IndexerRequestOutcome.Failed, observedUtc, observedUtc),
            1 => new IndexerRequestTransition(indexerId, IndexerRequestOutcome.Succeeded, observedUtc, observedUtc),
            _ => new IndexerRequestTransition(
                indexerId,
                IndexerRequestOutcome.RateLimited,
                observedUtc,
                observedUtc,
                TimeSpan.FromMinutes(caller))
        };
    }

    /// <summary>Records every store failure before passing it on.</summary>
    private sealed class FailureRecordingStore(IIndexerRequestStateStore inner) : IIndexerRequestStateStore
    {
        private int _writes;

        public ConcurrentQueue<Exception> Failures { get; } = new();

        public int Writes => Volatile.Read(ref _writes);

        public Task<IReadOnlyList<IndexerRequestState>> LoadAllAsync(CancellationToken cancellationToken = default)
            => RecordAsync(() => inner.LoadAllAsync(cancellationToken));

        public Task UpsertAsync(IndexerRequestState state, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _writes);
            return RecordAsync(async () =>
            {
                await inner.UpsertAsync(state, cancellationToken);
                return true;
            });
        }

        public Task ClearConsecutiveFailuresAsync(int indexerId, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _writes);
            return RecordAsync(async () =>
            {
                await inner.ClearConsecutiveFailuresAsync(indexerId, nowUtc, cancellationToken);
                return true;
            });
        }

        private async Task<T> RecordAsync<T>(Func<Task<T>> call)
        {
            try
            {
                return await call();
            }
            catch (Exception ex)
            {
                Failures.Enqueue(ex);
                throw;
            }
        }
    }
}
