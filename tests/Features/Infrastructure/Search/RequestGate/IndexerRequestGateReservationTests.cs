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

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Listenarr.Tests.Common;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestGate.IndexerRequestGateHarness;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestGate;

[Trait("Name", "IndexerRequestGateReservationTests")]
[Trait("Category", "Infrastructure")]
public sealed class IndexerRequestGateReservationTests : BaseTests
{
    private const int IndexerId = 7;
    private static readonly TimeSpan BoundedWait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task SendAsync_ThrowingReservation_DefersAsReservationUnavailableAndSendsNothing()
    {
        // Given
        var store = new RecordingStateStore();
        var reservation = new SpyReservation(reserve: (_, _) => throw new InvalidOperationException("ledger offline"));
        using var harness = new IndexerRequestGateHarness(
            store,
            Sequence(() => Status(HttpStatusCode.OK)),
            reservations: [reservation]);

        // When
        var result = await harness.SendAsync(IndexerId);

        // Then
        Assert.Equal(
            new RequestDeferral(Start.AddMinutes(1).UtcDateTime, RequestDeferralReason.ReservationUnavailable),
            Assert.IsType<IndexerRequestResult.Deferred>(result).Deferral);
        Assert.Equal(1, reservation.ReserveCalls);
        Assert.Equal(0, harness.Handler.Hits);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task SendAsync_Reservation_IsAskedAfterTheSlotAndCooldownRecheckAndBeforeDispatch()
    {
        // Given: cap 1; the first request holds the slot and then draws a cooldown.
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wireCalls = 0;
        var wireCallsAtReserve = new ConcurrentQueue<int>();
        var reservation = new SpyReservation(reserve: (_, _) =>
        {
            wireCallsAtReserve.Enqueue(Volatile.Read(ref wireCalls));
            return Task.FromResult<RequestDeferral?>(null);
        });
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            async (_, _) =>
            {
                Interlocked.Increment(ref wireCalls);
                firstEntered.TrySetResult();
                await releaseFirst.Task;
                return RateLimited(new RetryConditionHeaderValue(TimeSpan.FromHours(1)));
            },
            perIndexerCap: 1,
            reservations: [reservation]);

        // When
        var first = harness.SendAsync(IndexerId);
        await firstEntered.Task.WaitAsync(BoundedWait);
        var queued = harness.SendAsync(IndexerId);
        var reserveCallsWhileQueued = reservation.ReserveCalls;
        releaseFirst.SetResult();
        var firstResult = await first.WaitAsync(BoundedWait);
        var queuedResult = await queued.WaitAsync(BoundedWait);

        // Then: the queued request is never offered to the reservation, and the one that is
        // offered has not reached the wire yet.
        Assert.Equal(1, reserveCallsWhileQueued);
        var cooldown = new RequestDeferral(Start.AddHours(1).UtcDateTime, RequestDeferralReason.RateLimited);
        Assert.Equal(cooldown, Assert.IsType<IndexerRequestResult.Deferred>(firstResult).Deferral);
        Assert.Equal(cooldown, Assert.IsType<IndexerRequestResult.Deferred>(queuedResult).Deferral);
        Assert.Equal(1, reservation.ReserveCalls);
        Assert.Equal([0], wireCallsAtReserve);
        var context = Assert.Single(reservation.Reserved);
        Assert.Equal(new IndexerRequestContext(IndexerId, SearchOrigin.Sweep, IndexerRequestKind.Search), context);
        Assert.Equal(1, harness.Handler.Hits);
    }

    [Fact]
    public async Task SendAsync_CallerCancelsInsideReservation_PropagatesRecordsNothingAndFreesTheSlot()
    {
        // Given: cap 1, so a slot kept by the cancelled request would block the next one.
        var store = new RecordingStateStore();
        using var caller = new CancellationTokenSource();
        var reservation = new SpyReservation(reserve: async (_, cancellationToken) =>
        {
            if (cancellationToken.CanBeCanceled)
            {
                await caller.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return null;
        });
        using var harness = new IndexerRequestGateHarness(
            store,
            Sequence(() => Status(HttpStatusCode.OK)),
            perIndexerCap: 1,
            reservations: [reservation]);

        // When
        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.SendAsync(IndexerId, caller.Token).WaitAsync(BoundedWait));
        var next = await harness.SendAsync(IndexerId, CancellationToken.None).WaitAsync(BoundedWait);

        // Then
        Assert.Equal(caller.Token, thrown.CancellationToken);
        using var response = Assert.IsType<IndexerRequestResult.Sent>(next).Response;
        Assert.Equal(2, reservation.ReserveCalls);
        Assert.Equal(1, harness.Handler.Hits);
        Assert.Equal(0, store.Writes);
        Assert.Null(store.Row(IndexerId));
    }

    [Fact]
    public async Task SendAsync_ReservationCancelledWhileCallerTokenIsLive_DefersAsReservationUnavailable()
    {
        // Given: the reservation's own timeout fires; the caller has not cancelled.
        using var caller = new CancellationTokenSource();
        var reservation = new SpyReservation(reserve: (_, _) => throw new OperationCanceledException(new CancellationToken(canceled: true)));
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            Sequence(() => Status(HttpStatusCode.OK)),
            reservations: [reservation]);

        // When
        var result = await harness.SendAsync(IndexerId, caller.Token).WaitAsync(BoundedWait);

        // Then
        Assert.Equal(
            new RequestDeferral(Start.AddMinutes(1).UtcDateTime, RequestDeferralReason.ReservationUnavailable),
            Assert.IsType<IndexerRequestResult.Deferred>(result).Deferral);
        Assert.False(caller.IsCancellationRequested);
        Assert.Equal(0, harness.Handler.Hits);
    }
}
