using System.Net;
using Listenarr.Application.Search.Indexers.Common;
using Listenarr.Infrastructure.Torrents;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Torrents
{
    [Trait("Name", "TorrentFileDownloaderTests")]
    [Trait("Category", "TorrentFileDownloader")]
    public class TorrentFileDownloaderTests
    {
        [Fact]
        public async Task DownloadAsync_WhenTransientFailuresRecover_ReturnsTorrentBytes()
        {
            var calls = 0;
            var delays = new List<TimeSpan>();
            var torrentBytes = await File.ReadAllBytesAsync(GetTorrentDataPath());
            var downloader = CreateDownloader(
                (_, _) =>
                {
                    calls++;
                    return Task.FromResult(calls < 3
                        ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                        : new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new ByteArrayContent(torrentBytes)
                        });
                },
                (delay, _) =>
                {
                    delays.Add(delay);
                    return Task.CompletedTask;
                });

            var result = await downloader.DownloadAsync("https://indexer.example.com/book.torrent", indexerId: null);

            Assert.True(result.HasBytes);
            Assert.Equal(torrentBytes, result.TorrentBytes);
            Assert.Equal(3, calls);
            Assert.Equal(2, delays.Count);
        }

        [Fact]
        public async Task DownloadAsync_WhenRetriesAreExhausted_ReturnsSanitizedFailure()
        {
            var calls = 0;
            var downloader = CreateDownloader(
                (_, _) =>
                {
                    calls++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                });

            var result = await downloader.DownloadAsync("https://indexer.example.com/book.torrent?apikey=secret", indexerId: null);

            Assert.True(result.IsEmpty);
            Assert.Equal(3, calls);
            Assert.Equal("Torrent metadata download failed with HTTP 500.", result.FailureReason);
            Assert.DoesNotContain("secret", result.FailureReason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task DownloadAsync_WhenPayloadIsHtml_ReturnsFailure()
        {
            var downloader = CreateDownloader(
                (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html>not a torrent</html>")
                }));

            var result = await downloader.DownloadAsync("https://indexer.example.com/book.torrent", indexerId: null);

            Assert.True(result.IsEmpty);
            Assert.Contains("non-torrent", result.FailureReason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task DownloadAsync_WhenResponseRedirects_ValidatesAndFollowsDestination()
        {
            var requestedUrls = new List<string>();
            var torrentBytes = await File.ReadAllBytesAsync(GetTorrentDataPath());
            var downloader = CreateDownloader(
                (request, _) =>
                {
                    requestedUrls.Add(request.RequestUri!.ToString());
                    if (requestedUrls.Count == 1)
                    {
                        var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                        redirect.Headers.Location = new Uri("/resolved/book.torrent", UriKind.Relative);
                        return Task.FromResult(redirect);
                    }

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(torrentBytes)
                    });
                });

            var result = await downloader.DownloadAsync("https://indexer.example.com/download?id=secret", indexerId: null);

            Assert.True(result.HasBytes);
            Assert.Equal(
                ["https://indexer.example.com/download?id=secret", "https://indexer.example.com/resolved/book.torrent"],
                requestedUrls);
        }

        [Fact]
        public async Task DownloadAsync_WhenCallerCancels_PropagatesCancellation()
        {
            var downloader = CreateDownloader(
                async (_, cancellationToken) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                });
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => downloader.DownloadAsync("https://indexer.example.com/book.torrent", indexerId: null, cancellation.Token));
        }

        private static TorrentFileDownloader CreateDownloader(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(IndexerHttpDispatch.ClientName))
                .Returns(() => new HttpClient(new DelegatingHandlerMock(handler)));
            return new TorrentFileDownloader(
                NullLogger<TorrentFileDownloader>.Instance,
                factory.Object,
                new PassThroughGate(),
                delay ?? ((_, _) => Task.CompletedTask));
        }

        private sealed class PassThroughGate : IIndexerRequestGate
        {
            public async Task<IndexerRequestResult> SendAsync(
                IndexerRequestContext context,
                Func<CancellationToken, Task<HttpResponseMessage>> dispatch,
                CancellationToken cancellationToken)
                => new IndexerRequestResult.Sent(await dispatch(cancellationToken));

            public Task<RequestDeferral?> GetDeferralAsync(int indexerId, SearchOrigin origin, CancellationToken cancellationToken)
                => Task.FromResult<RequestDeferral?>(null);
        }

        private static string GetTorrentDataPath() =>
            TestUtils.GetTorrentDataPath("big-buck-bunny.torrent");
    }
}
