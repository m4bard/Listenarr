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
using System.Net;
using Listenarr.Tests.Common;
using Listenarr.Tests.Features.Infrastructure.Search.RequestGate;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestGate.IndexerRequestGateHarness;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestBudget;

// The non-charging check the gate's GetDeferralAsync makes: the same window and until-time as a
// refusal, no writes, and on a storage failure no answer rather than an error.
[Trait("Name", "EfIndexerRequestReservationPeekTests")]
[Trait("Category", "Infrastructure")]
public sealed class EfIndexerRequestReservationPeekTests : BaseTests
{
    [Fact]
    public async Task Peek_ExhaustedReportsDeferral_WritesNothing()
    {
        // Given: the whole limit spent, and the refusal a further charge gets.
        await using var database = await IndexerRequestBudgetTestDatabase.CreateAsync();
        var indexerId = await database.AddIndexerAsync(4);
        Assert.Equal(4, await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Interactive));
        var refusal = await database.ReserveAsync(indexerId, SearchOrigin.Interactive);
        var usageBefore = await database.UsageAsync();
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            Sequence(() => Status(HttpStatusCode.OK)),
            clock: database.Clock,
            reservations: [database.Reservation]);

        // When
        var peeked = await database.Reservation.PeekAsync(indexerId, SearchOrigin.Interactive, CancellationToken.None);
        var gateDeferral = await harness.Gate.GetDeferralAsync(indexerId, SearchOrigin.Interactive, CancellationToken.None);

        // Then
        Assert.NotNull(refusal);
        Assert.Equal(RequestDeferralReason.AllowanceExhausted, refusal.Value.Reason);
        Assert.Equal(refusal, peeked);
        Assert.Equal(refusal, gateDeferral);
        Assert.Equal(0, harness.Handler.Hits);
        Assert.Equivalent(usageBefore, await database.UsageAsync(), strict: true);
    }

    [Fact]
    public async Task Peek_BelowTheOriginsCeiling_ReturnsNull()
    {
        // Given: the sweep's share of a limit of 4 is 3, and 3 are spent.
        await using var database = await IndexerRequestBudgetTestDatabase.CreateAsync();
        var indexerId = await database.AddIndexerAsync(4);
        Assert.Equal(3, await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Sweep));

        // When
        var sweep = await database.Reservation.PeekAsync(indexerId, SearchOrigin.Sweep, CancellationToken.None);
        var interactive = await database.Reservation.PeekAsync(indexerId, SearchOrigin.Interactive, CancellationToken.None);

        // Then
        Assert.NotNull(sweep);
        Assert.Null(interactive);
        Assert.Equal(3, await database.SpentAsync(indexerId));
    }

    [Fact]
    public async Task Peek_NoLimit_ReturnsNull()
    {
        // Given
        await using var database = await IndexerRequestBudgetTestDatabase.CreateAsync();
        var indexerId = await database.AddIndexerAsync(null);

        // When
        var peeked = await database.Reservation.PeekAsync(indexerId, SearchOrigin.Sweep, CancellationToken.None);

        // Then
        Assert.Null(peeked);
    }

    [Fact]
    public async Task Peek_StorageFailure_ReturnsNull_WhereTheChargeThrows()
    {
        // Given: an exhausted indexer, so a peek that could read would report a deferral.
        await using var database = await IndexerRequestBudgetTestDatabase.CreateAsync();
        var indexerId = await database.AddIndexerAsync(1);
        Assert.Equal(1, await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Interactive));
        await using var failing = database.CreateProvider(
            new FailingReads(() => new InvalidOperationException("simulated storage failure")));
        var reservation = failing.GetRequiredService<IIndexerRequestReservation>();

        // When
        var peeked = await reservation.PeekAsync(indexerId, SearchOrigin.Interactive, CancellationToken.None);
        var charge = () => database.ReserveAsync(indexerId, SearchOrigin.Interactive, reservation: reservation);

        // Then
        Assert.NotNull(await database.Reservation.PeekAsync(indexerId, SearchOrigin.Interactive, CancellationToken.None));
        Assert.Null(peeked);
        await Assert.ThrowsAsync<InvalidOperationException>(charge);
        Assert.Equal(1, await database.SpentAsync(indexerId));
    }

    [Fact]
    public async Task Peek_CancelledWhileCallerTokenIsLive_ReturnsNull()
    {
        // Given: a read that is cancelled although the caller's token is not, as a timeout is.
        await using var database = await IndexerRequestBudgetTestDatabase.CreateAsync();
        var indexerId = await database.AddIndexerAsync(1);
        Assert.Equal(1, await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Interactive));
        await using var failing = database.CreateProvider(new FailingReads(() => new OperationCanceledException()));
        var reservation = failing.GetRequiredService<IIndexerRequestReservation>();

        // When
        var peeked = await reservation.PeekAsync(indexerId, SearchOrigin.Interactive, CancellationToken.None);

        // Then
        Assert.Null(peeked);
    }

    [Fact]
    public async Task Peek_CallerCancelled_Propagates()
    {
        // Given
        await using var database = await IndexerRequestBudgetTestDatabase.CreateAsync();
        var indexerId = await database.AddIndexerAsync(1);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // When
        var peek = () => database.Reservation.PeekAsync(indexerId, SearchOrigin.Interactive, cancellation.Token);

        // Then
        await Assert.ThrowsAnyAsync<OperationCanceledException>(peek);
    }

    private sealed class FailingReads(Func<Exception> failure) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            throw failure();
    }
}
