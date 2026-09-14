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
    /// The release-shape preference, asserted as a difference between two candidates that are
    /// identical in every other respect.
    ///
    /// <para>Note what the NoPreference case is doing here. A detector that answered false to
    /// everything, or a scoring block that was never entered, would still pass a test that only
    /// checks "the individual release wins": it wins either way. The equality assertion under
    /// NoPreference and the inversion under PreferBundle are what make this file able to fail.</para>
    /// </summary>
    [Trait("Name", "SearchResultScorerShapeTests")]
    [Trait("Category", "Search")]
    public class SearchResultScorerShapeTests : BaseTests
    {
        // Two releases of the same series, differing only in shape. The bundle title is the
        // real one from the listenarr-testdata corpus (ASIN B0DHSHFQQH).
        private const string BundleTitle = "Edgar Rice Burroughs - The Barsoom Collection: Books 1-6 [M4B]";
        private const string IndividualTitle = "Edgar Rice Burroughs - A Princess of Mars [M4B]";

        private static SearchResultScorer CreateScorer() =>
            new SearchResultScorer(null, NullLogger<SearchResultScorerShapeTests>.Instance);

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

        [Fact]
        public async Task NoPreference_ScoresBothShapesIdentically()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(ReleaseShapePreference.NoPreference);

            var bundle = await scorer.Score(CreateResult(BundleTitle), profile);
            var individual = await scorer.Score(CreateResult(IndividualTitle), profile);

            Assert.Equal(individual.TotalScore, bundle.TotalScore);
            Assert.DoesNotContain("ReleaseShapeMatch", bundle.ScoreBreakdown.Keys);
            Assert.DoesNotContain("ReleaseShapeMismatch", bundle.ScoreBreakdown.Keys);
        }

        [Fact]
        public async Task PreferIndividual_PutsTheSingleBookAhead()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(ReleaseShapePreference.PreferIndividual);

            var bundle = await scorer.Score(CreateResult(BundleTitle), profile);
            var individual = await scorer.Score(CreateResult(IndividualTitle), profile);

            Assert.True(individual.TotalScore > bundle.TotalScore,
                $"individual {individual.TotalScore} should beat bundle {bundle.TotalScore}");
            Assert.Equal(-20, bundle.ScoreBreakdown["ReleaseShapeMismatch"]);
            Assert.Equal(5, individual.ScoreBreakdown["ReleaseShapeMatch"]);
        }

        [Fact]
        public async Task PreferBundle_InvertsTheOrdering()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(ReleaseShapePreference.PreferBundle);

            var bundle = await scorer.Score(CreateResult(BundleTitle), profile);
            var individual = await scorer.Score(CreateResult(IndividualTitle), profile);

            Assert.True(bundle.TotalScore > individual.TotalScore,
                $"bundle {bundle.TotalScore} should beat individual {individual.TotalScore}");
            Assert.Equal(5, bundle.ScoreBreakdown["ReleaseShapeMatch"]);
            Assert.Equal(-20, individual.ScoreBreakdown["ReleaseShapeMismatch"]);
        }

        /// <summary>
        /// The decision the whole feature turns on: preferring individual books must not leave a
        /// monitored book unfilled when a bundle is the only thing anyone is seeding.
        /// </summary>
        [Fact]
        public async Task PreferIndividual_StillAcceptsABundleWhenItIsTheOnlyCandidate()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(ReleaseShapePreference.PreferIndividual);

            var bundle = await scorer.Score(CreateResult(BundleTitle), profile);

            Assert.False(bundle.IsRejected);
            Assert.True(bundle.TotalScore > 0);
            Assert.Empty(bundle.RejectionReasons);
        }

        /// <summary>
        /// The penalty has to stay survivable when everything else has gone wrong too, or it is
        /// a filter wearing a preference's clothes: the score falls through the "computed score
        /// &lt;= 0" path and the operator is told the score was zero, not that it was a bundle.
        /// </summary>
        [Fact]
        public async Task TheShapePenaltyAloneCannotTipACandidateIntoRejection()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(ReleaseShapePreference.PreferIndividual);
            profile.PreferredLanguages = new List<string> { "German" };
            profile.PreferredFormats = new List<string> { "flac" };

            // Missing format, missing quality, a language mismatch and a decade of age, plus the
            // bundle penalty on top.
            var battered = CreateResult(BundleTitle);
            battered.Format = null;
            battered.Quality = null;
            battered.Language = "English";
            battered.PublishedDate = DateTime.UtcNow.AddYears(-10).ToString("o");
            battered.Seeders = 0;
            battered.DownloadType = "nzb";

            var score = await scorer.Score(battered, profile);

            Assert.True(score.TotalScore > 0,
                $"a bundle penalty on top of every other penalty still rejected: {score.TotalScore}, "
                + $"breakdown {string.Join(", ", score.ScoreBreakdown.Select(kv => $"{kv.Key}={kv.Value}"))}");
            Assert.False(score.IsRejected);
        }

        /// <summary>
        /// When the library record is itself an omnibus, a bundle release is the correct release
        /// for it whatever the profile says, or an operator who prefers individual books can
        /// never fill a legitimately monitored omnibus.
        /// </summary>
        [Fact]
        public async Task ABundleTargetOverridesAPreferenceForIndividualBooks()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(ReleaseShapePreference.PreferIndividual);

            var bundle = await scorer.Score(CreateResult(BundleTitle), profile, targetIsBundle: true);
            var individual = await scorer.Score(CreateResult(IndividualTitle), profile, targetIsBundle: true);

            Assert.True(bundle.TotalScore >= individual.TotalScore,
                $"bundle {bundle.TotalScore} should not lose to individual {individual.TotalScore} "
                + "when the monitored record is itself a bundle");
            Assert.Equal(5, bundle.ScoreBreakdown["ReleaseShapeMatch"]);
        }

        [Fact]
        public void ABundleTargetIsReadOffTheStoredSeriesPosition()
        {
            // The two halves of the refinement have to agree: what AutomaticSearchService passes
            // as targetIsBundle is exactly this call on the library record.
            Assert.True(ReleaseShapeDetector.IsBundleSeriesNumber(
                new Audiobook { Title = "The Barsoom Collection", SeriesNumber = "1-6" }.SeriesNumber));
            Assert.False(ReleaseShapeDetector.IsBundleSeriesNumber(
                new Audiobook { Title = "A Princess of Mars", SeriesNumber = "1" }.SeriesNumber));
        }
    }
}
