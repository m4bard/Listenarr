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
using Listenarr.Tests.Features.Infrastructure.Search.RequestBudget;
using Listenarr.Tests.Features.Infrastructure.Search.RequestGate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestGate.IndexerRequestGateHarness;

namespace Listenarr.Tests.Features.Application.Search.Indexers.Common;

// The daily limit's failure paths through the real request gate: the reservation never catches
// a storage failure, so the gate defers the request and nothing reaches the indexer, and the
// caller's token alone decides whether a cancellation propagates.
[Trait("Name", "IndexerRequestBudgetFailureTests")]
[Trait("Category", "Application")]
public sealed class IndexerRequestBudgetFailureTests : BaseTests
{
    private const int DailyLimit = 10;
    private const int Spent = 3;
    private static readonly TimeSpan BoundedWait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Reservation_StorageFailure_DefersAndLeavesUsageUnchanged()
    {
        // Given: part of the limit spent, and a reservation whose every read and statement fails.
        await using var database = await IndexerRequestBudgetTestDatabase.CreateAsync();
        var indexerId = await database.AddIndexerAsync(DailyLimit);
        Assert.Equal(Spent, await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Sweep, stopAfter: Spent));
        var usageBefore = await database.UsageAsync();
        var fault = new StorageFault(reads: true, statements: true)
        {
            Fault = () => throw new SqliteException("simulated disk I/O error", 10)
        };
        await using var failing = database.CreateProvider(fault);
        var store = new RecordingStateStore();
        using var harness = new IndexerRequestGateHarness(
            store,
            Sequence(() => Status(HttpStatusCode.OK)),
            clock: database.Clock,
            reservations: [failing.GetRequiredService<IIndexerRequestReservation>()]);
        var unavailableUntil = database.Clock.GetUtcNow().UtcDateTime + IndexerRequestPolicy.UnavailableRetry;

        // When: the storage fails, and then recovers.
        var failed = await harness.SendAsync(indexerId).WaitAsync(BoundedWait);
        var usageAfterFailure = await database.UsageAsync();
        var hitsAfterFailure = harness.Handler.Hits;
        var faultsFired = fault.Fired;
        fault.Fault = null;
        var recovered = await harness.SendAsync(indexerId).WaitAsync(BoundedWait);

