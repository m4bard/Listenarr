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
using Listenarr.Tests.Common;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestGate.IndexerRequestGateHarness;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestGate;

[Trait("Name", "IndexerRequestGateStateLoadTests")]
[Trait("Category", "Infrastructure")]
public sealed class IndexerRequestGateStateLoadTests : BaseTests
{
    private const int IndexerId = 7;
    private static readonly TimeSpan BoundedWait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task StateLoadFailure_DefersNotSends()
    {
        // Given: the first load fails, the second succeeds.
        var store = new FirstLoadFailingStore(_ => throw new InvalidOperationException("database is locked"));
        using var harness = new IndexerRequestGateHarness(store, Sequence(() => Status(HttpStatusCode.OK)));

        // When
        var first = await harness.SendAsync(IndexerId);
        var hitsAfterFirst = harness.Handler.Hits;
        var next = await harness.SendAsync(IndexerId);

        // Then
        Assert.Equal(
            new RequestDeferral(Start.AddMinutes(1).UtcDateTime, RequestDeferralReason.RequestStateUnavailable),
            Assert.IsType<IndexerRequestResult.Deferred>(first).Deferral);
        Assert.Equal(0, hitsAfterFirst);
        using var response = Assert.IsType<IndexerRequestResult.Sent>(next).Response;
        Assert.Equal(1, harness.Handler.Hits);
        Assert.Equal(2, store.Loads);
    }

    [Fact]
    public async Task StateLoadFailure_GetDeferralReportsUnavailableUntilTheStateLoads()
    {
        // Given
        var store = new FirstLoadFailingStore(_ => throw new InvalidOperationException("database is locked"));
        using var harness = new IndexerRequestGateHarness(store, Sequence(() => Status(HttpStatusCode.OK)));

        // When
        var first = await harness.Gate.GetDeferralAsync(IndexerId, SearchOrigin.Sweep, CancellationToken.None);
        var next = await harness.Gate.GetDeferralAsync(IndexerId, SearchOrigin.Sweep, CancellationToken.None);

        // Then
        Assert.Equal(new RequestDeferral(Start.AddMinutes(1).UtcDateTime, RequestDeferralReason.RequestStateUnavailable), first);
        Assert.Null(next);
        Assert.Equal(2, store.Loads);
    }

    [Fact]
    public async Task StateLoad_CallerCancelsDuringLoad_PropagatesAndTheNextCallLoadsAndSends()
    {
        // Given
        using var caller = new CancellationTokenSource();
        var store = new FirstLoadFailingStore(async cancellationToken =>
        {
            await caller.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
        });
        using var harness = new IndexerRequestGateHarness(store, Sequence(() => Status(HttpStatusCode.OK)));

        // When
        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.SendAsync(IndexerId, caller.Token).WaitAsync(BoundedWait));
        var hitsAfterCancel = harness.Handler.Hits;
        var next = await harness.SendAsync(IndexerId).WaitAsync(BoundedWait);

        // Then
        Assert.Equal(caller.Token, thrown.CancellationToken);
        Assert.Equal(0, hitsAfterCancel);
        using var response = Assert.IsType<IndexerRequestResult.Sent>(next).Response;
        Assert.Equal(1, harness.Handler.Hits);
        Assert.Equal(2, store.Loads);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task StateLoad_CancelledWhileCallerTokenIsLive_DefersAsRequestStateUnavailable()
    {
        // Given: the store's own timeout fires; the caller has not cancelled.
        using var caller = new CancellationTokenSource();
        var store = new FirstLoadFailingStore(_ => throw new OperationCanceledException(new CancellationToken(canceled: true)));
        using var harness = new IndexerRequestGateHarness(store, Sequence(() => Status(HttpStatusCode.OK)));

        // When
        var result = await harness.SendAsync(IndexerId, caller.Token).WaitAsync(BoundedWait);

        // Then
        Assert.Equal(
            new RequestDeferral(Start.AddMinutes(1).UtcDateTime, RequestDeferralReason.RequestStateUnavailable),
            Assert.IsType<IndexerRequestResult.Deferred>(result).Deferral);
        Assert.False(caller.IsCancellationRequested);
        Assert.Equal(0, harness.Handler.Hits);
    }

    /// <summary>
    /// An in-memory store whose first load runs the given failure; later loads succeed.
    /// </summary>
    private sealed class FirstLoadFailingStore(Func<CancellationToken, Task> failFirstLoad) : IIndexerRequestStateStore
    {
        private readonly RecordingStateStore _inner = new();
        private int _loads;

        public int Loads => Volatile.Read(ref _loads);

        public int Writes => _inner.Writes;

        public async Task<IReadOnlyList<IndexerRequestState>> LoadAllAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _loads) == 1)
            {
                await failFirstLoad(cancellationToken);
            }

            return await _inner.LoadAllAsync(cancellationToken);
        }

        public Task UpsertAsync(IndexerRequestState state, CancellationToken cancellationToken = default)
            => _inner.UpsertAsync(state, cancellationToken);

        public Task ClearConsecutiveFailuresAsync(int indexerId, DateTime nowUtc, CancellationToken cancellationToken = default)
            => _inner.ClearConsecutiveFailuresAsync(indexerId, nowUtc, cancellationToken);
    }
}
