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
using Microsoft.EntityFrameworkCore;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestBudget.IndexerRequestBudgetTestDatabase;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestBudget;

// The daily request allowance against a migrated SQLite file: the usage rows are the only
// count, so every case reads them back from the database rather than from the reservation.
[Trait("Name", "EfIndexerRequestReservationTests")]
[Trait("Category", "Infrastructure")]
public sealed class EfIndexerRequestReservationTests : BaseTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NoLimit_AdmitsWithoutWriting(int? dailyLimit)
    {
        // Given
        await using var database = await CreateAsync();
        var indexerId = await database.AddIndexerAsync(dailyLimit);

        // When
        var results = new List<RequestDeferral?>();
        for (var i = 0; i < 100; i++)
        {
            results.Add(await database.ReserveAsync(indexerId, SearchOrigin.Sweep));
        }

        // Then
        Assert.All(results, Assert.Null);
        Assert.Empty(await database.UsageAsync());
    }

    [Fact]
    public async Task Reserve_SweepStopsAtItsShare_InteractiveSpendsTheReserve()
    {
        // Given: no settings row, so the reserve is the default 25 percent.
        await using var database = await CreateAsync();
        var indexerId = await database.AddIndexerAsync(100);

        // When
        var sweepAdmitted = await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Sweep);
        var interactiveAdmitted = await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Interactive);

        // Then
        Assert.Equal(75, sweepAdmitted);
        Assert.Equal(25, interactiveAdmitted);
        Assert.Equal(100, await database.SpentAsync(indexerId));
    }

    [Fact]
    public async Task Reserve_StoredReservePercent_SetsTheUnattendedShare()
    {
        // Given
        await using var database = await CreateAsync();
        await database.SetReservePercentAsync(40);
        var indexerId = await database.AddIndexerAsync(10);

        // When
        var bulkAdmitted = await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Bulk);

        // Then
        Assert.Equal(6, bulkAdmitted);
    }

    [Fact]
    public async Task Reserve_RetryStopsWhereTheSweepDoes()
    {
        // Given
        await using var database = await CreateAsync();
        var indexerId = await database.AddIndexerAsync(100);
        Assert.Equal(40, await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Sweep, stopAfter: 40));

        // When
        var retryAdmitted = await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Retry);

        // Then
        Assert.Equal(35, retryAdmitted);
        Assert.NotNull(await database.ReserveAsync(indexerId, SearchOrigin.Sweep));
        Assert.Null(await database.ReserveAsync(indexerId, SearchOrigin.Interactive));
    }

    [Fact]
    public async Task Reserve_IndexersHaveSeparateBudgets()
    {
        // Given
        await using var database = await CreateAsync();
        var first = await database.AddIndexerAsync(4);
        var second = await database.AddIndexerAsync(4);

        // When
        var firstAdmitted = await database.ReserveUntilRefusedAsync(first, SearchOrigin.Interactive);
        var secondAdmitted = await database.ReserveUntilRefusedAsync(second, SearchOrigin.Interactive);

        // Then
        Assert.Equal(4, firstAdmitted);
        Assert.Equal(4, secondAdmitted);
        Assert.Equal(4, await database.SpentAsync(first));
        Assert.Equal(4, await database.SpentAsync(second));
    }

    [Fact]
    public async Task DeletingAnIndexer_RemovesItsUsageRows()
    {
        // Given
        await using var database = await CreateAsync();
        var deleted = await database.AddIndexerAsync(4);
        var kept = await database.AddIndexerAsync(4);
        await database.ReserveUntilRefusedAsync(deleted, SearchOrigin.Interactive);
        await database.ReserveUntilRefusedAsync(kept, SearchOrigin.Interactive);

        // When
        await using (var context = await database.Factory.CreateDbContextAsync())
        {
            context.Indexers.Remove(await context.Indexers.SingleAsync(indexer => indexer.Id == deleted));
            await context.SaveChangesAsync();
        }

        // Then
        var usage = Assert.Single(await database.UsageAsync());
        Assert.Equal(kept, usage.IndexerId);
    }

    [Fact]
    public async Task Reserve_RequestStillCountsAt24Hours_AndDropsOutWithItsHour()
    {
        // Given: spent at 10:59; the 10:00 bucket counts until the current hour is 11:00 the
        // next day, so a full 24 hours after the spend the indexer is still refused.
        await using var database = await CreateAsync();
        var indexerId = await database.AddIndexerAsync(4);
        Assert.Equal(4, await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Interactive));

        // When
        database.Clock.Advance(TimeSpan.FromHours(24));
        var atTwentyFourHours = await database.ReserveAsync(indexerId, SearchOrigin.Interactive);
        database.Clock.Advance(TimeSpan.FromMinutes(1));
        var atTheNextHour = await database.ReserveAsync(indexerId, SearchOrigin.Interactive);

        // Then
        Assert.Equal(
            new RequestDeferral(new DateTime(2026, 3, 2, 11, 0, 0, DateTimeKind.Utc), RequestDeferralReason.AllowanceExhausted),
            atTwentyFourHours);
        Assert.Null(atTheNextHour);
    }

    [Fact]
    public async Task NeverUnderCountsAgainstATrue24HourWindow()
    {
        // Given: one request per simulated 7 minutes for two days against a generous limit. At
        // every step a limit equal to the requests sent in the preceding 24 hours must refuse.
        await using var database = await CreateAsync();
        var indexerId = await database.AddIndexerAsync(1_000_000);
        var sent = new List<DateTimeOffset>();

        for (var minute = 0; minute < 48 * 60; minute += 7)
        {
            // When
            var now = Start.AddMinutes(minute);
            database.Clock.Advance(now - database.Clock.GetUtcNow());
            Assert.Null(await database.ReserveAsync(indexerId, SearchOrigin.Interactive));
            sent.Add(now);

            var trueCount = sent.Count(at => at > now.AddHours(-24));
            await database.SetDailyLimitAsync(indexerId, trueCount);
            var probe = await database.ReserveAsync(indexerId, SearchOrigin.Interactive);
            await database.SetDailyLimitAsync(indexerId, 1_000_000);

            // Then
            Assert.True(
                probe is not null,
                $"under-counted at {now:u}: {trueCount} requests in the last 24 hours");
        }
    }

    [Fact]
    public async Task Restart_UsageSurvives_CeilingHolds()
    {
        // Given
        await using var database = await CreateAsync();
        var indexerId = await database.AddIndexerAsync(100);
        Assert.Equal(60, await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Sweep, stopAfter: 60));

        // When: a second service provider over the same file stands for the restarted process.
        database.Clock.Advance(TimeSpan.FromMinutes(30));
        await using var restarted = database.CreateProvider();
        var afterRestart = restarted.GetRequiredService<IIndexerRequestReservation>();
        var admittedAfterRestart = await database.ReserveUntilRefusedAsync(
            indexerId,
            SearchOrigin.Sweep,
            reservation: afterRestart);

        // Then
        Assert.Equal(15, admittedAfterRestart);
        Assert.Equal(75, await database.SpentAsync(indexerId));
    }

    [Fact]
    public async Task HourText_EfRowAndStatementCollide()
    {
        // Given: a row for the current hour written through EF, as any other writer would.
        await using var database = await CreateAsync();
        var indexerId = await database.AddIndexerAsync(10);
        var hour = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        await using (var context = await database.Factory.CreateDbContextAsync())
        {
            context.IndexerRequestUsages.Add(new IndexerRequestUsage { IndexerId = indexerId, HourStartUtc = hour, Count = 3 });
            await context.SaveChangesAsync();
        }

        // When
        var deferral = await database.ReserveAsync(indexerId, SearchOrigin.Interactive);

        // Then: the statement's hour is the same key, so it increments the EF row.
        Assert.Null(deferral);
        var usage = Assert.Single(await database.UsageAsync());
        Assert.Equal(hour, usage.HourStartUtc);
        Assert.Equal(4, usage.Count);
    }
}
