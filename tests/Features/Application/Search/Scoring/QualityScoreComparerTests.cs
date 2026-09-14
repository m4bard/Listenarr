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

namespace Listenarr.Tests.Features.Application.Search.Scoring
{
    // Regression tests for Listenarr#178, Bug 1: automatic search picked the top result via
    // OrderByDescending(s => s.TotalScore), so Indexer.Priority never factored into the decision
    // at all. QualityScoreComparer now makes priority a genuine last-resort tie-break, mirroring
    // Readarr's DownloadDecisionComparer shape: it compares TotalScore first (which already
    // captures quality/format/language/seeders/age) and only consults IndexerPriority when two
    // results are exactly tied on that score.
    [Trait("Name", "QualityScoreComparerTests")]
    [Trait("Category", "Application")]
    public sealed class QualityScoreComparerTests : BaseTests
    {
        private static QualityScore MakeScore(int totalScore, int? indexerPriority) =>
            new() { TotalScore = totalScore, IndexerPriority = indexerPriority };

        [Fact]
        public void HigherTotalScore_AlwaysWins_RegardlessOfIndexerPriority()
        {
            // Meaningfully better result on the worst-priority indexer versus a meaningfully
            // worse result on the best-priority indexer. This is the regression case for the
            // original bug, where priority-as-an-additive-term could let indexer choice reverse
            // this outcome.
            var betterResultWorstIndexer = MakeScore(totalScore: 90, indexerPriority: 50);
            var worseResultBestIndexer = MakeScore(totalScore: 40, indexerPriority: 1);

            var ordered = new[] { worseResultBestIndexer, betterResultWorstIndexer }
                .OrderByDescending(s => s, QualityScoreComparer.Instance)
                .ToList();

            Assert.Same(betterResultWorstIndexer, ordered[0]);
        }

        [Fact]
        public void ExactTie_IsBrokenByIndexerPriority_LowerNumberWins()
        {
            var topPriority = MakeScore(totalScore: 70, indexerPriority: 1);
            var bottomPriority = MakeScore(totalScore: 70, indexerPriority: 50);

            var ordered = new[] { bottomPriority, topPriority }
                .OrderByDescending(s => s, QualityScoreComparer.Instance)
                .ToList();

            Assert.Same(topPriority, ordered[0]);
        }

        [Fact]
        public void ExactTie_UnknownIndexerPriority_NeverBeatsAKnownPriority()
        {
            var unknownPriority = MakeScore(totalScore: 70, indexerPriority: null);
            var knownPriority = MakeScore(totalScore: 70, indexerPriority: 50); // worst known priority

            var ordered = new[] { unknownPriority, knownPriority }
                .OrderByDescending(s => s, QualityScoreComparer.Instance)
                .ToList();

            Assert.Same(knownPriority, ordered[0]);
        }

        [Fact]
        public void ExactTie_SameIndexerPriority_IsStableAndDoesNotThrow()
        {
            var a = MakeScore(totalScore: 70, indexerPriority: 25);
            var b = MakeScore(totalScore: 70, indexerPriority: 25);

            Assert.Equal(0, QualityScoreComparer.Instance.Compare(a, b));
        }
    }
}
