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

using System.Net;
using System.Net.Http.Headers;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Infrastructure.Search.RequestGate;
using Listenarr.Tests.Common;
using Listenarr.Tests.Features.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestGate.IndexerRequestGateHarness;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestGate;

[Trait("Name", "IndexerRequestGateConcurrentStateTests")]
[Trait("Category", "Infrastructure")]
public sealed class IndexerRequestGateConcurrentStateTests : BaseTests
{
    private static readonly TimeSpan BoundedWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LockProbe = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// A rate limit and a success for the same indexer are applied together, in either order.
    /// Whichever is applied first is held inside its database write; the other must wait for
    /// the indexer's lock rather than read or write around it. Runs with and without connection
    /// pooling, since production pools.
    /// </summary>
    [Theory]
    [InlineData("rate-limit-first", false)]
    [InlineData("rate-limit-first", true)]
    [InlineData("success-first", false)]
    [InlineData("success-first", true)]
    public async Task CooldownSurvivesConcurrentSuccess(string completionOrder, bool pooling)
    {
        // Given: one earlier failure, so the success has a count to clear inside the lock.
        await using var database = await IndexerRequestStateTestDatabase.CreateAsync(indexerCount: 1, pooling: pooling);
        var indexerId = database.IndexerIds[0];
        await new EfIndexerRequestStateStore(database.Factory).UpsertAsync(new IndexerRequestState
        {
            IndexerId = indexerId,
            ConsecutiveFailures = 1,
            UpdatedUtc = Start.UtcDateTime
        });
        var rateLimitFirst = completionOrder == "rate-limit-first";
        var store = new FirstWriteHoldingStore(new EfIndexerRequestStateStore(database.Factory));
        var rateLimitArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var successArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rateLimitComputed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondPastSeam = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var harness = new IndexerRequestGateHarness(
            store,
            async (_, _) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    rateLimitArrived.SetResult();
                    await successArrived.Task;
                    return RateLimited(new RetryConditionHeaderValue(TimeSpan.FromSeconds(3600)));
                }

                // The success is answered after the rate limit. A later request only reaches
                // the wire if the cooldown was lost.
                successArrived.TrySetResult();
                await rateLimitComputed.Task;
                return Status(HttpStatusCode.OK);
            },
            perIndexerCap: 2);
        harness.Cache.OnStateComputed = async transition =>
        {
            var isRateLimit = transition.Outcome == IndexerRequestOutcome.RateLimited;
            if (isRateLimit)
            {
                rateLimitComputed.SetResult();
            }

            if (isRateLimit != rateLimitFirst)
            {
                await store.FirstWriteEntered.Task.WaitAsync(BoundedWait);
                secondPastSeam.SetResult();
            }
        };

        // When
        var rateLimited = harness.SendAsync(indexerId);
        await rateLimitArrived.Task.WaitAsync(BoundedWait);
        var succeeded = harness.SendAsync(indexerId);
        await secondPastSeam.Task.WaitAsync(BoundedWait);
        var secondWroteWhileFirstHeld =
            await Task.WhenAny(store.SecondWriteEntered.Task, Task.Delay(LockProbe)) == store.SecondWriteEntered.Task;
        store.ReleaseFirstWrite.SetResult();
        var rateLimitedResult = await rateLimited.WaitAsync(BoundedWait);
        using var sent = Assert.IsType<IndexerRequestResult.Sent>(await succeeded.WaitAsync(BoundedWait)).Response;
        var nowUtc = harness.Clock.GetUtcNow().UtcDateTime;
        var reloaded = new IndexerRequestStateCache(
            new EfIndexerRequestStateStore(database.Factory),
            NullLogger<IndexerRequestStateCache>.Instance);
        var reloadedRows = await new EfIndexerRequestStateStore(database.Factory).LoadAllAsync();
        var third = await harness.SendAsync(indexerId);

        // Then: the rate limit was observed once the success reached the wire, a pacing
        // interval after the start.
        Assert.False(secondWroteWhileFirstHeld);
        var cooldown = new RequestDeferral((Start + Interval).AddSeconds(3600).UtcDateTime, RequestDeferralReason.RateLimited);
        Assert.Equal(cooldown, Assert.IsType<IndexerRequestResult.Deferred>(rateLimitedResult).Deferral);
        Assert.Equal(cooldown, harness.Cache.GetCooldown(indexerId, nowUtc));
        Assert.True(await reloaded.EnsureLoadedAsync(_ => { }, CancellationToken.None));
        Assert.Equal(cooldown, reloaded.GetCooldown(indexerId, nowUtc));
        Assert.Equal(0, Assert.Single(reloadedRows).ConsecutiveFailures);
        Assert.Equal(cooldown, Assert.IsType<IndexerRequestResult.Deferred>(third).Deferral);
        Assert.Equal(2, harness.Handler.Hits);
        Assert.Equal(2, store.Writes);
    }

    /// <summary>
    /// Holds the first write inside the store until released and signals when a second write
    /// arrives, so a test can see whether a write gets past the indexer's lock.
    /// </summary>
    private sealed class FirstWriteHoldingStore(IIndexerRequestStateStore inner) : IIndexerRequestStateStore
    {
        private int _writes;

        public int Writes => Volatile.Read(ref _writes);

        public TaskCompletionSource FirstWriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SecondWriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<IndexerRequestState>> LoadAllAsync(CancellationToken cancellationToken = default)
            => inner.LoadAllAsync(cancellationToken);

        public async Task UpsertAsync(IndexerRequestState state, CancellationToken cancellationToken = default)
        {
            await EnterWriteAsync();
            await inner.UpsertAsync(state, cancellationToken);
        }

        public async Task ClearConsecutiveFailuresAsync(int indexerId, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            await EnterWriteAsync();
            await inner.ClearConsecutiveFailuresAsync(indexerId, nowUtc, cancellationToken);
        }

        private async Task EnterWriteAsync()
        {
            switch (Interlocked.Increment(ref _writes))
            {
                case 1:
                    FirstWriteEntered.SetResult();
                    await ReleaseFirstWrite.Task.WaitAsync(BoundedWait);
                    break;
                case 2:
                    SecondWriteEntered.SetResult();
                    break;
            }
        }
    }
}
