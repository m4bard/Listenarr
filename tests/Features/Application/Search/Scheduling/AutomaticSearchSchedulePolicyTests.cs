/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Application.Search.Scheduling;
using Listenarr.Domain.Search;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.Scheduling
{
    /// <summary>
    /// The three wait ladders: no-result (the cooldown doubling to a cap), pair failure (an hour
    /// doubling to a day) and book failure (a quarter hour doubling to the cooldown), with the
    /// doubling guarded against overflow for any count.
    /// </summary>
    [Trait("Name", "AutomaticSearchSchedulePolicyTests")]
    [Trait("Category", "AutomaticSearchSchedule")]
    public class AutomaticSearchSchedulePolicyTests : BaseTests
    {
        private static readonly DateTime Now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        private static readonly TimeSpan Cooldown = TimeSpan.FromHours(24);

        [Fact]
        public void BoundsConstants_HoldTheOperatorValues()
        {
            Assert.Equal(168, AutomaticSearchBounds.NoResultMaxIntervalHours);
            Assert.Equal(60, AutomaticSearchBounds.PairFailureBaseMinutes);
            Assert.Equal(24, AutomaticSearchBounds.PairFailureMaxIntervalHours);
            Assert.Equal(15, AutomaticSearchBounds.BookFailureBaseMinutes);
        }

        [Theory]
        [InlineData(1, 24)]
        [InlineData(2, 48)]
        [InlineData(3, 96)]
        [InlineData(4, 168)]
        [InlineData(5, 168)]
        [InlineData(40, 168)]
        public void NextNoResultAttempt_DoublesTheCooldownToTheWeeklyCap(int count, int expectedHours)
        {
            Assert.Equal(Now.AddHours(expectedHours), AutomaticSearchSchedulePolicy.NextNoResultAttempt(Now, count, Cooldown));
        }

        [Theory]
        [InlineData(1, 240)]
        [InlineData(2, 240)]
        [InlineData(int.MaxValue, 240)]
        public void NextNoResultAttempt_ACooldownAboveTheWeeklyCapIsItsOwnCap(int count, int expectedHours)
        {
            Assert.Equal(
                Now.AddHours(expectedHours),
                AutomaticSearchSchedulePolicy.NextNoResultAttempt(Now, count, TimeSpan.FromHours(240)));
        }

        [Theory]
        [InlineData(1, 60)]
        [InlineData(2, 120)]
        [InlineData(3, 240)]
        [InlineData(4, 480)]
        [InlineData(5, 960)]
        [InlineData(6, 1440)]
        [InlineData(7, 1440)]
        [InlineData(100, 1440)]
        public void NextPairFailureAttempt_DoublesAnHourToADay(int count, int expectedMinutes)
        {
            Assert.Equal(Now.AddMinutes(expectedMinutes), AutomaticSearchSchedulePolicy.NextPairFailureAttempt(Now, count));
        }

        [Theory]
        [InlineData(1, 15)]
        [InlineData(2, 30)]
        [InlineData(3, 60)]
        [InlineData(4, 120)]
        [InlineData(7, 960)]
        [InlineData(8, 1440)]
        [InlineData(9, 1440)]
        public void NextBookFailureAttempt_DoublesAQuarterHourToTheCooldown(int count, int expectedMinutes)
        {
            Assert.Equal(
                Now.AddMinutes(expectedMinutes),
                AutomaticSearchSchedulePolicy.NextBookFailureAttempt(Now, count, Cooldown));
        }

        [Fact]
        public void NextBookFailureAttempt_ACooldownBelowTheBaseIsTheCap()
        {
            Assert.Equal(
                Now.AddMinutes(10),
                AutomaticSearchSchedulePolicy.NextBookFailureAttempt(Now, 1, TimeSpan.FromMinutes(10)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        [InlineData(int.MinValue)]
        public void Ladders_TreatACountBelowOneAsTheFirstStep(int count)
        {
            Assert.Equal(Now.AddHours(24), AutomaticSearchSchedulePolicy.NextNoResultAttempt(Now, count, Cooldown));
            Assert.Equal(Now.AddHours(1), AutomaticSearchSchedulePolicy.NextPairFailureAttempt(Now, count));
            Assert.Equal(Now.AddMinutes(15), AutomaticSearchSchedulePolicy.NextBookFailureAttempt(Now, count, Cooldown));
        }

        [Theory]
        [InlineData(31)]
        [InlineData(63)]
        [InlineData(64)]
        [InlineData(1000)]
        [InlineData(int.MaxValue)]
        public void Ladders_DoNotOverflowOnHugeCounts(int count)
        {
            Assert.Equal(Now.AddHours(168), AutomaticSearchSchedulePolicy.NextNoResultAttempt(Now, count, Cooldown));
            Assert.Equal(Now.AddHours(24), AutomaticSearchSchedulePolicy.NextPairFailureAttempt(Now, count));
            Assert.Equal(Now.AddHours(24), AutomaticSearchSchedulePolicy.NextBookFailureAttempt(Now, count, Cooldown));
        }

        [Fact]
        public void Ladders_SaturateAtTheEndOfTimeInsteadOfThrowing()
        {
            var nearEnd = DateTime.SpecifyKind(DateTime.MaxValue.AddMinutes(-1), DateTimeKind.Utc);

            var next = AutomaticSearchSchedulePolicy.NextPairFailureAttempt(nearEnd, 3);

            Assert.Equal(DateTime.MaxValue.Ticks, next.Ticks);
            Assert.Equal(DateTimeKind.Utc, next.Kind);
        }
    }
}
