/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Application.Search.Scheduling;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search
{
    // The acquisition seam is the post-search half only: every case asserts that no request reached
    // the gate and no indexer provider was asked, whatever the outcome.
    [Trait("Name", "AutomaticSearchAcquisitionTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class AutomaticSearchAcquisitionTests : BaseTests
    {
        private readonly CountingGate _gate = new();
        private readonly CountingProvider _searchProvider = new();
        private readonly Mock<IDownloadService> _downloadService = new();
        private readonly Mock<IQualityProfileService> _qualityProfileService = new();

        [Fact]
        public async Task AcceptableCandidateWithAClient_IsSatisfied()
        {
            var book = await ArrangeAsync();

            var result = await AcquireAsync(book, Candidate(DirectDownloadMetadataKeys.ClientId));

            Assert.Equal(BookSearchOutcome.Satisfied, result.Outcome);
            Assert.Equal(1, result.DownloadsQueued);
            _downloadService.Verify(d => d.StartDownloadAsync(
                It.IsAny<SearchResult>(), DirectDownloadMetadataKeys.ClientId, book.Id), Times.Once);
            AssertNoIndexerTraffic();
        }

        [Fact]
        public async Task CutoffMet_IsNotNeeded()
        {
            var book = await ArrangeAsync(new QualityProfile
            {
                Name = "Cutoff",
                CutoffQuality = "MP3",
                Qualities = new List<QualityDefinition> { new() { Quality = "MP3", Priority = 1 } }
            });
            var completed = new DownloadBuilder()
                .WithId("held")
                .WithAudiobookId(book.Id)
                .WithStatus(DownloadStatus.Completed)
                .Build();
            completed.Metadata = new Dictionary<string, object> { ["Quality"] = "MP3" };
            await _downloadRepository.AddAsync(completed);

            var result = await AcquireAsync(book, Candidate(DirectDownloadMetadataKeys.ClientId));

            Assert.Equal(BookSearchOutcome.NotNeeded, result.Outcome);
            _qualityProfileService.Verify(q => q.ScoreSearchResults(
                It.IsAny<List<SearchResult>>(), It.IsAny<QualityProfile>()), Times.Never);
            AssertNoIndexerTraffic();
        }

        [Fact]
        public async Task ScorerThrows_IsBookFailed()
        {
            var book = await ArrangeAsync();
            _qualityProfileService
                .Setup(q => q.ScoreSearchResults(It.IsAny<List<SearchResult>>(), It.IsAny<QualityProfile>()))
                .ThrowsAsync(new InvalidOperationException("scoring failed"));

            var result = await AcquireAsync(book, Candidate(DirectDownloadMetadataKeys.ClientId));

            Assert.Equal(BookSearchOutcome.BookFailed, result.Outcome);
            AssertNoIndexerTraffic();
        }

        [Fact]
        public async Task NoSuitableDownloadClient_IsGrabNotCompleted()
        {
            var book = await ArrangeAsync();

            var result = await AcquireAsync(book, Candidate("Torrent"));

            Assert.Equal(BookSearchOutcome.GrabNotCompleted, result.Outcome);
            Assert.Equal(0, result.DownloadsQueued);
            Assert.Null(result.GrabDeferral);
            _downloadService.Verify(d => d.StartDownloadAsync(
                It.IsAny<SearchResult>(), It.IsAny<string>(), It.IsAny<int?>()), Times.Never);
            AssertNoIndexerTraffic();
        }

        [Fact]
        public async Task DownloadStartThrows_IsGrabNotCompleted()
        {
            var book = await ArrangeAsync();
            _downloadService
                .Setup(d => d.StartDownloadAsync(It.IsAny<SearchResult>(), It.IsAny<string>(), It.IsAny<int?>()))
                .ThrowsAsync(new InvalidOperationException("client refused"));

            var result = await AcquireAsync(book, Candidate(DirectDownloadMetadataKeys.ClientId));

            Assert.Equal(BookSearchOutcome.GrabNotCompleted, result.Outcome);
            AssertNoIndexerTraffic();
        }

        [Fact]
        public async Task NoCandidates_IsNoAcceptableResults()
        {
            var book = await ArrangeAsync();

            var result = await AcquireAsync(book);

            Assert.Equal(BookSearchOutcome.NoAcceptableResults, result.Outcome);
            AssertNoIndexerTraffic();
        }

        private async Task<Audiobook> ArrangeAsync(QualityProfile? profile = null)
        {
            _downloadService
                .Setup(d => d.StartDownloadAsync(It.IsAny<SearchResult>(), It.IsAny<string>(), It.IsAny<int?>()))
                .ReturnsAsync("download-1");
            _qualityProfileService
                .Setup(q => q.ScoreSearchResults(It.IsAny<List<SearchResult>>(), It.IsAny<QualityProfile>()))
                .ReturnsAsync((List<SearchResult> results, QualityProfile _) =>
                    results.Select(r => new QualityScore { SearchResult = r, TotalScore = 100 }).ToList());
            Init(builder =>
            {
                builder.WithSingleton<IIndexerRequestGate>(_gate);
                builder.WithSingleton<IIndexerSearchProvider>(_searchProvider);
                builder.WithSingleton(_downloadService.Object);
                builder.WithSingleton(_qualityProfileService.Object);
            });
            // An indexer the provider serves, so a search issued by mistake would be counted.
            await _indexerRepository.AddAsync(new IndexerBuilder()
                .WithName("Counted")
                .WithImplementation(CountingProvider.Implementation)
                .WithUrl("https://indexer.invalid")
                .WithEnabled()
                .Build());
            return await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("A Princess of Mars")
                .WithAuthor("Edgar Rice Burroughs")
                .WithMonitored()
                .WithQualityProfile(profile ?? new QualityProfile { Name = "Any" })
                .Build());
        }

        private async Task<AcquisitionResult> AcquireAsync(Audiobook book, params IndexerSearchResult[] candidates)
        {
            using var scope = _provider.CreateScope();
            var acquisition = scope.ServiceProvider.GetRequiredService<IAutomaticSearchAcquisition>();
            var loaded = await scope.ServiceProvider.GetRequiredService<IAudiobookRepository>().GetByIdAsync(book.Id);
            return await acquisition.AcquireAsync(loaded!, candidates, SearchOrigin.Sweep, CancellationToken.None);
        }

        private static IndexerSearchResult Candidate(string downloadType) => new()
        {
            Title = "Edgar Rice Burroughs - A Princess of Mars - M4B",
            DownloadType = downloadType,
            MagnetLink = downloadType == "Torrent" ? "magnet:?xt=urn:btih:0000000000000000000000000000000000000000" : string.Empty
        };

        private void AssertNoIndexerTraffic()
        {
            Assert.Equal(0, _gate.Calls);
            Assert.Equal(0, _searchProvider.Calls);
        }

        private sealed class CountingGate : IIndexerRequestGate
        {
            private int _calls;

            public int Calls => Volatile.Read(ref _calls);

            public async Task<IndexerRequestResult> SendAsync(
                IndexerRequestContext context,
                Func<CancellationToken, Task<HttpResponseMessage>> dispatch,
                CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _calls);
                return new IndexerRequestResult.Sent(await dispatch(cancellationToken));
            }

            public Task<RequestDeferral?> GetDeferralAsync(int indexerId, SearchOrigin origin, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _calls);
                return Task.FromResult<RequestDeferral?>(null);
            }
        }

        private sealed class CountingProvider : IIndexerSearchProvider
        {
            public const string Implementation = "AcquisitionCountingIndexer";
            private int _calls;

            public string IndexerType => Implementation;

            public int Calls => Volatile.Read(ref _calls);

            public Task<IndexerSearchOutcome> SearchAsync(
                Indexer indexer,
                string query,
                string? category,
                SearchRequest? request,
                IndexerSearchContext context,
                CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _calls);
                return Task.FromResult(new IndexerSearchOutcome(
                    indexer.Id, IndexerSearchStatus.Completed, Array.Empty<IndexerSearchResult>(), RequestsSent: 1));
            }
        }
    }
}
