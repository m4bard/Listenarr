/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search
{
    // Full-cycle regression coverage for the author-free fallback (#1068/#1072). The classifier
    // unit tests stop at ConstrainFallbackResultsToTitle; these drive the whole automatic-search
    // cycle (AutomaticSearchProcessor.RunCycleAsync) through to the download-service handoff, so a
    // broadened-but-wrong result is proven never to reach StartDownloadAsync, while a correctly
    // attributed fallback is proven to reach it.
    [Trait("Name", "AutomaticSearchProcessorFallbackTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class AutomaticSearchProcessorFallbackTests : BaseTests
    {
        [Fact]
        public Task RunCycleAsync_AuthorFreeFallback_SameWordDifferentBook_IsNotDownloaded() =>
            AssertFallbackNotDownloadedAsync(
                title: "Peter Pan",
                author: "J M Barrie",
                fallbackResultTitle: "James M. Barrie - Peter Pan in Kensington Gardens");

        [Fact]
        public Task RunCycleAsync_AuthorFreeFallback_SameTitleDifferentAuthor_IsNotDownloaded() =>
            AssertFallbackNotDownloadedAsync(
                title: "Faust",
                author: "Johann Wolfgang von Goethe",
                fallbackResultTitle: "Leo Tolstoy - Faust");

        [Fact]
        public async Task RunCycleAsync_AuthorFreeFallback_CorrectlyAttributed_ReachesDownloadSelection()
        {
            var fullQuery = "Peter Pan J M Barrie";
            var fallbackQuery = "Peter Pan";
            var correct = new IndexerSearchResult
            {
                Title = "J M Barrie - Peter Pan - M4B",
                DownloadType = DirectDownloadMetadataKeys.ClientId
            };

            var search = new Mock<ISearchService>();
            SetupSearch(search, fullQuery, fallbackQuery, new List<IndexerSearchResult> { correct });
            var download = new Mock<IDownloadService>();
            download.Setup(d => d.StartDownloadAsync(
                    It.IsAny<SearchResult>(), It.IsAny<string>(), It.IsAny<int?>()))
                .ReturnsAsync("download-1");
            // Isolate the fallback -> selection wiring from the quality scorer (its own tests cover
            // scoring): accept the constrained result with a positive, non-rejected score.
            var quality = new Mock<IQualityProfileService>();
            quality.Setup(q => q.ScoreSearchResults(
                    It.IsAny<List<SearchResult>>(), It.IsAny<QualityProfile>()))
                .ReturnsAsync((List<SearchResult> results, QualityProfile _) =>
                    results.Select(r => new QualityScore { SearchResult = r, TotalScore = 100 }).ToList());
            Init(builder =>
            {
                builder.WithSingleton(search.Object);
                builder.WithSingleton(download.Object);
                builder.WithSingleton(quality.Object);
            });
            var audiobook = await SeedMonitoredAudiobookAsync("Peter Pan", "J M Barrie");

            await RunCycleAsync();

            download.Verify(d => d.StartDownloadAsync(
                    It.Is<SearchResult>(r => r.Title == correct.Title),
                    DirectDownloadMetadataKeys.ClientId,
                    audiobook.Id),
                Times.Once);
        }

        private async Task AssertFallbackNotDownloadedAsync(
            string title,
            string author,
            string fallbackResultTitle)
        {
            var fullQuery = $"{title} {author}";
            var fallbackQuery = title;
            var search = new Mock<ISearchService>();
            SetupSearch(
                search,
                fullQuery,
                fallbackQuery,
                new List<IndexerSearchResult> { new() { Title = fallbackResultTitle } });
            var download = new Mock<IDownloadService>(MockBehavior.Strict);
            Init(builder =>
            {
                builder.WithSingleton(search.Object);
                builder.WithSingleton(download.Object);
            });
            await SeedMonitoredAudiobookAsync(title, author);

            await RunCycleAsync();

            // The guard drops the wrong book before scoring, so the download service is never asked
            // to grab it (strict mock would throw on any call).
            download.Verify(d => d.StartDownloadAsync(
                It.IsAny<SearchResult>(), It.IsAny<string>(), It.IsAny<int?>()), Times.Never);
        }

        private static void SetupSearch(
            Mock<ISearchService> search,
            string fullQuery,
            string fallbackQuery,
            List<IndexerSearchResult> fallbackResults)
        {
            SetupQuery(search, fullQuery, new List<IndexerSearchResult>());
            SetupQuery(search, fallbackQuery, fallbackResults);
        }

        // The sweep searches one indexer per call; each call answers for the indexer it names.
        private static void SetupQuery(Mock<ISearchService> search, string query, List<IndexerSearchResult> results) =>
            search.Setup(s => s.SearchIndexersDetailedAsync(
                    query,
                    It.IsAny<IndexerSearchContext>(),
                    It.IsAny<IReadOnlyCollection<int>?>(),
                    It.IsAny<bool>(),
                    It.IsAny<SearchRequest?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, IndexerSearchContext _, IReadOnlyCollection<int>? indexerIds, bool _, SearchRequest? _, CancellationToken _) =>
                    new IndexerSearchReport(indexerIds!
                        .Select(id => new IndexerSearchOutcome(id, IndexerSearchStatus.Completed, results, RequestsSent: 1))
                        .ToList()));

        private async Task<Audiobook> SeedMonitoredAudiobookAsync(string title, string author)
        {
            var indexer = new IndexerBuilder().WithName("Only").WithUrl("https://indexer.invalid").WithEnabled().Build();
            indexer.EnableAutomaticSearch = true;
            await _indexerRepository.AddAsync(indexer);
            return await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle(title)
                .WithAuthor(author)
                .WithMonitored()
                .WithQualityProfile(new QualityProfile { Name = "Any" })
                .Build());
        }

        private async Task RunCycleAsync()
        {
            var processor = new AutomaticSearchProcessor(
                NullLogger<AutomaticSearchProcessor>.Instance,
                _provider.GetRequiredService<IServiceScopeFactory>());
            await processor.RunCycleAsync(CancellationToken.None);
        }
    }
}