        // Then
        Assert.Equal(
            new RequestDeferral(unavailableUntil, RequestDeferralReason.ReservationUnavailable),
            Assert.IsType<IndexerRequestResult.Deferred>(failed).Deferral);
        Assert.True(faultsFired > 0);
        Assert.Equal(0, hitsAfterFailure);
        Assert.Equivalent(usageBefore, usageAfterFailure, strict: true);
        using var response = Assert.IsType<IndexerRequestResult.Sent>(recovered).Response;
        Assert.Equal(1, harness.Handler.Hits);
        Assert.Equal(Spent + 1, await database.SpentAsync(indexerId));
    }

    [Fact]
    public async Task Cancellation_IsTheCallersToken_CallerCancelledPropagatesAndRecordsNothing()
    {
        // Given: the caller cancels as the charge statement is about to run.
        await using var database = await IndexerRequestBudgetTestDatabase.CreateAsync();
        var indexerId = await database.AddIndexerAsync(DailyLimit);
        Assert.Equal(Spent, await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Sweep, stopAfter: Spent));
        var usageBefore = await database.UsageAsync();
        using var caller = new CancellationTokenSource();
        var fault = new StorageFault(reads: false, statements: true) { Fault = () => caller.Cancel() };
        await using var cancelling = database.CreateProvider(fault);
        var store = new RecordingStateStore();
        using var harness = new IndexerRequestGateHarness(
            store,
            Sequence(() => Status(HttpStatusCode.OK)),
            clock: database.Clock,
            reservations: [cancelling.GetRequiredService<IIndexerRequestReservation>()]);

        // When
        var send = () => harness.SendAsync(indexerId, caller.Token).WaitAsync(BoundedWait);

        // Then
        // EF's cancelled statement does not carry the caller's token, so the token itself is what
        // marks this as the caller's cancellation.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(send);
        Assert.True(caller.IsCancellationRequested);
        Assert.Equal(1, fault.Fired);
        Assert.Equal(0, harness.Handler.Hits);
        Assert.Equal(0, store.Writes);
        Assert.Equivalent(usageBefore, await database.UsageAsync(), strict: true);
    }

    [Fact]
    public async Task Cancellation_IsTheCallersToken_CancelledStatementWithLiveTokenIsReservationUnavailable()
    {
        // Given: the charge statement is cancelled by something other than the caller, as a
        // timeout would be.
        await using var database = await IndexerRequestBudgetTestDatabase.CreateAsync();
        var indexerId = await database.AddIndexerAsync(DailyLimit);
        Assert.Equal(Spent, await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Sweep, stopAfter: Spent));
        var usageBefore = await database.UsageAsync();
        using var caller = new CancellationTokenSource();
        var fault = new StorageFault(reads: false, statements: true)
        {
            Fault = () => throw new OperationCanceledException(new CancellationToken(canceled: true))
        };
        await using var failing = database.CreateProvider(fault);
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            Sequence(() => Status(HttpStatusCode.OK)),
            clock: database.Clock,
            reservations: [failing.GetRequiredService<IIndexerRequestReservation>()]);
        var unavailableUntil = database.Clock.GetUtcNow().UtcDateTime + IndexerRequestPolicy.UnavailableRetry;

        // When
        var result = await harness.SendAsync(indexerId, caller.Token).WaitAsync(BoundedWait);

        // Then
        Assert.Equal(
            new RequestDeferral(unavailableUntil, RequestDeferralReason.ReservationUnavailable),
            Assert.IsType<IndexerRequestResult.Deferred>(result).Deferral);
        Assert.False(caller.IsCancellationRequested);
        Assert.Equal(1, fault.Fired);
        Assert.Equal(0, harness.Handler.Hits);
        Assert.Equivalent(usageBefore, await database.UsageAsync(), strict: true);
    }

    [Fact]
    public async Task GetDeferral_CallerCancelledDuringPeek_PropagatesRatherThanDeferring()
    {
        // Given: an exhausted indexer, so a peek that completed would report a deferral, and a
        // caller that cancels as the peek reads.
        await using var database = await IndexerRequestBudgetTestDatabase.CreateAsync();
        var indexerId = await database.AddIndexerAsync(1);
        Assert.Equal(1, await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Interactive));
        using var caller = new CancellationTokenSource();
        var fault = new StorageFault(reads: true, statements: false) { Fault = () => caller.Cancel() };
        await using var cancelling = database.CreateProvider(fault);
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            Sequence(() => Status(HttpStatusCode.OK)),
            clock: database.Clock,
            reservations: [cancelling.GetRequiredService<IIndexerRequestReservation>()]);

        // When
        var peek = () => harness.Gate.GetDeferralAsync(indexerId, SearchOrigin.Interactive, caller.Token).WaitAsync(BoundedWait);

        // Then
        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(peek);
        Assert.Equal(caller.Token, thrown.CancellationToken);
        Assert.Equal(1, fault.Fired);
        Assert.Equal(0, harness.Handler.Hits);
    }

    /// <summary>
    /// Runs <see cref="Fault"/>, while it is set, before each read or statement of the chosen
    /// kinds, and counts how often it ran.
    /// </summary>
    private sealed class StorageFault(bool reads, bool statements) : DbCommandInterceptor
    {
        private int _fired;

        public Action? Fault { get; set; }

        public int Fired => Volatile.Read(ref _fired);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Run(reads);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Run(statements);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Run(bool applies)
        {
            if (applies && Fault is { } fault)
            {
                Interlocked.Increment(ref _fired);
                fault();
            }
        }
    }
}
