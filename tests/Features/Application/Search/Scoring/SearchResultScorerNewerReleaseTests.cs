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
using System.Globalization;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Search.Scoring
{
    /// <summary>
    /// The profile's "Prefer newer releases" setting adds a bonus that fades from 10 points for a
    /// release published today to nothing at a year old, applied only to a release that has
    /// already been accepted.
    /// </summary>
    /// <remarks>
    /// Every release here carries no quality label, no format and no language, and the profile
    /// has no preferred formats, languages or words, so each one scores 100 less the fixed
    /// 10-point missing-quality penalty. That keeps these numbers independent of the quality
    /// ladder: the only terms that can differ between two releases are the existing age penalty
    /// (6 points a year, floored) and the bonus under test.
    /// </remarks>
    [Trait("Area", "Scoring")]
    [Trait("Name", "SearchResultScorerNewerReleaseTests")]
    [Trait("Category", "Application")]
    public sealed class SearchResultScorerNewerReleaseTests : BaseTests
    {
        private const int UnlabelledScore = 90;
        private const string BonusKey = "NewerRelease";

        private static SearchResultScorer CreateScorer() =>
            new SearchResultScorer(null, NullLogger.Instance);

        private static QualityProfile CreateProfile(
            bool preferNewerReleases,
            int minimumScore = 0,
            int maximumAge = 0,
            int maximumSize = 0,
            List<string>? mustNotContain = null) =>
            new QualityProfile
            {
                Name = "newer release test",
                Qualities = new List<QualityDefinition>
                {
                    new() { Quality = "MP3 128kbps", Allowed = true, Priority = 0 },
                },
                MinimumSize = 0,
                MaximumSize = maximumSize,
                PreferredFormats = new List<string>(),
                PreferredLanguages = new List<string>(),
                PreferredWords = new List<string>(),
                MustContain = new List<string>(),
                MustNotContain = mustNotContain ?? new List<string>(),
                MinimumSeeders = 0,
                MinimumScore = minimumScore,
                MaximumAge = maximumAge,
                PreferNewerReleases = preferNewerReleases,
            };

        private static SearchResult Release(string? publishedDate, string title = "Some Author - Some Book") =>
            new SearchResult
            {
                Id = $"{title}-{publishedDate}",
                Title = title,
                Quality = string.Empty,
                Format = string.Empty,
                Language = string.Empty,
                DownloadType = "torrent",
                Size = 300L * 1024 * 1024,
                Seeders = 0,
                PublishedDate = publishedDate,
            };

        // An hour old rather than this instant, so the release is unambiguously in the past and
        // still counts as published today.
        private static string PublishedToday() => DateTime.UtcNow.AddHours(-1).ToString("o", CultureInfo.InvariantCulture);

        private static string PublishedDaysAgo(int days) => DateTime.UtcNow.AddDays(-days).ToString("o", CultureInfo.InvariantCulture);

        [Theory]
        [InlineData(0, 10)]
        [InlineData(1, 9)]
        [InlineData(182, 5)]
        [InlineData(300, 1)]
        [InlineData(328, 1)]
        [InlineData(329, 0)]
        [InlineData(365, 0)]
        [InlineData(3650, 0)]
        public void TheBonusFadesLinearlyOverAYearAndIsNeverNegative(int daysOld, int expectedBonus)
        {
            var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            var published = now.AddDays(-daysOld).ToString("o", CultureInfo.InvariantCulture);

            Assert.Equal(expectedBonus, CreateScorer().NewerReleaseBonus(published, now));
        }

        [Fact]
        public void AReleaseFromEarlierTodayEarnsTheFullBonus()
        {
            // The age is counted in whole days. A fractional age would floor a release published
            // an hour ago to 9, and only a release dated in the future could ever reach 10.
            var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

            Assert.Equal(10, CreateScorer().NewerReleaseBonus(now.AddHours(-11).ToString("o", CultureInfo.InvariantCulture), now));
        }

        [Fact]
        public void AFutureDatedReleaseEarnsTheFullBonusAndNoMore()
        {
            // A year and more ahead, far enough that an unclamped negative age would push the
            // linear formula well past 10 rather than being lost in the floor.
            var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

            Assert.Equal(10, CreateScorer().NewerReleaseBonus(now.AddDays(400).ToString("o", CultureInfo.InvariantCulture), now));
        }

        [Fact]
        public async Task WithThePreferenceOnTheNewerOfTwoOtherwiseIdenticalReleasesScoresHigher()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(preferNewerReleases: true);

            var today = await scorer.Score(Release(PublishedToday()), profile);
            var halfAYear = await scorer.Score(Release(PublishedDaysAgo(182)), profile);
            var older = await scorer.Score(Release(PublishedDaysAgo(300)), profile);

            Assert.False(today.IsRejected);
            Assert.False(halfAYear.IsRejected);
            Assert.False(older.IsRejected);

            Assert.Equal(10, today.ScoreBreakdown[BonusKey]);
            Assert.Equal(UnlabelledScore + 10, today.TotalScore);

            // 182 days: the age penalty is floor(182 / 3650 * 60) = 2 and the bonus is 5.
            Assert.Equal(5, halfAYear.ScoreBreakdown[BonusKey]);
            Assert.Equal(UnlabelledScore - 2 + 5, halfAYear.TotalScore);

            // 300 days: the age penalty is floor(300 / 3650 * 60) = 4 and the bonus is 1.
            Assert.Equal(1, older.ScoreBreakdown[BonusKey]);
            Assert.Equal(UnlabelledScore - 4 + 1, older.TotalScore);

            Assert.True(today.TotalScore > older.TotalScore, $"today scored {today.TotalScore} and 300 days {older.TotalScore}");
        }

        [Fact]
        public async Task WithThePreferenceOffOnlyTheExistingAgePenaltySeparatesThem()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(preferNewerReleases: false);

            var today = await scorer.Score(Release(PublishedToday()), profile);
            var older = await scorer.Score(Release(PublishedDaysAgo(300)), profile);

            Assert.False(today.ScoreBreakdown.ContainsKey(BonusKey));
            Assert.False(older.ScoreBreakdown.ContainsKey(BonusKey));
            Assert.Equal(UnlabelledScore, today.TotalScore);
            Assert.Equal(UnlabelledScore - 4, older.TotalScore);
            Assert.Equal(-4, older.ScoreBreakdown["Age"]);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not a date")]
        public async Task AReleaseWithNoUsableDateEarnsNothing(string? publishedDate)
        {
            // The gates report an age of zero for these, the same as for a release published
            // this instant. Reading that zero as "brand new" would hand the full bonus to every
            // release whose indexer does not report a date.
            var scorer = CreateScorer();
            var profile = CreateProfile(preferNewerReleases: true);

            var undated = await scorer.Score(Release(publishedDate), profile);

            Assert.False(undated.IsRejected);
            Assert.False(undated.ScoreBreakdown.ContainsKey(BonusKey));
            Assert.Equal(UnlabelledScore, undated.TotalScore);
        }

        [Fact]
        public async Task TheBonusDoesNotRescueAReleaseOlderThanMaximumAge()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(preferNewerReleases: true, maximumAge: 30);

            var tooOld = await scorer.Score(Release(PublishedDaysAgo(31)), profile);
            var withinLimit = await scorer.Score(Release(PublishedDaysAgo(29)), profile);

            Assert.True(tooOld.IsRejected);
            Assert.Contains(tooOld.RejectionReasons, reason => reason.Contains("profile maximum age", StringComparison.Ordinal));
            Assert.False(tooOld.ScoreBreakdown.ContainsKey(BonusKey));

            // The control: one just inside the limit is accepted and does carry the bonus, so the
            // rejection above is the gate and not the bonus failing to apply at that age.
            Assert.False(withinLimit.IsRejected);
            Assert.Equal(9, withinLimit.ScoreBreakdown[BonusKey]);
        }

        [Fact]
        public async Task TheBonusDoesNotLiftAReleaseOverMinimumScore()
        {
            var scorer = CreateScorer();

            // Without the bonus this release scores 90; with it, 100. A minimum of 95 sits
            // between the two, so it is rejected only if the bonus is applied after the check.
            var belowMinimum = await scorer.Score(Release(PublishedToday()), CreateProfile(preferNewerReleases: true, minimumScore: 95));

            Assert.True(belowMinimum.IsRejected);
            Assert.Contains(belowMinimum.RejectionReasons, reason => reason.Contains($"Score {UnlabelledScore} below profile minimum 95", StringComparison.Ordinal));
            Assert.False(belowMinimum.ScoreBreakdown.ContainsKey(BonusKey));

            // The control: at a minimum it does meet, the same release is accepted with the bonus.
            var atMinimum = await scorer.Score(Release(PublishedToday()), CreateProfile(preferNewerReleases: true, minimumScore: UnlabelledScore));

            Assert.False(atMinimum.IsRejected);
            Assert.Equal(UnlabelledScore + 10, atMinimum.TotalScore);
        }

        [Fact]
        public async Task TheBonusDoesNotRescueAForbiddenWordOrAnOversizedRelease()
        {
            var scorer = CreateScorer();

            var forbidden = await scorer.Score(
                Release(PublishedToday(), title: "Some Author - Some Book abridged"),
                CreateProfile(preferNewerReleases: true, mustNotContain: new List<string> { "abridged" }));
            var oversized = await scorer.Score(
                Release(PublishedToday()),
                CreateProfile(preferNewerReleases: true, maximumSize: 100));

            Assert.True(forbidden.IsRejected);
            Assert.Equal(-1, forbidden.TotalScore);
            Assert.False(forbidden.ScoreBreakdown.ContainsKey(BonusKey));

            Assert.True(oversized.IsRejected);
            Assert.Equal(-1, oversized.TotalScore);
            Assert.False(oversized.ScoreBreakdown.ContainsKey(BonusKey));
        }
    }
}
