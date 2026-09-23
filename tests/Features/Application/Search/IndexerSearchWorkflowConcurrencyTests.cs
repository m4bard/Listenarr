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

namespace Listenarr.Tests.Features.Application.Search
{
    [Trait("Area", "Search")]
    [Trait("Name", "IndexerSearchWorkflowConcurrencyTests")]
    [Trait("Category", "IndexerSearchWorkflow")]
    public class IndexerSearchWorkflowConcurrencyTests : BaseTests
    {
        /// <summary>
        /// Fake provider that records the highest number of calls it ever had in flight
        /// at the same time, so tests can assert a concurrency cap actually caps.
        /// </summary>
        private sealed class ConcurrencyTrackingSearchProvider : IIndexerSearchProvider
        {
            private int _current;
            private int _maxObserved;

            public string IndexerType => "Torznab";

            public async Task<IndexerQueryObservation> SearchAsync(
                Indexer indexer,
                string query,
                string? category = null,
                SearchRequest? request = null,
                CancellationToken ct = default)
            {
                var current = Interlocked.Increment(ref _current);
                InterlockedMax(ref _maxObserved, current);
                try
                {
                    // Hold the "connection" open briefly so overlapping calls have a
                    // real chance to race each other rather than completing sequentially
                    // by coincidence.
                    await Task.Delay(50, ct);
                    return IndexerQueryObservation.FromResults(
                        new List<IndexerSearchResult>
                        {
                            new IndexerSearchResult { Title = $"{indexer.Name} result", Source = indexer.Name }
                        },
                        query);
                }
                finally
                {
                    Interlocked.Decrement(ref _current);
                }
            }

            public int MaxObservedConcurrency => _maxObserved;

            private static void InterlockedMax(ref int target, int value)
            {
                int initial, computed;
                do
                {
                    initial = target;
                    computed = Math.Max(initial, value);
                } while (Interlocked.CompareExchange(ref target, computed, initial) != initial);
            }
        }

