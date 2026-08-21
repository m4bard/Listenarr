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
using System.Net;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Search.Indexers
{
    /// <summary>
    /// A single slow indexer must not fail the whole multi-indexer search.
    /// HttpClient reports its own configured Timeout as a TaskCanceledException, which is an
    /// OperationCanceledException, so a guard written as "ex is not OperationCanceledException"
    /// lets an indexer timeout escape every layer that was built to absorb a bad indexer.
    /// </summary>
    [Trait("Name", "IndexerSearchTimeoutTests")]
    [Trait("Category", "Application")]
    public class IndexerSearchTimeoutTests : BaseTests
    {
        private const string ResponsiveIndexerHost = "responsive.indexer.test";
        private const string UnresponsiveIndexerHost = "unresponsive.indexer.test";

        private static readonly TimeSpan ClientTimeout = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan LongerThanClientTimeout = TimeSpan.FromSeconds(30);

        private const string TorznabFeed = @"<?xml version=""1.0""?>
<rss>
  <channel>
    <item>
      <title>Responsive Indexer Result</title>
      <guid>responsive-1</guid>
      <pubDate>Mon, 01 Jan 2025 00:00:00 +0000</pubDate>
      <enclosure url=""https://responsive.indexer.test/one.torrent"" length=""123456"" />
    </item>
  </channel>
</rss>";

        [Fact]
        public async Task SearchIndexersAsync_ReturnsResponsiveIndexerResults_WhenAnotherIndexerExceedsHttpClientTimeout()
        {
            using var httpClient = new HttpClient(new PerHostHandler())
            {
                Timeout = ClientTimeout
            };
            var workflow = CreateWorkflow(
                httpClient,
                new TorznabNewznabSearchProvider(httpClient, NullLogger<TorznabNewznabSearchProvider>.Instance),
                Indexer(UnresponsiveIndexerHost, "Torznab"),
                Indexer(ResponsiveIndexerHost, "Torznab"));

            var results = await workflow.SearchIndexersAsync("some audiobook");

            Assert.Single(results);
            Assert.Equal("Responsive Indexer Result", results[0].Title);
        }

        [Fact]
        public async Task SearchIndexersAsync_PropagatesGenuineCallerCancellation()
        {
            var cancellation = await CaptureCallerCancellationAsync();
            using var httpClient = new HttpClient(new PerHostHandler());
            var workflow = CreateWorkflow(
                httpClient,
                new ThrowingSearchProvider(cancellation),
                Indexer(UnresponsiveIndexerHost, ThrowingSearchProvider.Implementation));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => workflow.SearchIndexersAsync("some audiobook"));
        }

        [Fact]
        public async Task IsNonFatal_SeparatesHttpClientTimeoutFromCallerCancellation()
        {
            var timeout = await CaptureHttpClientTimeoutAsync();
            var cancellation = await CaptureCallerCancellationAsync();

            // Both are OperationCanceledException, so the exception type alone cannot separate them.
            Assert.IsAssignableFrom<OperationCanceledException>(timeout);
            Assert.IsAssignableFrom<OperationCanceledException>(cancellation);

            Assert.True(WorkerExceptionClassifier.IsNonFatal(timeout));
            Assert.False(WorkerExceptionClassifier.IsNonFatal(cancellation));
            Assert.False(WorkerExceptionClassifier.IsNonFatal(new OutOfMemoryException()));
            Assert.True(WorkerExceptionClassifier.IsNonFatal(new HttpRequestException("refused")));
        }

        /// <summary>
        /// Produces the real exception the runtime raises when HttpClient.Timeout elapses,
        /// rather than a hand-built stand-in.
        /// </summary>
        private static async Task<Exception> CaptureHttpClientTimeoutAsync()
        {
            using var httpClient = new HttpClient(new PerHostHandler()) { Timeout = ClientTimeout };
            var caught = await Record.ExceptionAsync(
                () => httpClient.GetAsync($"https://{UnresponsiveIndexerHost}/api"));
            Assert.NotNull(caught);
            return caught;
        }

        /// <summary>
        /// Produces the real exception the runtime raises when the caller's own token is cancelled.
        /// </summary>
        private static async Task<Exception> CaptureCallerCancellationAsync()
        {
            using var httpClient = new HttpClient(new PerHostHandler());
            using var cancellationSource = new CancellationTokenSource(ClientTimeout);
            var caught = await Record.ExceptionAsync(
                () => httpClient.GetAsync($"https://{UnresponsiveIndexerHost}/api", cancellationSource.Token));
            Assert.NotNull(caught);
            return caught;
        }

        private IndexerSearchWorkflow CreateWorkflow(
            HttpClient httpClient,
            IIndexerSearchProvider provider,
            params Indexer[] indexers)
        {
            var indexerRepository = new Mock<IIndexerRepository>();
            indexerRepository
                .Setup(repository => repository.GetEnabledAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(indexers.ToList());

            return new IndexerSearchWorkflow(
                httpClient,
                Mock.Of<IConfigurationService>(),
                indexerRepository.Object,
                new[] { provider },
                new IndexerAdditionalSettingsParser(NullLogger<IndexerAdditionalSettingsParser>.Instance),
                NullLogger<IndexerSearchWorkflow>.Instance);
        }

        private static Indexer Indexer(string host, string implementation) => new()
        {
            Name = host,
            Url = $"https://{host}",
            Type = "Torrent",
            Implementation = implementation,
            IsEnabled = true
        };

        /// <summary>
        /// Answers immediately for the responsive host and never answers for the other one,
        /// so HttpClient's own timeout is what ends the request.
        /// </summary>
        private sealed class PerHostHandler : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                if (request.RequestUri?.Host == ResponsiveIndexerHost)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(TorznabFeed)
                    };
                }

                await Task.Delay(LongerThanClientTimeout, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        }

        private sealed class ThrowingSearchProvider : IIndexerSearchProvider
        {
            public const string Implementation = "AlwaysCancels";

            private readonly Exception _exception;

            public ThrowingSearchProvider(Exception exception) => _exception = exception;

            public string IndexerType => Implementation;

            public Task<List<IndexerSearchResult>> SearchAsync(
                Indexer indexer,
                string query,
                string? category = null,
                SearchRequest? request = null) => throw _exception;
        }
    }
}
