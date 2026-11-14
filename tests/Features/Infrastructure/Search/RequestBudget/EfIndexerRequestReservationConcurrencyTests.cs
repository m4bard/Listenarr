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
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestBudget;

// The charge is one statement, so callers that race for the last units of an indexer's limit
// can never take more than the ceiling between them: in one process, across many contexts and
// across two service providers over the same file, in both journal modes.
[Trait("Name", "EfIndexerRequestReservationConcurrencyTests")]
[Trait("Category", "Infrastructure")]
public sealed class EfIndexerRequestReservationConcurrencyTests : BaseTests
{
    private static readonly TimeSpan BoundedWait = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task Reservation_ParallelAcrossContexts_AdmitsExactlyTheCeiling(bool writeAheadLog, int providers)
    {
        // Given: a limit of 50, half the callers interactive and half the sweep, spread over
        // one or two service providers.
        await using var database = await IndexerRequestBudgetTestDatabase.CreateAsync(writeAheadLog);
        var indexerId = await database.AddIndexerAsync(50);
        var reservations = new List<IIndexerRequestReservation> { database.Reservation };
        for (var extra = 1; extra < providers; extra++)
        {
            reservations.Add(database.CreateProvider().GetRequiredService<IIndexerRequestReservation>());
        }

        using var start = new ManualResetEventSlim(false);
        var attempts = Enumerable.Range(0, 200).Select(i => Task.Run(() =>
        {
            start.Wait();
            var origin = i % 2 == 0 ? SearchOrigin.Interactive : SearchOrigin.Sweep;
            return database.ReserveAsync(indexerId, origin, reservation: reservations[i % reservations.Count]);
        })).ToList();

        // When
        start.Set();
        var results = await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromMinutes(2));

        // Then: a busy database would have thrown out of the charge, failing the wait above.
        Assert.Equal(50, results.Count(result => result is null));
        Assert.All(
            results.Where(result => result is not null),
            result => Assert.Equal(RequestDeferralReason.AllowanceExhausted, result!.Value.Reason));
        Assert.Equal(50, await database.SpentAsync(indexerId));
    }

    [Fact]
    public async Task Reservation_TwoCallersBothPastTheirReadsBeforeEitherWrites_AdmitsOne()
    {
        // Given: a ceiling of 1 and two callers on separate contexts. Each caller's charge is
        // held until both have reached it, so everything either reads before its write has
        // been read by both before either writes.
        var barrier = new ChargeBarrier(parties: 2);
        await using var database = await IndexerRequestBudgetTestDatabase.CreateAsync(interceptors: barrier);
        var indexerId = await database.AddIndexerAsync(1);

        // When
        var results = await Task.WhenAll(
            Task.Run(() => database.ReserveAsync(indexerId, SearchOrigin.Interactive)),
            Task.Run(() => database.ReserveAsync(indexerId, SearchOrigin.Interactive)))
            .WaitAsync(BoundedWait * 2);

        // Then
        Assert.True(barrier.BothArrived, "the two charges did not overlap");
        Assert.Equal(1, results.Count(result => result is null));
        Assert.Equal(1, await database.SpentAsync(indexerId));
    }

    private sealed class ChargeBarrier(int parties) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public bool BothArrived => _allArrived.Task.IsCompletedSuccessfully;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"IndexerRequestUsages\"", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _arrived) == parties)
                {
                    _allArrived.TrySetResult();
                }

                await _allArrived.Task.WaitAsync(BoundedWait, cancellationToken);
            }

            return result;
        }
    }
}