        private static IndexerSearchWorkflow BuildWorkflow(
            List<Indexer> indexers,
            ConcurrencyTrackingSearchProvider provider,
            Action<Mock<IConfigurationService>>? configure = null)
        {
            var indexerRepository = new Mock<IIndexerRepository>();
            indexerRepository
                .Setup(r => r.GetEnabledAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(indexers);

            var configurationService = new Mock<IConfigurationService>();
            configure?.Invoke(configurationService);
            var settingsParser = new IndexerAdditionalSettingsParser(NullLogger<IndexerAdditionalSettingsParser>.Instance);

            return new IndexerSearchWorkflow(
                new HttpClient(),
                configurationService.Object,
                indexerRepository.Object,
                new[] { provider },
                settingsParser,
                NullLogger<IndexerSearchWorkflow>.Instance);
        }

        private static Action<Mock<IConfigurationService>> WithCeiling(int ceiling) =>
            mock => mock
                .Setup(c => c.GetApplicationSettingsAsync())
                .ReturnsAsync(new ApplicationSettings { MaxConcurrentIndexerSearches = ceiling });

        private static List<Indexer> BuildIndexers(int count)
        {
            var indexers = new List<Indexer>();
            for (var i = 0; i < count; i++)
            {
                indexers.Add(new Indexer
                {
                    Id = i + 1,
                    Name = $"Indexer{i + 1}",
                    Type = "Torrent",
                    Implementation = "Torznab",
                    Url = $"http://127.0.0.1:9117/indexer{i + 1}",
                    IsEnabled = true
                });
            }
            return indexers;
        }

        [Fact]
        [Trait("Method", "SearchIndexersAsync")]
        [Trait("Scenario", "IndexerFanOutNeverExceedsConcurrencyCap")]
        public async Task SearchIndexersAsync_MoreIndexersThanCap_NeverExceedsConcurrencyCap()
        {
            // Given: more enabled indexers (10) than the fan-out cap (4).
            const int indexerCount = 10;
            var indexers = BuildIndexers(indexerCount);
            var provider = new ConcurrencyTrackingSearchProvider();
            var workflow = BuildWorkflow(indexers, provider);

            // When
            var results = await workflow.SearchIndexersAsync("test query");

            // Then: the cap was actually enforced, not just coincidentally respected.
            Assert.True(
                provider.MaxObservedConcurrency <= 4,
                $"Expected at most 4 concurrent indexer searches, observed {provider.MaxObservedConcurrency}");
            Assert.True(
                provider.MaxObservedConcurrency > 1,
                "Expected some overlap between indexer searches (test would not detect a missing cap otherwise)");

            // And: capping concurrency must not silently drop indexers -- every one of the
            // 10 configured indexers must have contributed its result.
            Assert.Equal(indexerCount, results.Count);
            var sources = results.Select(r => r.Source).OrderBy(s => s).ToList();
            var expectedSources = indexers.Select(i => i.Name).OrderBy(n => n).ToList();
            Assert.Equal(expectedSources, sources);
        }

        [Fact]
        [Trait("Method", "SearchIndexersAsync")]
        [Trait("Scenario", "OperatorLowersConcurrencyCeiling")]
        public async Task SearchIndexersAsync_CeilingSetToTwo_NeverRunsMoreThanTwoAtOnce()
        {
            var indexers = BuildIndexers(10);
            var provider = new ConcurrencyTrackingSearchProvider();
            var workflow = BuildWorkflow(indexers, provider, WithCeiling(2));

            var results = await workflow.SearchIndexersAsync("test query");

            Assert.Equal(2, provider.MaxObservedConcurrency);
            Assert.Equal(10, results.Count);
        }

        [Fact]
        [Trait("Method", "SearchIndexersAsync")]
        [Trait("Scenario", "OperatorRaisesConcurrencyCeiling")]
        public async Task SearchIndexersAsync_CeilingSetToEight_RunsMoreThanTheShippedFour()
        {
            // The mirror of the test above. Without it a hardcoded 4 would satisfy "at most 2"
            // only by failing it, and a ceiling that could be lowered but never raised would pass.
            var indexers = BuildIndexers(10);
            var provider = new ConcurrencyTrackingSearchProvider();
            var workflow = BuildWorkflow(indexers, provider, WithCeiling(8));

            var results = await workflow.SearchIndexersAsync("test query");

            Assert.True(
                provider.MaxObservedConcurrency > 4,
                $"Expected more than 4 concurrent indexer searches with a ceiling of 8, observed {provider.MaxObservedConcurrency}");
            Assert.True(provider.MaxObservedConcurrency <= 8);
            Assert.Equal(10, results.Count);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-3)]
        [Trait("Method", "SearchIndexersAsync")]
        [Trait("Scenario", "NonPositiveCeilingRunsOneAtATime")]
        public async Task SearchIndexersAsync_NonPositiveCeiling_RunsOneAtATimeRatherThanUnbounded(int ceiling)
        {
            // Parallel.ForEachAsync reads -1 as "no limit" and throws on 0. Neither may leak
            // through: a zero typed into the setting is clamped to the gentlest ceiling there is.
            var indexers = BuildIndexers(5);
            var provider = new ConcurrencyTrackingSearchProvider();
            var workflow = BuildWorkflow(indexers, provider, WithCeiling(ceiling));

            var results = await workflow.SearchIndexersAsync("test query");

            Assert.Equal(1, provider.MaxObservedConcurrency);
            Assert.Equal(5, results.Count);
        }

        [Fact]
        [Trait("Method", "SearchIndexersAsync")]
        [Trait("Scenario", "SettingsReadFailureKeepsShippedCeiling")]
        public async Task SearchIndexersAsync_SettingsReadThrows_SearchesAtTheShippedCeilingOfFour()
        {
            // A settings read that fails must not fail the search, and must not fall back to
            // anything wider than what shipped.
            var indexers = BuildIndexers(10);
            var provider = new ConcurrencyTrackingSearchProvider();
            var workflow = BuildWorkflow(indexers, provider, mock => mock
                .Setup(c => c.GetApplicationSettingsAsync())
                .ThrowsAsync(new InvalidOperationException("settings unavailable")));

            var results = await workflow.SearchIndexersAsync("test query");

            Assert.InRange(provider.MaxObservedConcurrency, 2, 4);
            Assert.Equal(10, results.Count);
        }

        [Fact]
        [Trait("Scenario", "ShippedCeilingIsFour")]
        public void ApplicationSettings_DefaultCeiling_IsTheFourThatWasHardcoded()
        {
            Assert.Equal(4, new ApplicationSettings().MaxConcurrentIndexerSearches);
        }
    }
}
