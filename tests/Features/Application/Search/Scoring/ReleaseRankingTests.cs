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
    /// Release selection reads the operator's quality profile, and falls back to the accumulated
    /// score only between releases the profile ranks equally.
    /// </summary>
    /// <remarks>
    /// The control is <see cref="AProfileThatPrefersMp3FlipsThePick"/>. It is the same two
    /// releases under a profile whose ordering is reversed, and the pick has to come out the other
    /// way round. If it did not, these tests would be satisfied by anything that happened to
    /// prefer AAC, including a rewritten hardcoded ladder, and they would say nothing about the
    /// profile being read.
    ///
    /// <see cref="TheScoreStillSeparatesTwoReleasesOnTheSameRung"/> is the second control: the
    /// score has to keep deciding where the profile does not, which is what shows the ordering was
    /// layered above the score rather than put in place of it.
    /// </remarks>
    [Trait("Area", "Scoring")]
    [Trait("Name", "ReleaseRankingTests")]
    [Trait("Category", "Application")]
    public sealed class ReleaseRankingTests : BaseTests
    {
        /// <summary>
        /// The ordering Listenarr's own default profile insists on, from
        /// QualityProfileService.EnsureProfileHasRequiredQualitiesAsync. The hardcoded ladder
        /// scores every AAC rung 78 and MP3 320kbps 80, so it inverts the first six of these.
        /// </summary>
        private static readonly string[] ShippedOrder =
        {
            "AAC 320kbps", "AAC 256kbps", "AAC 192kbps", "AAC 128kbps", "AAC 64kbps",
            "MP3 320kbps", "MP3 256kbps", "MP3 VBR", "MP3 192kbps", "MP3 128kbps", "MP3 64kbps",
        };

        private static QualityProfile Profile(IEnumerable<string> order, params string[] preferredWords)
        {
            var rungs = order.Select((quality, index) => new QualityDefinition
            {
                Quality = quality,
                Allowed = true,
                Priority = index,
            }).ToList();

            return new QualityProfile
            {
                Name = "ranking test",
                Qualities = rungs,
                CutoffQuality = rungs[0].Quality,
                MinimumSize = 0,
                MaximumSize = 0,
                PreferredFormats = new List<string>(),
                PreferredLanguages = new List<string>(),
                PreferredWords = new List<string>(preferredWords),
                MustContain = new List<string>(),
                MustNotContain = new List<string>(),
                MinimumSeeders = 0,
                MinimumScore = 0,
                MaximumAge = 0,
                IsDefault = false,
            };
        }

        private static SearchResult Release(string quality, string? title = null)
            => new()
            {
                Id = quality,
                Title = title ?? $"Some Author - Some Book {quality}",
                Quality = quality,
                DownloadType = "torrent",
                Size = 300L * 1024 * 1024,
                Seeders = 0,
                PublishedDate = string.Empty,
                Format = string.Empty,
                Language = string.Empty,
            };

        private static async Task<List<QualityScore>> Rank(QualityProfile profile, params SearchResult[] releases)
        {
            var scorer = new SearchResultScorer(null, NullLogger.Instance);
            var scored = new List<QualityScore>();
            foreach (var release in releases)
            {
                scored.Add(await scorer.Score(release, profile));
            }

            return scored.InPreferenceOrder(profile).ToList();
        }

        [Fact]
        public async Task TheShippedDefaultOrderPicksAacOverMp3()
        {
            var profile = Profile(ShippedOrder);

            var ranked = await Rank(profile, Release("MP3 320kbps"), Release("AAC 320kbps"));

            // The ladder scores MP3 320kbps 80 and AAC 320kbps 78, so ordering by the score alone
            // picked the MP3 although the profile puts AAC 320kbps five rungs above it.
            Assert.Equal("AAC 320kbps", ranked[0].SearchResult.Quality);
            Assert.True(
                ranked[1].TotalScore > ranked[0].TotalScore,
                "the ladder no longer disagrees with the profile, so this test has stopped "
                + "measuring whether the profile is read");
        }

        [Fact]
        public async Task AProfileThatPrefersMp3FlipsThePick()
        {
            // CONTROL. The same two releases, the same scores, one reordered profile. The pick has
            // to come out the other way round, which is what shows the profile is being read
            // rather than AAC being preferred by some other means.
            var profile = Profile(ShippedOrder.OrderBy(quality => quality.StartsWith("AAC", StringComparison.Ordinal)));

            var ranked = await Rank(profile, Release("MP3 320kbps"), Release("AAC 320kbps"));

            Assert.Equal("MP3 320kbps", ranked[0].SearchResult.Quality);
        }

        [Fact]
        public async Task TheProfileSeparatesTwoRungsTheLadderTies()
        {
            // Every AAC rung scores 78, so the ladder could not tell these apart at all and the
            // grab fell to whatever order the indexer answered in.
            var profile = Profile(ShippedOrder);

            var ranked = await Rank(profile, Release("AAC 64kbps"), Release("AAC 320kbps"));

            Assert.Equal("AAC 320kbps", ranked[0].SearchResult.Quality);
            Assert.Equal(ranked[0].TotalScore, ranked[1].TotalScore);
        }

        [Fact]
        public async Task TheScoreStillSeparatesTwoReleasesOnTheSameRung()
        {
            // CONTROL. The profile ranks these equally, so the accumulated score has to decide.
            // If it did not, the ordering would have replaced the score rather than sitting above
            // it, and every preference an operator sets would have stopped counting.
            var profile = Profile(ShippedOrder, "retail", "unabridged");

            var ranked = await Rank(
                profile,
                Release("AAC 320kbps", "Some Author - Some Book AAC 320kbps"),
                Release("AAC 320kbps", "Some Author - Some Book retail unabridged AAC 320kbps"));

            Assert.Contains("retail", ranked[0].SearchResult.Title);
            Assert.True(ranked[0].TotalScore > ranked[1].TotalScore);
        }

        [Fact]
        public async Task AnM4bLabelRanksOnTheProfilesAacRungs()
        {
            // The torznab parser emits container labels such as "M4B" that appear in no profile by
            // that name. Ranking by an exact name comparison would leave every one of them
            // unrankable, so the label goes through the same codec-group mapping a file does.
            var profile = Profile(ShippedOrder);

            Assert.Equal("AAC 320kbps", QualityMatcher.RankingRung("M4B 320kbps", profile)?.Quality);
            Assert.Equal("AAC 64kbps", QualityMatcher.RankingRung("M4B 64kbps", profile)?.Quality);
        }

        [Fact]
        public async Task AReleaseTheProfileCannotRankSortsAfterOneItCan()
        {
            var profile = Profile(ShippedOrder);

            var ranked = await Rank(profile, Release("Vorbis 500kbps"), Release("MP3 64kbps"));

            Assert.Equal("MP3 64kbps", ranked[0].SearchResult.Quality);
            Assert.Equal(ReleaseRanking.Unrankable, ReleaseRanking.RungPriority("Vorbis 500kbps", profile));
            await Task.CompletedTask;
        }

        [Fact]
        public async Task TheSharedScoringEndpointReturnsTheSameOrder()
        {
            // The ordering has to be in ScoreSearchResults itself, not only in the helper: that is
            // the one sort POST /api/v1/qualityprofile/{id}/score returns and the one both
            // download pickers start from. A helper nobody called would pass every test above.
            var service = new QualityProfileService(
                Mock.Of<IQualityProfileRepository>(), NullLogger<QualityProfileService>.Instance);
            var profile = Profile(ShippedOrder);

            var ranked = await service.ScoreSearchResults(
                new List<SearchResult> { Release("MP3 320kbps"), Release("AAC 320kbps") },
                profile);

            Assert.Equal("AAC 320kbps", ranked[0].SearchResult.Quality);
            Assert.Equal("MP3 320kbps", ranked[1].SearchResult.Quality);
        }

        [Fact]
        public async Task RejectedReleasesStillSortLastWhateverTheProfileSays()
        {
            var profile = Profile(ShippedOrder);
            profile.MustNotContain = new List<string> { "listenarrprobeforbidden" };

            var ranked = await Rank(
                profile,
                Release("AAC 320kbps", "Some Book listenarrprobeforbidden AAC 320kbps"),
                Release("MP3 64kbps"));

            Assert.Equal("MP3 64kbps", ranked[0].SearchResult.Quality);
            Assert.True(ranked[1].IsRejected);
        }
    }
}
