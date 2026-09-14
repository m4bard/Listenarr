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

namespace Listenarr.Tests.Features.Application.Audiobooks.Quality
{
    /// <summary>
    /// Scoring differently is not the claim worth making; grabbing differently is. This runs
    /// candidates through the same ScoreSearchResults the automatic search calls and then
    /// applies the winner-selection predicate from AutomaticSearchService, so the assertion is
    /// about which release would actually be sent to the download client.
    /// </summary>
    [Trait("Name", "ReleaseShapeGrabOutcomeTests")]
    [Trait("Category", "Search")]
    public class ReleaseShapeGrabOutcomeTests : BaseTests
    {
        private const string BundleTitle = "Edgar Rice Burroughs - The Barsoom Collection: Books 1-6 [M4B]";
        private const string IndividualTitle = "Edgar Rice Burroughs - A Princess of Mars [M4B]";

        private static QualityProfileService CreateService() =>
            new QualityProfileService(Mock.Of<IQualityProfileRepository>(), NullLogger<QualityProfileService>.Instance);

        private static QualityProfile CreateProfile(ReleaseShapePreference preference) =>
            new QualityProfile
            {
                PreferredFormats = new List<string> { "m4b" },
                PreferredLanguages = new List<string>(),
                PreferredWords = new List<string>(),
                MustContain = new List<string>(),
                MustNotContain = new List<string>(),
                MinimumSeeders = 0,
                MaximumAge = 0,
                MinimumScore = 0,
                PreferredReleaseShape = preference
            };

        private static SearchResult CreateResult(string title) =>
            new SearchResult
            {
                Title = title,
                Size = 400L * 1024 * 1024,
                Format = "m4b",
                Quality = "m4b",
                Language = "English",
                DownloadType = "torrent",
                Seeders = 5,
                PublishedDate = DateTime.UtcNow.ToString("o")
            };

        /// <summary>The predicate at AutomaticSearchService.ProcessAudiobookAsync.</summary>
        private static QualityScore? PickWinner(List<QualityScore> scored) =>
            scored.Where(s => !s.IsRejected).OrderByDescending(s => s.TotalScore).FirstOrDefault();

        [Theory]
        [InlineData(ReleaseShapePreference.PreferIndividual, IndividualTitle)]
        [InlineData(ReleaseShapePreference.PreferBundle, BundleTitle)]
        public async Task ThePreferenceDecidesWhichOfTwoCandidatesIsGrabbed(
            ReleaseShapePreference preference,
            string expectedWinner)
        {
            var candidates = new List<SearchResult>
            {
                CreateResult(BundleTitle),
                CreateResult(IndividualTitle)
            };

            var scored = await CreateService().ScoreSearchResults(candidates, CreateProfile(preference));
            var winner = PickWinner(scored);

            Assert.NotNull(winner);
            Assert.Equal(expectedWinner, winner!.SearchResult.Title);
        }

        [Fact]
        public async Task WithNoPreference_NeitherShapeIsFavoured()
        {
            // The control. If the preference block were never reached, or the detector answered
            // false to everything, the two tests above would still pass on whichever candidate
            // happened to be ordered first. This one fails if the shape is moving scores here.
            var candidates = new List<SearchResult>
            {
                CreateResult(BundleTitle),
                CreateResult(IndividualTitle)
            };

            var scored = await CreateService().ScoreSearchResults(candidates, CreateProfile(ReleaseShapePreference.NoPreference));

            Assert.Equal(2, scored.Count);
            Assert.Equal(scored[0].TotalScore, scored[1].TotalScore);
            Assert.All(scored, s => Assert.DoesNotContain("ReleaseShapeMismatch", s.ScoreBreakdown.Keys));
        }

        [Fact]
        public async Task PreferringIndividualBooks_StillGrabsTheBundleWhenItIsAllThereIs()
        {
            var candidates = new List<SearchResult> { CreateResult(BundleTitle) };

            var scored = await CreateService().ScoreSearchResults(candidates, CreateProfile(ReleaseShapePreference.PreferIndividual));
            var winner = PickWinner(scored);

            Assert.NotNull(winner);
            Assert.Equal(BundleTitle, winner!.SearchResult.Title);
            Assert.Equal(-20, winner.ScoreBreakdown["ReleaseShapeMismatch"]);
        }

        [Fact]
        public async Task AnOmnibusRecordGrabsTheBundleEvenUnderPreferIndividual()
        {
            var omnibusRecord = new Audiobook { Title = "The Barsoom Collection", SeriesNumber = "1-6" };
            var candidates = new List<SearchResult>
            {
                CreateResult(BundleTitle),
                CreateResult(IndividualTitle)
            };

            var scored = await CreateService().ScoreSearchResults(
                candidates,
                CreateProfile(ReleaseShapePreference.PreferIndividual),
                ReleaseShapeDetector.IsBundleSeriesNumber(omnibusRecord.SeriesNumber));
            var winner = PickWinner(scored);

            Assert.NotNull(winner);
            Assert.Equal(BundleTitle, winner!.SearchResult.Title);
        }
    }
}
