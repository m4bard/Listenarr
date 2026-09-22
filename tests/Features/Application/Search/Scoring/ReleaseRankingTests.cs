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
        public async Task AProfileThatPrefersTheLowerLadderRungStillDecides()
        {
            // CONTROL. It has to discriminate against BOTH wrong implementations, so the pair is
            // chosen so that neither can produce the asserted answer.
            //
            // MP3 64kbps scores 40 on the ladder and AAC 64kbps scores 78, so ordering by the
            // score alone returns the AAC. A rule that merely prefers AAC returns the AAC too.
            // Only reading this profile, which puts MP3 64kbps first, returns the MP3.
            //
            // An earlier version of this control used AAC 320kbps against MP3 320kbps under a
            // reversed profile, and it passed under score-only ordering as well, because the
            // ladder already scores MP3 320kbps above AAC 320kbps. It agreed with the code it was
            // supposed to rule out, so a broken apparatus looked exactly like a pass.
            var profile = Profile(new[] { "MP3 64kbps", "AAC 64kbps", "AAC 320kbps", "MP3 320kbps" });

            var ranked = await Rank(profile, Release("AAC 64kbps"), Release("MP3 64kbps"));

            Assert.Equal("MP3 64kbps", ranked[0].SearchResult.Quality);
            Assert.True(
                ranked[1].TotalScore > ranked[0].TotalScore,
                "the ladder no longer disagrees with this profile, so the control has stopped "
                + "ruling out score-only ordering");
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
        public void AContainerLabelRanksOnTheProfilesAacRungs()
        {
            // Parsers emit container labels that appear in no profile by that name.
            // SearchResultAttributeParser.DetectQualityFromFormat returns a bare "M4B", and an
            // exact name comparison would leave it unrankable, so the label goes through the same
            // codec-group mapping a file does. A bare container carries no bitrate, so it takes
            // the worst rung of its group rather than over-claiming.
            var profile = Profile(ShippedOrder);

            Assert.Equal("AAC 64kbps", QualityMatcher.RankingRung("M4B", profile)?.Quality);
            Assert.Equal("AAC 320kbps", QualityMatcher.RankingRung("M4B 320kbps", profile)?.Quality);
        }

        [Fact]
        public void ABitrateOfAThousandOrMoreRanksOnTheRungItNames()
        {
            // The label's bitrate is kbps and AudioQualityInput takes bits per second. Passing it
            // through unscaled survived by accident below 1000 and was divided by 1000 at or above
            // it, so "MP3 1411kbps" ranked on the worst rung in the profile.
            var profile = Profile(ShippedOrder);

            Assert.Equal("MP3 320kbps", QualityMatcher.RankingRung("MP3 1411kbps", profile)?.Quality);
            Assert.Equal("AAC 320kbps", QualityMatcher.RankingRung("AAC 1000kbps", profile)?.Quality);
            Assert.Equal("MP3 128kbps", QualityMatcher.RankingRung("MP3 128kbps", profile)?.Quality);
        }

        [Fact]
        public async Task TheBestCandidateThatIsAnUpgradeIsChosenRatherThanOnlyTheFirst()
        {
            // AutomaticSearchService ranks with InPreferenceOrder and then asks IsQualityBetter
            // whether the pick beats what is on disk. The two resolve a label differently: the
            // ranking maps by codec and bitrate, the gate matches an exact profile name. So a
            // bare "AAC" is rankable and not resolvable, and testing only the first candidate
            // discarded every release behind it.
            //
            // This models the picker's rule with the real collaborators rather than running the
            // service, which needs a full service scope. The service itself is not covered here.
            var profile = Profile(ShippedOrder);
            const string onDisk = "MP3 128kbps";

            var ranked = await Rank(profile, Release("MP3 320kbps"), Release("AAC"));

            // The premise: the bare label outranks the MP3 and the gate cannot resolve it.
            Assert.Equal("AAC", ranked[0].SearchResult.Quality);
            Assert.False(QualityMatcher.IsLabelBetter("AAC", onDisk, profile));
            Assert.True(QualityMatcher.IsLabelBetter("MP3 320kbps", onDisk, profile));

            var chosen = ranked.FirstOrDefault(candidate =>
                QualityMatcher.IsLabelBetter(candidate.SearchResult.Quality, onDisk, profile));

            Assert.NotNull(chosen);
            Assert.Equal("MP3 320kbps", chosen!.SearchResult.Quality);
        }

        [Fact]
        public async Task AReleaseTheProfileCannotRankSortsAfterOneItCan()
        {
            // The label has to be ACCEPTED but unrankable, or the ordering assertion is vacuous:
            // a rejected release already sorts last on the rejected key, which predates this
            // change. A bare "320" passes the allowed-quality veto, because that veto is a
            // substring match in both directions and "mp3 320kbps" contains "320", while the
            // shipped profile has no codec-less rung for it to rank on.
            var profile = Profile(ShippedOrder);

            var ranked = await Rank(profile, Release("320"), Release("MP3 64kbps"));

            Assert.False(ranked[0].IsRejected);
            Assert.False(ranked[1].IsRejected);
            Assert.Equal("MP3 64kbps", ranked[0].SearchResult.Quality);
            Assert.Equal(ReleaseRanking.Unrankable, ReleaseRanking.RungPriority("320", profile));
            Assert.True(
                ranked[1].TotalScore > ranked[0].TotalScore,
                "the unrankable release no longer scores above the ranked one, so this test no "
                + "longer shows the rung key rather than the score deciding");
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
