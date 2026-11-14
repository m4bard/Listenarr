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

namespace Listenarr.Tests.Features.Application.Search.Indexers.Common;

[Trait("Name", "IndexerRequestBudgetPolicyTests")]
[Trait("Category", "Application")]
public sealed class IndexerRequestBudgetPolicyTests : BaseTests
{
    private static readonly DateTime H0 = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(100, 25, 75)]
    [InlineData(2000, 25, 1500)]
    [InlineData(10, 25, 7)]
    [InlineData(100, 0, 100)]
    [InlineData(100, 95, 10)]
    [InlineData(100, -5, 100)]
    public void CeilingFor_Unattended_IsLimitLessReserveRoundedDown(int limit, int reserve, int expected)
    {
        Assert.Equal(expected, IndexerRequestBudgetPolicy.CeilingFor(limit, SearchOrigin.Sweep, reserve));
        Assert.Equal(limit, IndexerRequestBudgetPolicy.CeilingFor(limit, SearchOrigin.Interactive, reserve));
    }

    [Fact]
    public void CeilingFor_EveryOriginButInteractive_IsUnattended()
    {
        foreach (var origin in Enum.GetValues<SearchOrigin>())
        {
            var expected = origin == SearchOrigin.Interactive ? 100 : 75;
            Assert.Equal(expected, IndexerRequestBudgetPolicy.CeilingFor(100, origin, 25));
        }
    }

    [Fact]
    public void HourStartUtc_TruncatesToTheHourInUtc()
    {
        var now = new DateTimeOffset(2026, 3, 1, 12, 59, 30, TimeSpan.FromHours(2));

        var hour = IndexerRequestBudgetPolicy.HourStartUtc(now);

        Assert.Equal(H0, hour);
        Assert.Equal(DateTimeKind.Utc, hour.Kind);
    }

    [Fact]
    public void WindowAndPruneBounds_AreOneAndTwoDaysBeforeTheHour()
    {
        Assert.Equal(H0.AddHours(-24), IndexerRequestBudgetPolicy.WindowStartUtc(H0));
        Assert.Equal(H0.AddHours(-48), IndexerRequestBudgetPolicy.PruneBeforeUtc(H0));
    }

    [Fact]
    public void NextAdmissionUtc_FirstBucketToLeaveCuresTheOverage_IsThatBucketPlus25Hours()
    {
        var buckets = new[] { (H0, 4), (H0.AddHours(3), 6) };

        var until = IndexerRequestBudgetPolicy.NextAdmissionUtc(buckets, ceiling: 10, hour: H0.AddHours(5));

        Assert.Equal(H0.AddHours(25), until);
        Assert.Equal(DateTimeKind.Utc, until.Kind);
    }

    [Fact]
    public void NextAdmissionUtc_OverCount_WalksUntilTheRemainderIsBelowTheCeiling()
    {
        var buckets = new[] { (H0.AddHours(4), 5), (H0, 4), (H0.AddHours(3), 6) };

        var until = IndexerRequestBudgetPolicy.NextAdmissionUtc(buckets, ceiling: 10, hour: H0.AddHours(5));

        // 15 used: dropping the first bucket leaves 11, dropping the second leaves 5.
        Assert.Equal(H0.AddHours(28), until);
    }

    [Fact]
    public void NextAdmissionUtc_BucketsOutsideTheWindow_AreNotCounted()
    {
        var hour = H0.AddHours(30);
        var buckets = new[] { (H0, 9), (H0.AddHours(29), 2) };

        var until = IndexerRequestBudgetPolicy.NextAdmissionUtc(buckets, ceiling: 3, hour: hour);

        // The stale bucket is outside the window, so the one in it is under the ceiling already.
        Assert.Equal(H0.AddHours(31), until);
    }

    [Fact]
    public void NextAdmissionUtc_RemainderEqualToCeiling_KeepsWalking()
    {
        var buckets = new[] { (H0, 2), (H0.AddHours(1), 3), (H0.AddHours(3), 5) };

        var until = IndexerRequestBudgetPolicy.NextAdmissionUtc(buckets, ceiling: 8, hour: H0.AddHours(5));

        // 10 used: dropping the first bucket leaves 8, which is still at the ceiling.
        Assert.Equal(H0.AddHours(26), until);
    }

    [Fact]
    public void NextAdmissionUtc_UnspecifiedKindBuckets_StillReturnUtc()
    {
        var unspecified = DateTime.SpecifyKind(H0, DateTimeKind.Unspecified);
        var buckets = new[] { (unspecified, 4), (unspecified.AddHours(3), 6) };

        var until = IndexerRequestBudgetPolicy.NextAdmissionUtc(buckets, ceiling: 10, hour: H0.AddHours(5));

        Assert.Equal(H0.AddHours(25), until);
        Assert.Equal(DateTimeKind.Utc, until.Kind);
    }

    [Fact]
    public void NextAdmissionUtc_SumBelowCeiling_IsTheNextHour()
    {
        var buckets = new[] { (H0, 4), (H0.AddHours(3), 5) };

        var until = IndexerRequestBudgetPolicy.NextAdmissionUtc(buckets, ceiling: 10, hour: H0.AddHours(5));

        Assert.Equal(H0.AddHours(6), until);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NextAdmissionUtc_CeilingNeverMet_WaitsAFullWindow(int ceiling)
    {
        var buckets = new[] { (H0, 4) };

        var until = IndexerRequestBudgetPolicy.NextAdmissionUtc(buckets, ceiling, hour: H0.AddHours(5));

        Assert.Equal(H0.AddHours(30), until);
    }
}
