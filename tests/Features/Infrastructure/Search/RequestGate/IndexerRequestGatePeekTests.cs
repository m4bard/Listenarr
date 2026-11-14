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

[Trait("Name", "IndexerRequestGatePeekTests")]
[Trait("Category", "Infrastructure")]
public sealed class IndexerRequestGatePeekTests : BaseTests
{
    private const int IndexerId = 7;
    private static readonly TimeSpan BoundedWait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task GetDeferral_PeeksWithoutCharging()
    {
        // Given: the reason is whatever the reservation chooses; the gate passes it through.
        var deferral = new RequestDeferral(Start.AddMinutes(10).UtcDateTime, RequestDeferralReason.CycleRequestBound);
        var reservation = new SpyReservation(peek: (_, _, _) => Task.FromResult<RequestDeferral?>(deferral));
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            Sequence(() => Status(HttpStatusCode.OK)),
            reservations: [reservation]);

        // When
        var peeked = await harness.Gate.GetDeferralAsync(IndexerId, SearchOrigin.Interactive, CancellationToken.None);

        // Then
        Assert.Equal(deferral, peeked);
        Assert.Equal(1, reservation.PeekCalls);
        Assert.Equal(0, reservation.ReserveCalls);
        Assert.Equal(0, harness.Handler.Hits);
    }

    [Fact]
    public async Task GetDeferral_ThrowingPeek_GivesReservationUnavailable()
    {
        // Given
        var reservation = new SpyReservation(peek: (_, _, _) => throw new InvalidOperationException("ledger offline"));
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            Sequence(() => Status(HttpStatusCode.OK)),
            reservations: [reservation]);

        // When
        var peeked = await harness.Gate.GetDeferralAsync(IndexerId, SearchOrigin.Sweep, CancellationToken.None);

        // Then
        Assert.Equal(new RequestDeferral(Start.AddMinutes(1).UtcDateTime, RequestDeferralReason.ReservationUnavailable), peeked);
        Assert.Equal(0, reservation.ReserveCalls);
    }

    [Fact]
    public async Task GetDeferral_SeveralDeferrals_TheLatestUntilWins()
    {
        // Given: a 20 minute cooldown, and peeks of 5, none, 30 and 10 minutes.
        var reservations = new[] { 5, 0, 30, 10 }
            .Select(minutes => new SpyReservation(peek: (_, _, _) => Task.FromResult<RequestDeferral?>(minutes == 0
                ? null
                : new RequestDeferral(Start.AddMinutes(minutes).UtcDateTime, RequestDeferralReason.CycleRequestBound))))
            .ToList();
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            Sequence(() => RateLimited(new RetryConditionHeaderValue(TimeSpan.FromMinutes(20)))),
            reservations: reservations);
        var rateLimited = await harness.SendAsync(IndexerId);

        // When
        var peeked = await harness.Gate.GetDeferralAsync(IndexerId, SearchOrigin.Sweep, CancellationToken.None);

        // Then
        Assert.Equal(
            new RequestDeferral(Start.AddMinutes(20).UtcDateTime, RequestDeferralReason.RateLimited),
            Assert.IsType<IndexerRequestResult.Deferred>(rateLimited).Deferral);
        Assert.Equal(new RequestDeferral(Start.AddMinutes(30).UtcDateTime, RequestDeferralReason.CycleRequestBound), peeked);
        Assert.All(reservations, reservation => Assert.Equal(1, reservation.PeekCalls));
    }

    [Fact]
    public async Task GetDeferral_CallerCancelsInsidePeek_PropagatesAndRecordsNothing()
    {
        // Given
        var store = new RecordingStateStore();
        using var caller = new CancellationTokenSource();
        var reservation = new SpyReservation(peek: async (_, _, cancellationToken) =>
        {
            await caller.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        });
        using var harness = new IndexerRequestGateHarness(
            store,
            Sequence(() => Status(HttpStatusCode.OK)),
            reservations: [reservation]);

        // When
        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Gate.GetDeferralAsync(IndexerId, SearchOrigin.Sweep, caller.Token).WaitAsync(BoundedWait));

        // Then
        Assert.Equal(caller.Token, thrown.CancellationToken);
        Assert.Equal(0, reservation.ReserveCalls);
        Assert.Equal(0, store.Writes);
        Assert.Null(store.Row(IndexerId));
    }

    [Fact]
    public async Task GetDeferral_PeekCancelledWhileCallerTokenIsLive_GivesReservationUnavailable()
    {
        // Given: the peek's own timeout fires; the caller has not cancelled.
        using var caller = new CancellationTokenSource();
        var reservation = new SpyReservation(peek: (_, _, _) => throw new OperationCanceledException(new CancellationToken(canceled: true)));
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            Sequence(() => Status(HttpStatusCode.OK)),
            reservations: [reservation]);

        // When
        var peeked = await harness.Gate.GetDeferralAsync(IndexerId, SearchOrigin.Sweep, caller.Token).WaitAsync(BoundedWait);

        // Then
        Assert.Equal(new RequestDeferral(Start.AddMinutes(1).UtcDateTime, RequestDeferralReason.ReservationUnavailable), peeked);
        Assert.False(caller.IsCancellationRequested);
    }
}
