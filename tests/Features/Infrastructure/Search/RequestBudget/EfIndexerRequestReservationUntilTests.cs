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

using Listenarr.Tests.Common;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestBudget.IndexerRequestBudgetTestDatabase;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestBudget;

// When a refused request may next be tried: the policy's walk over the buckets the database
// holds, on the hour the charge itself used.
[Trait("Name", "EfIndexerRequestReservationUntilTests")]
[Trait("Category", "Infrastructure")]
public sealed class EfIndexerRequestReservationUntilTests : BaseTests
{
    private static readonly DateTime FirstHour = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Refusal_UntilIsWhenHeadroomReturns()
    {
        // Given: limit 10 and no reserve; 4 requests in the first hour and 6 three hours later.
        await using var database = await CreateAsync();
        await database.SetReservePercentAsync(0);
        var indexerId = await database.AddIndexerAsync(10);
        Assert.Equal(4, await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Sweep, stopAfter: 4));
        database.Clock.Advance(TimeSpan.FromHours(3));
        Assert.Equal(6, await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Sweep));

        // When
        database.Clock.Advance(TimeSpan.FromHours(2));
        var deferral = await database.ReserveAsync(indexerId, SearchOrigin.Sweep);

        // Then: the first hour's 4 leave the window 25 hours after that hour starts, which
        // brings the count below the limit; the refusal itself charged nothing.
        Assert.Equal(
            new RequestDeferral(FirstHour.AddHours(25), RequestDeferralReason.AllowanceExhausted),
            deferral);
        Assert.Equal(10, await database.SpentAsync(indexerId));
    }

    [Fact]
    public async Task Refusal_LaterBucketNeeded_UntilIsThatBucketsExit()
    {
        // Given: limit 10 with the default reserve, so the sweep's ceiling is 7; 2 requests,
        // then 5 an hour later, and the interactive search takes the rest.
        await using var database = await CreateAsync();
        var indexerId = await database.AddIndexerAsync(10);
        Assert.Equal(2, await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Sweep, stopAfter: 2));
        database.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(5, await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Sweep));
        Assert.Equal(3, await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Interactive));

        // When
        var deferral = await database.ReserveAsync(indexerId, SearchOrigin.Sweep);

        // Then: dropping the first hour leaves 8, still not below 7, so the second hour decides.
        Assert.Equal(
            new RequestDeferral(FirstHour.AddHours(26), RequestDeferralReason.AllowanceExhausted),
            deferral);
    }

    [Fact]
    public async Task Refusal_CeilingZero_WaitsAFullWindowFromTheChargedHour()
    {
        // Given: a limit of 1 leaves the sweep a ceiling of 0. The clock moves an hour every
        // time it is read, so an until-time from a second reading would land an hour later.
        await using var database = await CreateAsync();
        var indexerId = await database.AddIndexerAsync(1);
        await using var provider = database.CreateProvider(new SteppingClock(Start, TimeSpan.FromHours(1)));
        var reservation = provider.GetRequiredService<IIndexerRequestReservation>();

        // When
        var deferral = await database.ReserveAsync(indexerId, SearchOrigin.Sweep, reservation: reservation);

        // Then
        Assert.Equal(
            new RequestDeferral(FirstHour.AddHours(25), RequestDeferralReason.AllowanceExhausted),
            deferral);
        Assert.Empty(await database.UsageAsync());
    }

    private sealed class SteppingClock(DateTimeOffset start, TimeSpan step) : TimeProvider
    {
        private readonly Lock _sync = new();
        private DateTimeOffset _next = start;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
            {
                var now = _next;
                _next += step;
                return now;
            }
        }
    }
}
