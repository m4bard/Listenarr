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
    /// Items 172 (word-boundary filter terms), 178 (indexer priority as a tie-break) and 122
    /// (the bundle/individual preference) each add a distinct concern to SearchResultScorer, and
    /// each was built without seeing the other two. These tests assert the order of precedence
    /// between them, which is a property of the combination and so cannot be covered by any of
    /// the three branches' own test files.
    ///
    /// <para>The precedence is: a filter term rejects outright, before anything is scored; the
    /// shape preference is an ordinary scoring term inside TotalScore; indexer priority breaks a
    /// tie on TotalScore and nothing more. Read downward, each concern can only influence the
    /// outcome where the one above it has not already settled it.</para>
    /// </summary>
    [Trait("Name", "ScorerConcernInteractionTests")]
    [Trait("Category", "Search")]
    public sealed class ScorerConcernInteractionTests : BaseTests
    {
        private const string BundleTitle = "Edgar Rice Burroughs - The Barsoom Collection: Books 1-6 [M4B]";
        private const string IndividualTitle = "Edgar Rice Burroughs - A Princess of Mars [M4B]";

        private static SearchResultScorer CreateScorer() =>
            new SearchResultScorer(null, NullLogger<ScorerConcernInteractionTests>.Instance);

        private static QualityProfile CreateProfile(
            ReleaseShapePreference preference = ReleaseShapePreference.NoPreference,
            List<string>? mustNotContain = null,
            List<string>? mustContain = null) =>
            new QualityProfile
            {
                PreferredFormats = new List<string> { "m4b" },
                PreferredLanguages = new List<string>(),
                PreferredWords = new List<string>(),
                MustContain = mustContain ?? new List<string>(),
                MustNotContain = mustNotContain ?? new List<string>(),
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

        // Item 172 over item 122: a forbidden word is a filter, the shape preference is not.
        // The reject returns before the shape block is reached, so a wanted shape cannot earn
        // back a release the operator has excluded by name.
        [Fact]
        public async Task ForbiddenWord_RejectsABundle_EvenWhenBundlesArePreferred()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(
                ReleaseShapePreference.PreferBundle,
                mustNotContain: new List<string> { "Collection" });

            var score = await scorer.Score(CreateResult(BundleTitle), profile);

            Assert.True(score.IsRejected);
            Assert.Equal(-1, score.TotalScore);
            Assert.DoesNotContain("ReleaseShapeMatch", score.ScoreBreakdown.Keys);
        }

        // The same pairing in the direction that matters for item 172's actual bug. Blocking
        // "abridged" must not take out an unabridged bundle, which before the word-boundary fix
        // it would have, leaving a bundle-preferring profile with nothing to grab.
        [Fact]
        public async Task ForbiddenAbridged_DoesNotRejectAnUnabridgedBundle()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(
                ReleaseShapePreference.PreferBundle,
                mustNotContain: new List<string> { "abridged" });

            var score = await scorer.Score(
                CreateResult("Edgar Rice Burroughs - The Barsoom Collection: Books 1-6 (Unabridged) [M4B]"),
                profile);

            Assert.False(score.IsRejected);
            Assert.Equal(5, score.ScoreBreakdown["ReleaseShapeMatch"]);
        }

        // Item 172's any-of required words over item 122: a bundle that matches none of the
        // required words is gone before its shape is considered.
        [Fact]
        public async Task RequiredWords_AreAppliedBeforeShapeScoring()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(
                ReleaseShapePreference.PreferBundle,
                mustContain: new List<string> { "retail", "unabridged" });

            var score = await scorer.Score(CreateResult(BundleTitle), profile);

            Assert.True(score.IsRejected);
            Assert.DoesNotContain("ReleaseShapeMatch", score.ScoreBreakdown.Keys);
        }

        // Item 122 over item 178: the shape preference lands in TotalScore, which is the
        // comparer's primary key, so a wanted shape settles the ordering and priority is never
        // consulted. This is the case that would regress if anyone later folded priority into
        // TotalScore as an additive term.
        [Fact]
        public async Task PreferredShape_OutranksIndexerPriority()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(ReleaseShapePreference.PreferBundle);

            var wantedShapeWorstIndexer = await scorer.Score(CreateResult(BundleTitle), profile);
            wantedShapeWorstIndexer.IndexerPriority = 50;

            var unwantedShapeBestIndexer = await scorer.Score(CreateResult(IndividualTitle), profile);
            unwantedShapeBestIndexer.IndexerPriority = 1;

            var ordered = new[] { unwantedShapeBestIndexer, wantedShapeWorstIndexer }
                .OrderByDescending(s => s, QualityScoreComparer.Instance)
                .ToList();

            Assert.Same(wantedShapeWorstIndexer, ordered[0]);
        }

        // Item 178 under item 122: with no shape preference the two candidates tie on
        // TotalScore, and only then does priority decide. Together with the test above this
        // pins the boundary of priority's influence from both sides.
        [Fact]
        public async Task WithNoShapePreference_IndexerPriorityBreaksTheResultingTie()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(ReleaseShapePreference.NoPreference);

            var bundleBestIndexer = await scorer.Score(CreateResult(BundleTitle), profile);
            bundleBestIndexer.IndexerPriority = 1;

            var individualWorstIndexer = await scorer.Score(CreateResult(IndividualTitle), profile);
            individualWorstIndexer.IndexerPriority = 50;

            Assert.Equal(individualWorstIndexer.TotalScore, bundleBestIndexer.TotalScore);

            var ordered = new[] { individualWorstIndexer, bundleBestIndexer }
                .OrderByDescending(s => s, QualityScoreComparer.Instance)
                .ToList();

            Assert.Same(bundleBestIndexer, ordered[0]);
        }

        // An omnibus library record overrides a PreferIndividual profile, and that override is
        // still subject to item 172's filters rather than bypassing them.
        [Fact]
        public async Task OmnibusTarget_OverridesPreferIndividual_ButStillObeysForbiddenWords()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(ReleaseShapePreference.PreferIndividual);

            var allowed = await scorer.Score(CreateResult(BundleTitle), profile, targetIsBundle: true);
            Assert.Equal(5, allowed.ScoreBreakdown["ReleaseShapeMatch"]);

            var filtered = CreateProfile(
                ReleaseShapePreference.PreferIndividual,
                mustNotContain: new List<string> { "Barsoom" });
            var rejected = await scorer.Score(CreateResult(BundleTitle), filtered, targetIsBundle: true);

            Assert.True(rejected.IsRejected);
        }
    }
}
