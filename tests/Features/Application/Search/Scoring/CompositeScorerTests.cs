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
    // Regression tests for Listenarr#178: CompositeScorer's indexer-priority term used to be
    // (51 - priority) * 1000.0, a term large enough (up to 50,000 points) to outrank a real
    // quality/format difference on the Smart-sort path. It is now scaled down to tie-break
    // magnitude only (see CompositeScorer.IndexerPriorityTieBreakWeight).
    [Trait("Name", "CompositeScorerTests")]
    [Trait("Category", "Application")]
    public sealed class CompositeScorerTests : BaseTests
    {
        private static Indexer MakeIndexer(int priority) =>
            new() { Name = $"Indexer-{priority}", Url = "https://test.local", Priority = priority, IsEnabled = true };

        [Fact]
        public void IndexerPriority_DoesNotOverride_MeaningfullyBetterQuality()
        {
            // Worst possible indexer priority paired with the best quality/format.
            var bestQualityWorstIndexer = new SearchResult
            {
                Title = "Best Quality, Worst-Priority Indexer",
                Quality = "FLAC",
                Format = "flac",
                Seeders = 1,
                Size = 200 * 1024 * 1024,
                PublishedDate = DateTime.UtcNow.ToString("o")
            };

            // Best possible indexer priority paired with a meaningfully worse quality/format.
            var worstQualityBestIndexer = new SearchResult
            {
                Title = "Worst Quality, Best-Priority Indexer",
                Quality = "MP3 64kbps",
                Format = "mp3",
                Seeders = 1,
                Size = 200 * 1024 * 1024,
                PublishedDate = DateTime.UtcNow.ToString("o")
            };

            var bestQualityScore = CompositeScorer.CalculateProwlarrStyleScore(bestQualityWorstIndexer, MakeIndexer(50));
            var worstQualityScore = CompositeScorer.CalculateProwlarrStyleScore(worstQualityBestIndexer, MakeIndexer(1));

            Assert.True(bestQualityScore.Total > worstQualityScore.Total,
                $"Expected the better-quality release ({bestQualityScore.Total}) on the lowest-priority indexer " +
                $"to still outrank the worse-quality release ({worstQualityScore.Total}) on the highest-priority indexer");
        }

        [Fact]
        public void IndexerPriority_BreaksExactTie_WhenEverythingElseIsEqual()
        {
            SearchResult MakeResult() => new()
            {
                Title = "Identical Release",
                Quality = "MP3 320kbps",
                Format = "mp3",
                Seeders = 10,
                Size = 200 * 1024 * 1024,
                PublishedDate = DateTime.UtcNow.ToString("o")
            };

            var topPriorityScore = CompositeScorer.CalculateProwlarrStyleScore(MakeResult(), MakeIndexer(1));
            var bottomPriorityScore = CompositeScorer.CalculateProwlarrStyleScore(MakeResult(), MakeIndexer(50));

            Assert.True(topPriorityScore.Total > bottomPriorityScore.Total,
                "Priority-1 indexer should win a tie against a priority-50 indexer");

            // The delta must stay at tie-break magnitude, not swamp the other tiers.
            var delta = topPriorityScore.Total - bottomPriorityScore.Total;
            Assert.True(delta <= 50, $"Expected the full priority-range delta to be small (<=50), got {delta}");
        }

        [Fact]
        public void NullIndexer_IndexerBreakdownIsZero()
        {
            var result = new SearchResult { Title = "No Indexer", Quality = "MP3 320kbps" };

            var score = CompositeScorer.CalculateProwlarrStyleScore(result, indexer: null);

            Assert.Equal(0, score.Breakdown["Indexer"]);
        }

        [Fact]
        public void IndexerPriority_ProducesNonZeroBreakdown_WhenIndexerProvided()
        {
            var result = new SearchResult { Title = "Known Indexer", Quality = "MP3 320kbps" };

            var score = CompositeScorer.CalculateProwlarrStyleScore(result, MakeIndexer(1));

            Assert.True(score.Breakdown["Indexer"] > 0);
            Assert.Equal(50.0, score.Breakdown["Indexer"]);
        }
    }
}
