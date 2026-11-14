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

using System.Net.Http.Headers;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.Indexers.Common;

[Trait("Name", "IndexerRequestPolicyTests")]
[Trait("Category", "Application")]
public sealed class IndexerRequestPolicyTests : BaseTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 3, 4, 12, 0, 0, TimeSpan.Zero);

    private static TimeSpan? Parse(RetryConditionHeaderValue? header) =>
        RetryAfterParser.Parse(header, new FixedTimeProvider(Now));

    [Fact]
    public void Constants_MatchTheProposedPolicy()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), IndexerRequestPolicy.Pacing);
        Assert.Equal(2, IndexerRequestPolicy.MaxConcurrentPerIndexer);
        Assert.Equal(8, IndexerRequestPolicy.MaxConcurrentGlobal);
        Assert.Equal(3, IndexerRequestPolicy.FailureThreshold);
        Assert.Equal(TimeSpan.FromHours(24), IndexerRequestPolicy.RetryAfterMaximum);
        Assert.Equal(TimeSpan.FromSeconds(60), IndexerRequestPolicy.RetryAfterPastDate);
        Assert.Equal(TimeSpan.FromMinutes(1), IndexerRequestPolicy.UnavailableRetry);
    }

    [Fact]
    public void EscalationLadder_HasTheProposedSteps()
    {
        TimeSpan[] expected =
        [
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(3),
            TimeSpan.FromHours(6), TimeSpan.FromHours(12), TimeSpan.FromHours(24)
        ];

        Assert.Equal(expected, IndexerRequestPolicy.EscalationLadder);
        Assert.Equal(TimeSpan.FromHours(1), IndexerRequestPolicy.EscalationLadder[IndexerRequestPolicy.RateLimitStartStep]);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, null)]
    [InlineData(2, null)]
    [InlineData(3, 60)]
    [InlineData(4, 300)]
    [InlineData(5, 900)]
    [InlineData(6, 1800)]
    [InlineData(7, 3600)]
    [InlineData(8, 10800)]
    [InlineData(9, 21600)]
    [InlineData(10, 43200)]
    [InlineData(11, 86400)]
    [InlineData(12, 86400)]
    [InlineData(500, 86400)]
    public void FailureCooldown_StartsAtTheThresholdAndEscalates(int failures, int? expectedSeconds)
    {
        var cooldown = IndexerRequestPolicy.FailureCooldown(failures);

        Assert.Equal(expectedSeconds is { } s ? TimeSpan.FromSeconds(s) : null, cooldown);
    }

    [Theory]
    [InlineData(0, 3600)]
    [InlineData(1, 10800)]
    [InlineData(2, 21600)]
    [InlineData(3, 43200)]
    [InlineData(4, 86400)]
    [InlineData(5, 86400)]
    [InlineData(100, 86400)]
    public void RateLimitCooldown_StartsAtOneHourAndEscalates(int prior, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), IndexerRequestPolicy.RateLimitCooldown(prior));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(8, 8)]
    [InlineData(9, 8)]
    [InlineData(1_000_000, 8)]
    public void LadderStep_ClampsIntoTheLadder(int step, int expectedIndex)
    {
        Assert.Equal(IndexerRequestPolicy.EscalationLadder[expectedIndex], IndexerRequestPolicy.LadderStep(step));
    }

    [Fact]
    public void RetryAfter_Delta120_Gives120Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(120), Parse(new RetryConditionHeaderValue(TimeSpan.FromSeconds(120))));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3600, 3600)]
    [InlineData(86400, 86400)]
    [InlineData(86401, 86400)]
    [InlineData(10_000_000, 86400)]
    public void RetryAfter_Delta_IsClampedToTwentyFourHours(int seconds, int expectedSeconds)
    {
        var result = Parse(new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds)));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), result);
    }

    [Fact]
    public void RetryAfter_FutureDate_GivesTheDifferenceFromNow()
    {
        var result = Parse(new RetryConditionHeaderValue(Now.AddMinutes(10)));

        Assert.Equal(TimeSpan.FromMinutes(10), result);
    }

    [Fact]
    public void RetryAfter_FarFutureDate_IsClamped()
    {
        Assert.Equal(TimeSpan.FromHours(24), Parse(new RetryConditionHeaderValue(Now.AddDays(30))));
    }

    [Theory]
    [InlineData(-3600)]
    [InlineData(-1)]
    [InlineData(0)]
    public void RetryAfter_PastOrCurrentDate_GivesSixtySeconds(int offsetSeconds)
    {
        var result = Parse(new RetryConditionHeaderValue(Now.AddSeconds(offsetSeconds)));

        Assert.Equal(TimeSpan.FromSeconds(60), result);
    }

    [Fact]
    public void RetryAfter_Missing_GivesNull()
    {
        Assert.Null(Parse(null));
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("")]
    [InlineData("12abc")]
    public void RetryAfter_Unparseable_GivesNull(string raw)
    {
        var parsed = RetryConditionHeaderValue.TryParse(raw, out var header);

        Assert.False(parsed);
        Assert.Null(Parse(header));
    }

    [Fact]
    public void RetryAfter_DeltaZero_GivesSixtySeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), Parse(new RetryConditionHeaderValue(TimeSpan.Zero)));
    }
}
