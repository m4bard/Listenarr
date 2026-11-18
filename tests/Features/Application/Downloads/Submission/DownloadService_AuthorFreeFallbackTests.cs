using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Downloads.Submission
{
    /// <summary>
    /// The Search button path (<see cref="DownloadService.SearchAndDownloadAsync"/> with
    /// automatic scoring) retries once without the author, as the automatic sweep does (#801).
    /// The retry asks again only the indexers whose full query completed with no results, as a
    /// fallback query for the same origin; a failed or deferred indexer is not asked again.
    /// </summary>
    [Trait("Name", "DownloadService_AuthorFreeFallbackTests")]
    [Trait("Category", "Downloads")]
    public class DownloadService_AuthorFreeFallbackTests : BaseTests
    {
        private const string Title = "A Princess of Mars";
        private const string Author = "Edgar Rice Burroughs";
        private const string FullQuery = Title + " " + Author;
        private const string? AnyQuery = null;

        private Mock<ISearchService> _searchService = null!;
        private readonly List<PreparedDownloadSubmission> _submitted = new();

        public override Task InitializeAsync()
        {
            _searchService = new Mock<ISearchService>(MockBehavior.Strict);
            var gateway = new Mock<IDownloadClientGateway>();
            gateway
                .Setup(g => g.AddAsync(It.IsAny<DownloadClientConfiguration>(), It.IsAny<PreparedDownloadSubmission>(), It.IsAny<CancellationToken>()))
                .Callback<DownloadClientConfiguration, PreparedDownloadSubmission, CancellationToken>((_, submission, _) => _submitted.Add(submission))
                .ReturnsAsync(new DownloadClientSubmissionResult("ABCDEF1234567890ABCDEF1234567890ABCDEF12"));
            _services.AddSingleton(_searchService.Object);
            _services.AddSingleton(gateway.Object);
            Init();
            return Task.CompletedTask;
        }

        [Fact]
        public async Task EmptyFullQuery_RetriesTitleOnly_AndSubmitsTheFallbackRelease()
        {
            var book = await SeedAsync(Title, Author);
            var release = Release(Title + " [M4B]");
            ReturnsFull(FullQuery, Completed(1));
            ReturnsFallback(Title, [1], Completed(1, release));

            var result = await SearchAndDownloadAsync(book);

            Assert.True(result.Success, result.Message);
            Assert.Equal(release.Title, Assert.Single(_submitted).Title);
            VerifyFull(FullQuery, Times.Once());
            VerifyFallback(Title, Times.Once());
        }

        [Fact]
        public async Task FullQueryWithResults_SearchesWithTheAuthorFirst_AndDoesNotRetry()
        {
            var book = await SeedAsync(Title, Author);
            ReturnsFull(FullQuery, Completed(1, Release(Title + " [M4B]", Author)));

            var result = await SearchAndDownloadAsync(book);

            Assert.True(result.Success, result.Message);
            VerifyFull(FullQuery, Times.Once());
            VerifyFallback(AnyQuery, Times.Never());
        }

        // As upstream: the retry is for a search that found nothing at all, so an indexer that
        // answered empty is not asked again while another indexer found releases.
        [Fact]
        public async Task AnotherIndexerWithResults_EmptyIndexerIsNotAskedAgain()
        {
            var book = await SeedAsync(Title, Author);
            ReturnsFull(FullQuery, Completed(1), Completed(2, Release(Title + " [M4B]", Author)));

            var result = await SearchAndDownloadAsync(book);

            Assert.True(result.Success, result.Message);
            VerifyFallback(AnyQuery, Times.Never());
        }

        [Fact]
        public async Task BothQueriesEmpty_RetriesExactlyOnce_AndReportsNoResults()
        {
            var book = await SeedAsync(Title, Author);
            ReturnsFull(FullQuery, Completed(1));
            ReturnsFallback(Title, [1], Completed(1));

            var result = await SearchAndDownloadAsync(book);

            Assert.False(result.Success);
            Assert.Equal("No search results found", result.Message);
            VerifyFull(AnyQuery, Times.Once());
            VerifyFallback(AnyQuery, Times.Once());
        }

        [Fact]
        public async Task BookWithoutAuthor_DoesNotRepeatTheSameQuery()
        {
            var book = await SeedAsync(Title);
            ReturnsFull(Title, Completed(1));

            var result = await SearchAndDownloadAsync(book);

            Assert.False(result.Success);
            VerifyFull(Title, Times.Once());
            VerifyFallback(AnyQuery, Times.Never());
        }

        // The fallback is built in the same query form as the full query, so an edition
        // annotation the full query drops is not sent back to the indexers on the retry.
        [Fact]
        public async Task AnnotatedTitle_RetriesWithTheQueryTitle()
        {
            var book = await SeedAsync(Title + " (Unabridged)", Author);
            ReturnsFull(FullQuery, Completed(1));
            ReturnsFallback(Title, [1], Completed(1));

            var result = await SearchAndDownloadAsync(book);

            Assert.Equal("No search results found", result.Message);
            VerifyFallback(Title, Times.Once());
        }

        [Fact]
        public async Task OnlyIndexersThatAnsweredEmpty_AreAskedAgain()
        {
            var book = await SeedAsync(Title, Author);
            ReturnsFull(FullQuery,
                Completed(1),
                new IndexerSearchOutcome(2, IndexerSearchStatus.Failed, [], RequestsSent: 1, FailureReason: "HTTP 500"),
                new IndexerSearchOutcome(3, IndexerSearchStatus.Deferred, [], RequestsSent: 0, Deferral(RequestDeferralReason.RateLimited)),
                Completed(4));
            ReturnsFallback(Title, [1, 4], Completed(1, Release(Title + " [M4B]")), Completed(4));

            var result = await SearchAndDownloadAsync(book);

            Assert.True(result.Success, result.Message);
            VerifyFallback(Title, Times.Once());
        }

        [Fact]
        public async Task NoIndexerAnswered_DoesNotRetry_AndReportsTheDeferral()
        {
            var book = await SeedAsync(Title, Author);
            var deferral = Deferral(RequestDeferralReason.RateLimited);
            ReturnsFull(FullQuery,
                new IndexerSearchOutcome(1, IndexerSearchStatus.Failed, [], RequestsSent: 1, FailureReason: "timeout"),
                new IndexerSearchOutcome(2, IndexerSearchStatus.Deferred, [], RequestsSent: 0, deferral));

            var result = await SearchAndDownloadAsync(book);

            Assert.False(result.Success);
            Assert.Equal(deferral, result.Deferral);
            VerifyFallback(AnyQuery, Times.Never());
        }

        // A fallback the gate held back decides how that indexer's search ended, so the book
        // reports the deferral rather than that nothing was found.
        [Fact]
        public async Task DeferredFallback_ReportsTheDeferral()
        {
            var book = await SeedAsync(Title, Author);
            var deferral = Deferral(RequestDeferralReason.RateLimited);
            ReturnsFull(FullQuery, Completed(1));
            ReturnsFallback(Title, [1], new IndexerSearchOutcome(1, IndexerSearchStatus.Deferred, [], RequestsSent: 0, deferral));

            var result = await SearchAndDownloadAsync(book);

            Assert.False(result.Success);
            Assert.Equal(deferral, result.Deferral);
        }

        [Fact]
        public async Task Fallback_KeepsTheSearchOrigin()
        {
            var book = await SeedAsync(Title, Author);
            ReturnsFull(FullQuery, SearchOrigin.Retry, Completed(1));
            ReturnsFallback(Title, [1], SearchOrigin.Retry, Completed(1, Release(Title + " [M4B]")));

            var result = await SearchAndDownloadAsync(book, SearchOrigin.Retry);

            Assert.True(result.Success, result.Message);
            VerifyFallback(Title, Times.Once());
        }

        // There is no catch-and-retry in DownloadService: an exception from the first search
        // propagates. This does not show that a real search cancels (the manual path passes no token).
        [Fact]
        public async Task FirstSearchThrows_PropagatesWithoutRetry()
        {
            var book = await SeedAsync(Title, Author);
            _searchService
                .Setup(s => s.SearchIndexersDetailedAsync(FullQuery, It.IsAny<IndexerSearchContext>(), null, true, null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());

            await Assert.ThrowsAsync<OperationCanceledException>(() => SearchAndDownloadAsync(book));

            VerifyFull(AnyQuery, Times.Once());
            VerifyFallback(AnyQuery, Times.Never());
        }

        private void ReturnsFull(string query, params IndexerSearchOutcome[] outcomes) =>
            ReturnsFull(query, SearchOrigin.Interactive, outcomes);

        private void ReturnsFull(string query, SearchOrigin origin, params IndexerSearchOutcome[] outcomes) =>
            _searchService
                .Setup(s => s.SearchIndexersDetailedAsync(
                    query,
                    It.Is<IndexerSearchContext>(c => c.Origin == origin && c.CycleAllowance == null && c.QueryKind == IndexerRequestKind.Search),
                    null,
                    true,
                    null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IndexerSearchReport(outcomes));

        private void ReturnsFallback(string query, int[] indexerIds, params IndexerSearchOutcome[] outcomes) =>
            ReturnsFallback(query, indexerIds, SearchOrigin.Interactive, outcomes);

        private void ReturnsFallback(string query, int[] indexerIds, SearchOrigin origin, params IndexerSearchOutcome[] outcomes) =>
            _searchService
                .Setup(s => s.SearchIndexersDetailedAsync(
                    query,
                    It.Is<IndexerSearchContext>(c => c.Origin == origin && c.CycleAllowance == null && c.QueryKind == IndexerRequestKind.FallbackQuery),
                    It.Is<IReadOnlyCollection<int>?>(ids => ids != null && ids.Order().SequenceEqual(indexerIds)),
                    true,
                    null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IndexerSearchReport(outcomes));

        // Verifies the searches of one kind, for one query or (with AnyQuery) for any query.
        private void VerifyFull(string? query, Times times) =>
            _searchService.Verify(s => s.SearchIndexersDetailedAsync(
                It.Is<string>(sent => query == AnyQuery || sent == query),
                It.Is<IndexerSearchContext>(c => c.QueryKind == IndexerRequestKind.Search),
                It.IsAny<IReadOnlyCollection<int>?>(),
                It.IsAny<bool>(),
                It.IsAny<SearchRequest?>(),
                It.IsAny<CancellationToken>()), times);

        private void VerifyFallback(string? query, Times times) =>
            _searchService.Verify(s => s.SearchIndexersDetailedAsync(
                It.Is<string>(sent => query == AnyQuery || sent == query),
                It.Is<IndexerSearchContext>(c => c.QueryKind == IndexerRequestKind.FallbackQuery),
                It.IsAny<IReadOnlyCollection<int>?>(),
                It.IsAny<bool>(),
                It.IsAny<SearchRequest?>(),
                It.IsAny<CancellationToken>()), times);

        private Task<SearchAndDownloadResult> SearchAndDownloadAsync(Audiobook book, SearchOrigin origin = SearchOrigin.Interactive) =>
            _provider.GetRequiredService<DownloadService>().SearchAndDownloadAsync(book.Id, origin);

        private async Task<Audiobook> SeedAsync(string title, params string[] authors)
        {
            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithOutputPath(Path.GetTempPath())
                .Build());
            await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfiguration
            {
                Id = "qb-1",
                Name = "local qbit",
                Type = "qbittorrent",
                Host = "localhost",
                Port = 8080,
                IsEnabled = true
            });
            var profile = await _qualityProfileRepository.AddAsync(
                new QualityProfileBuilder().WithName("Structured").WithStructuredDefaults().Build());
            var builder = new AudiobookBuilder()
                .WithBasePath(FileService.GetTempPath())
                .WithTitle(title);
            foreach (var author in authors) builder.WithAuthor(author);
            var book = builder.Build();
            book.QualityProfileId = profile.Id;
            book.QualityProfile = null;
            book.Monitored = true;
            return await _audiobookRepository.AddAsync(book);
        }

        private static IndexerSearchOutcome Completed(int indexerId, params IndexerSearchResult[] results) =>
            new(indexerId, IndexerSearchStatus.Completed, results, RequestsSent: 1);

        private static RequestDeferral Deferral(RequestDeferralReason reason) =>
            new(new DateTime(2026, 3, 2, 15, 0, 0, DateTimeKind.Utc), reason);

        private static IndexerSearchResult Release(string title, string artist = Author) => new()
        {
            Title = title,
            Artist = artist,
            IdentityEvidence = new CandidateIdentityEvidence
            {
                ReleaseName = title,
                Authors = [new ContributorEvidence(artist, IdentityEvidenceOrigin.ProviderSupplied)]
            },
            DownloadType = "Torrent",
            Format = "M4B",
            Seeders = 50,
            MagnetLink = "magnet:?xt=urn:btih:ABCDEF1234567890ABCDEF1234567890ABCDEF12",
            Size = 400L * 1024 * 1024
        };
    }
}
