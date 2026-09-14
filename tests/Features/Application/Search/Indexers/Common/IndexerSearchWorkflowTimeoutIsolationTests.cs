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

namespace Listenarr.Tests.Features.Application.Search.Indexers.Common
{
    /// <summary>
    /// Regression coverage for the per-indexer timeout isolation fix: one indexer's HTTP
    /// timeout (TaskCanceledException from HttpClient.Timeout) must not abort the whole
    /// multi-indexer Task.WhenAll fan-out and discard every other indexer's results.
    /// </summary>
    [Trait("Area", "Search")]
    [Trait("Name", "IndexerSearchWorkflowTimeoutIsolationTests")]
    [Trait("Category", "IndexerSearchWorkflow")]
    public class IndexerSearchWorkflowTimeoutIsolationTests : BaseTests
    {
        private static IndexerSearchWorkflow CreateWorkflow(
            List<Indexer> enabledIndexers,
            IEnumerable<IIndexerSearchProvider> providers)
        {
            var indexerRepository = new Mock<IIndexerRepository>();
            indexerRepository
                .Setup(r => r.GetEnabledAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(enabledIndexers);

            return new IndexerSearchWorkflow(
                new HttpClient(),
                Mock.Of<IConfigurationService>(),
                indexerRepository.Object,
                providers,
                new IndexerAdditionalSettingsParser(NullLogger<IndexerAdditionalSettingsParser>.Instance),
                NullLogger<IndexerSearchWorkflow>.Instance);
        }

        private static Indexer MakeIndexer(int id, string name, string implementation) => new()
        {
            Id = id,
            Name = name,
            Type = "Torrent",
            Implementation = implementation,
            Url = "https://example.invalid",
            IsEnabled = true,
        };

        [Fact]
        [Trait("Area", "Search")]
        [Trait("Method", "SearchIndexersAsync")]
        [Trait("Scenario", "OneIndexerTimeout_OthersStillReturnResults")]
        public async Task SearchIndexersAsync_OneIndexerTimesOut_OtherIndexerResultsStillReturned()
        {
            // Given: two enabled indexers. The Torznab one's provider simulates an
            // HttpClient.Timeout expiry (TaskCanceledException, caller never cancelled).
            // The MyAnonamouse one's provider returns real results.
            var timingOutIndexer = MakeIndexer(1, "Timing Out Indexer", "Torznab");
            var healthyIndexer = MakeIndexer(2, "Healthy Indexer", "MyAnonamouse");

            var timingOutProvider = new Mock<IIndexerSearchProvider>();
            timingOutProvider.SetupGet(p => p.IndexerType).Returns("Torznab");
            timingOutProvider
                .Setup(p => p.SearchAsync(
                    It.IsAny<Indexer>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<SearchRequest?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TaskCanceledException("Simulated HttpClient.Timeout expiry"));

            var healthyResults = new List<IndexerSearchResult>
            {
                new() { Id = "r1", Title = "Real Result One", Source = healthyIndexer.Name, IndexerId = healthyIndexer.Id },
                new() { Id = "r2", Title = "Real Result Two", Source = healthyIndexer.Name, IndexerId = healthyIndexer.Id },
            };
            var healthyProvider = new Mock<IIndexerSearchProvider>();
            healthyProvider.SetupGet(p => p.IndexerType).Returns("MyAnonamouse");
            healthyProvider
                .Setup(p => p.SearchAsync(
                    It.IsAny<Indexer>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<SearchRequest?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(IndexerQueryObservation.FromResults(healthyResults, "some query"));

            var workflow = CreateWorkflow(
                new List<Indexer> { timingOutIndexer, healthyIndexer },
                new[] { timingOutProvider.Object, healthyProvider.Object });

            // When: no external cancellation is requested - the default token is never cancelled.
            var results = await workflow.SearchIndexersAsync("some query");

            // Then: the healthy indexer's real results survive. The whole search did not fail
            // or come back empty just because one indexer's request timed out.
            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.Id == "r1");
            Assert.Contains(results, r => r.Id == "r2");
        }

        [Fact]
        [Trait("Area", "Search")]
        [Trait("Method", "SearchIndexersAsync")]
        [Trait("Scenario", "GenuineCancellation_StillPropagates")]
        public async Task SearchIndexersAsync_GenuineCancellation_PropagatesAndIsNotSwallowed()
        {
            // Given: a single indexer whose provider throws OperationCanceledException while
            // the caller's own token has genuinely been cancelled (e.g. app shutdown), not a
            // per-request HttpClient timeout.
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var indexer = MakeIndexer(1, "Some Indexer", "Torznab");

            var provider = new Mock<IIndexerSearchProvider>();
            provider.SetupGet(p => p.IndexerType).Returns("Torznab");
            provider
                .Setup(p => p.SearchAsync(
                    It.IsAny<Indexer>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<SearchRequest?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException("Simulated genuine cancellation"));

            var workflow = CreateWorkflow(new List<Indexer> { indexer }, new[] { provider.Object });

            // When / Then: the cancellation must not be silently swallowed and treated as an
            // isolated per-indexer failure - it must propagate out of the fan-out, because
            // ct.IsCancellationRequested is true for this call.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => workflow.SearchIndexersAsync("some query", ct: cts.Token));
        }
    }
}
