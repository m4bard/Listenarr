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
using Listenarr.Tests.Common;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestGate.IndexerRequestGateHarness;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestGate;

[Trait("Name", "IndexerRequestGateTests")]
[Trait("Category", "Infrastructure")]
public sealed class IndexerRequestGateTests : BaseTests
{
    private const int IndexerId = 7;
    private static readonly TimeSpan BoundedWait = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("delta-seconds")]
    [InlineData("http-date")]
    public async Task SendAsync_RateLimitedWithRetryAfter_DefersUntilRetryAfterWithoutWireHitsBeforeIt(string retryAfterForm)
    {
        // Given
        var until = Start.AddSeconds(retryAfterForm == "http-date" ? 300 : 120);
        var retryAfter = retryAfterForm == "http-date"
            ? new RetryConditionHeaderValue(until)
            : new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            Sequence(() => RateLimited(retryAfter), () => Status(HttpStatusCode.OK)));

        // When
        var rateLimited = await harness.SendAsync(IndexerId);
        harness.Clock.Advance(until - harness.Clock.GetUtcNow() - TimeSpan.FromSeconds(1));
        var justBefore = await harness.SendAsync(IndexerId);
        var peeked = await harness.Gate.GetDeferralAsync(IndexerId, SearchOrigin.Sweep, CancellationToken.None);
        var hitsBeforeUntil = harness.Handler.Hits;
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        var atUntil = await harness.SendAsync(IndexerId);

        // Then
        var expected = new RequestDeferral(until.UtcDateTime, RequestDeferralReason.RateLimited);
        Assert.Equal(expected, Assert.IsType<IndexerRequestResult.Deferred>(rateLimited).Deferral);
        Assert.Equal(expected, Assert.IsType<IndexerRequestResult.Deferred>(justBefore).Deferral);
        Assert.Equal(expected, peeked);
        Assert.Equal(1, hitsBeforeUntil);
        Assert.True(harness.Handler.Responses.TryPeek(out var rateLimitResponse));
        Assert.True(Assert.IsType<TrackedContent>(rateLimitResponse.Content).Disposed);
        using var sent = Assert.IsType<IndexerRequestResult.Sent>(atUntil).Response;
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        Assert.Equal(2, harness.Handler.Hits);
    }

    [Fact]
    public async Task SendAsync_ServiceUnavailableWithRetryAfter_DefersAsRateLimited()
    {
        // Given
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            Sequence(() => RateLimited(new RetryConditionHeaderValue(TimeSpan.FromSeconds(90)), HttpStatusCode.ServiceUnavailable)));

        // When
        var result = await harness.SendAsync(IndexerId);
        var next = await harness.SendAsync(IndexerId);

        // Then
        var expected = new RequestDeferral(Start.AddSeconds(90).UtcDateTime, RequestDeferralReason.RateLimited);
        Assert.Equal(expected, Assert.IsType<IndexerRequestResult.Deferred>(result).Deferral);
        Assert.Equal(expected, Assert.IsType<IndexerRequestResult.Deferred>(next).Deferral);
        Assert.Equal(1, harness.Handler.Hits);
    }

    [Fact]
    public async Task SendAsync_CapOne_RequestQueuedBehindRateLimitIsDeferredAfterSlotWaitWithoutWireHit()
    {
        // Given
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            async (_, _) =>
            {
                if (Interlocked.Increment(ref calls) > 1)
                {
                    return Status(HttpStatusCode.OK);
                }

                firstEntered.SetResult();
                await releaseFirst.Task;
                return RateLimited(new RetryConditionHeaderValue(TimeSpan.FromHours(1)));
            },
            perIndexerCap: 1);

        // When
        var first = harness.SendAsync(IndexerId);
        await firstEntered.Task.WaitAsync(BoundedWait);
        var queued = harness.SendAsync(IndexerId);
        var queuedBeforeRelease = queued.IsCompleted;
        releaseFirst.SetResult();
        var firstResult = await first.WaitAsync(BoundedWait);
        var queuedResult = await queued.WaitAsync(BoundedWait);

        // Then
        Assert.False(queuedBeforeRelease);
        var expected = new RequestDeferral(Start.AddHours(1).UtcDateTime, RequestDeferralReason.RateLimited);
        Assert.Equal(expected, Assert.IsType<IndexerRequestResult.Deferred>(firstResult).Deferral);
        Assert.Equal(expected, Assert.IsType<IndexerRequestResult.Deferred>(queuedResult).Deferral);
        Assert.Equal(1, harness.Handler.Hits);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SendAsync_UnattributedIndexer_IsNeitherPacedNorCooledDownNorRecorded(int unattributedId)
    {
        // Given
        var store = new RecordingStateStore();
        using var harness = new IndexerRequestGateHarness(
            store,
            Sequence(
                () => Status(HttpStatusCode.InternalServerError),
                () => Status(HttpStatusCode.InternalServerError),
                () => Status(HttpStatusCode.InternalServerError),
                () => RateLimited(new RetryConditionHeaderValue(TimeSpan.FromSeconds(60))),
                () => Status(HttpStatusCode.OK)));

        // When
        var results = new List<IndexerRequestResult>();
        for (var i = 0; i < 5; i++)
        {
            results.Add(await harness.SendAsync(unattributedId));
        }

        // Then
        Assert.All(results.Take(3), result => Assert.IsType<IndexerRequestResult.Sent>(result));
        Assert.Equal(
            new RequestDeferral(Start.AddSeconds(60).UtcDateTime, RequestDeferralReason.RateLimited),
            Assert.IsType<IndexerRequestResult.Deferred>(results[3]).Deferral);
        Assert.IsType<IndexerRequestResult.Sent>(results[4]);
        Assert.Equal(5, harness.Handler.Hits);
        Assert.Empty(harness.Clock.Delays);
        Assert.Equal(0, store.Writes);
        Assert.Null(store.Row(unattributedId));
        Assert.Null(await harness.Gate.GetDeferralAsync(unattributedId, SearchOrigin.Sweep, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SendAsync_UnattributedIndexer_IsNeverOfferedToAReservationOrPeek(int unattributedId)
    {
        // Given
        var reservation = new SpyReservation();
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            Sequence(() => Status(HttpStatusCode.OK)),
            reservations: [reservation]);

        // When
        var result = await harness.SendAsync(unattributedId);
        var peeked = await harness.Gate.GetDeferralAsync(unattributedId, SearchOrigin.Sweep, CancellationToken.None);

        // Then
        Assert.Equal(0, reservation.ReserveCalls);
        Assert.Equal(0, reservation.PeekCalls);
        using var response = Assert.IsType<IndexerRequestResult.Sent>(result).Response;
        Assert.Null(peeked);
        Assert.Equal(1, harness.Handler.Hits);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SendAsync_UnattributedIndexerWithSpentCycleAllowance_IsDeferredAsCycleRequestBound(int unattributedId)
    {
        // Given
        var allowance = new CycleRequestAllowance(1);
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            Sequence(() => Status(HttpStatusCode.OK)));

        // When
        var first = await harness.SendAsync(unattributedId, cycleAllowance: allowance);
        var second = await harness.SendAsync(unattributedId, cycleAllowance: allowance);

        // Then
        using var response = Assert.IsType<IndexerRequestResult.Sent>(first).Response;
        Assert.Equal(
            new RequestDeferral(Start.UtcDateTime, RequestDeferralReason.CycleRequestBound),
            Assert.IsType<IndexerRequestResult.Deferred>(second).Deferral);
        Assert.Equal(1, allowance.Taken);
        Assert.Equal(1, harness.Handler.Hits);
    }

    [Fact]
    public async Task SendAsync_UnattributedIndexer_StillHoldsTheGlobalCap()
    {
        // Given
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            async (_, _) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    firstEntered.SetResult();
                    await releaseFirst.Task;
                }

                return Status(HttpStatusCode.OK);
            },
            perIndexerCap: 2,
            globalCap: 1);

        // When
        var first = harness.SendAsync(0);
        await firstEntered.Task.WaitAsync(BoundedWait);
        var second = harness.SendAsync(IndexerId);
        var secondBeforeRelease = second.IsCompleted;
        releaseFirst.SetResult();
        using var firstResponse = Assert.IsType<IndexerRequestResult.Sent>(await first.WaitAsync(BoundedWait)).Response;
        using var secondResponse = Assert.IsType<IndexerRequestResult.Sent>(await second.WaitAsync(BoundedWait)).Response;

        // Then
        Assert.False(secondBeforeRelease);
        Assert.Equal(2, harness.Handler.Hits);
    }
}
