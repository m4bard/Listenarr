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
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Search.Scoring
{
    /// <summary>
    /// The release score has no ceiling, so an operator preference still separates two releases
    /// whose accumulated score has passed 100.
    /// </summary>
    /// <remarks>
    /// Every profile here neutralises the settings that are not under test, the same way
    /// tools/profile_gate_probe.py does, so that two releases can only score differently because
    /// of the term being measured: no preferred formats and no preferred languages (both of which
    /// add their own bonus or penalty), no size bounds, no minimum seeders, no published date.
    ///
    /// The control is <see cref="PreferredWordsAreWorthFiveEachWhenTheScoreHasHeadroom"/>. It
    /// scores the identical preference on a rung 50 points below the ceiling, where a ceiling of
    /// 100 never bit, and it therefore comes out the same with the ceiling present or absent. If
    /// it ever fails, preferred words are not reaching the scorer at all and every other
    /// assertion in this file is void rather than informative.
    /// </remarks>
    [Trait("Area", "Scoring")]
    [Trait("Name", "SearchResultScorerCeilingTests")]
    [Trait("Category", "Application")]
    public sealed class SearchResultScorerCeilingTests : BaseTests
    {
        private const int BaseScore = 100;

        private static readonly string[] PreferredWords =
        {
            "retail", "unabridged", "narrator", "chaptered", "m4b"
        };

        private static SearchResultScorer CreateScorer()
        {
            return new SearchResultScorer(null, NullLogger.Instance);
        }

        private static QualityProfile CreateProfile(int minimumScore = 0)
        {
            return new QualityProfile
            {
                Name = "ceiling test",
                Qualities = new List<QualityDefinition>
                {
                    new() { Quality = "FLAC", Allowed = true, Priority = 0 },
                    new() { Quality = "MP3 320kbps", Allowed = true, Priority = 1 },
                    new() { Quality = "MP3 128kbps", Allowed = true, Priority = 2 },
                },
                CutoffQuality = "FLAC",
                MinimumSize = 0,
                MaximumSize = 0,
                PreferredFormats = new List<string>(),
                PreferredLanguages = new List<string>(),
                PreferredWords = new List<string>(PreferredWords),
                MustContain = new List<string>(),
                MustNotContain = new List<string>(),
                MinimumSeeders = 0,
                MinimumScore = minimumScore,
                MaximumAge = 0,
                IsDefault = false,
            };
        }

        private static SearchResult Release(string quality, bool matchesPreferredWords, int seeders)
        {
            var preferred = matchesPreferredWords ? string.Join(' ', PreferredWords) : string.Empty;
            return new SearchResult
            {
                Id = $"{quality}-{matchesPreferredWords}-{seeders}",
                Title = $"Some Author - Some Book {preferred} {quality}".Trim(),
                Quality = quality,
                DownloadType = "torrent",
                Size = 300L * 1024 * 1024,
                Seeders = seeders,
                PublishedDate = string.Empty,
                Format = string.Empty,
                Language = string.Empty,
            };
        }

        [Fact]
        public async Task PreferredWordsStillSeparateTwoReleasesAtTheTopOfTheLadder()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile();

            // FLAC is the top rung, so the quality deduction is zero and the release is already
            // at 100 before a single preference is added.
            var preferred = await scorer.Score(Release("FLAC", matchesPreferredWords: true, seeders: 10), profile);
            var plain = await scorer.Score(Release("FLAC", matchesPreferredWords: false, seeders: 0), profile);

            Assert.False(preferred.IsRejected);
            Assert.False(plain.IsRejected);
            Assert.Equal(BaseScore, plain.TotalScore);
            Assert.Equal(BaseScore + 25 + 10, preferred.TotalScore);
            Assert.True(
                preferred.TotalScore > plain.TotalScore,
                $"the preferred release scored {preferred.TotalScore} and the plain one {plain.TotalScore}");
        }

        [Fact]
        public async Task AnOperatorPreferenceCanOutrankAHigherLadderRung()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile();

            // MP3 320kbps sits 20 below FLAC on the hardcoded ladder, and five preferred words are
            // worth 25, so the operator's own terms are what decide this pair.
            //
            // This is a deliberate consequence of Listenarr folding quality and preference into one
            // number, and it is not new: below the ceiling a preference could already outrank a
            // higher rung, because MP3 320kbps at 80 plus two preferred words beats AAC 256kbps at
            // 74. What changes here is that it can now also beat the top rung instead of tying with
            // it. Readarr reaches the opposite answer by structure rather than by weight: its
            // comparer runs CompareQuality before CompareCustomFormatScore and returns the first
            // non-zero result (src/NzbDrone.Core/DecisionEngine/DownloadDecisionComparer.cs:26-41),
            // so a format score there can only break a tie within a rung, never invert one. Sonarr
            // is the same. Anyone revisiting this should treat the single summed score as the thing
            // to argue about, not this test.
            var preferredMp3 = await scorer.Score(Release("MP3 320kbps", matchesPreferredWords: true, seeders: 0), profile);
            var plainFlac = await scorer.Score(Release("FLAC", matchesPreferredWords: false, seeders: 0), profile);

            Assert.Equal(BaseScore - 20 + 25, preferredMp3.TotalScore);
            Assert.Equal(BaseScore, plainFlac.TotalScore);
            Assert.True(
                preferredMp3.TotalScore > plainFlac.TotalScore,
                $"the preferred MP3 scored {preferredMp3.TotalScore} and the plain FLAC {plainFlac.TotalScore}");
        }

        [Fact]
        public async Task TheBreakdownReconcilesWithTheReportedTotal()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile();

            var atTheCeiling = await scorer.Score(Release("FLAC", matchesPreferredWords: true, seeders: 10), profile);
            var belowIt = await scorer.Score(Release("MP3 128kbps", matchesPreferredWords: true, seeders: 10), profile);
            var penalised = await scorer.Score(Release("MP3 64kbps", matchesPreferredWords: false, seeders: 0), profile);

            AssertBreakdownReconciles(atTheCeiling);
            AssertBreakdownReconciles(belowIt);
            AssertBreakdownReconciles(penalised);

            // Every site that moves TotalScore also writes its delta into ScoreBreakdown, so the
            // total is reconstructible from the base and the breakdown. The ceiling broke that: the
            // breakdown kept describing a release that scored 135 while the total said 100.
        }

        private static void AssertBreakdownReconciles(QualityScore score)
        {
            // The quality entry is the ladder's raw value rather than a delta, so its contribution
            // is that value minus the 100 the release started with.
            var quality = score.ScoreBreakdown.TryGetValue("Quality", out var ladder) ? ladder - 100 : 0;
            var everythingElse = score.ScoreBreakdown
                .Where(entry => entry.Key != "Quality")
                .Sum(entry => entry.Value);

            Assert.Equal(BaseScore + quality + everythingElse, score.TotalScore);
        }

        [Fact]
        public async Task PreferredWordsAreWorthFiveEachWhenTheScoreHasHeadroom()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile();

            // CONTROL. MP3 128kbps scores 50 on the ladder, so the accumulated score never
            // reached 100 and a ceiling could not have bitten. This pair therefore has to come
            // out the same whether the ceiling is present or not, which is what makes the tests
            // above evidence about the ceiling rather than about preferred words being inert.
            var preferred = await scorer.Score(Release("MP3 128kbps", matchesPreferredWords: true, seeders: 10), profile);
            var plain = await scorer.Score(Release("MP3 128kbps", matchesPreferredWords: false, seeders: 0), profile);

            Assert.Equal(BaseScore - 50, plain.TotalScore);
            Assert.Equal(BaseScore - 50 + 25 + 10, preferred.TotalScore);
            Assert.Equal(25, preferred.ScoreBreakdown["PreferredWords"]);
            Assert.False(plain.ScoreBreakdown.ContainsKey("PreferredWords"));
        }

        [Fact]
        public async Task MinimumScoreStillReadsTheAccumulatedScore()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(minimumScore: 120);

            // MinimumScore was always compared before the ceiling was applied, so removing the
            // ceiling must not move the boundary: a release at 135 is above a minimum of 120 and
            // a release at 100 is below it, before and after. The rejection half of this test
            // holds either way; the accepted total is what the ceiling used to truncate.
            var above = await scorer.Score(Release("FLAC", matchesPreferredWords: true, seeders: 10), profile);
            var below = await scorer.Score(Release("FLAC", matchesPreferredWords: false, seeders: 0), profile);

            Assert.False(above.IsRejected);
            Assert.Equal(BaseScore + 25 + 10, above.TotalScore);

            Assert.True(below.IsRejected);
            Assert.Equal(-1, below.TotalScore);
            Assert.Contains(below.RejectionReasons, reason => reason.Contains("below profile minimum"));
        }
    }
}
