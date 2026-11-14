using System.Net;
using Listenarr.Infrastructure.DependencyInjection.Platform;
using Listenarr.Infrastructure.Torrents;
using Listenarr.Tests.Features.Infrastructure.Search.RequestGate;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestGate.IndexerRequestGateHarness;

namespace Listenarr.Tests.Features.Infrastructure.Torrents;

/// <summary>
/// Runs the torrent downloader on the production Indexer client against a real loopback server, so
/// wire requests are counted independently of the code under test.
/// </summary>
[Trait("Name", "TorrentFetchGateTests")]
[Trait("Category", "TorrentFileDownloader")]
public sealed class TorrentFetchGateTests : BaseTests
{
    private const int IndexerId = 7;
    private static readonly byte[] TorrentBytes = "d4:spam4:eggse"u8.ToArray();

    [Fact]
    public async Task TorrentFetch_RetryAfterIsDeferral()
    {
        // Given: an indexer that answers 429 with a two-minute Retry-After, behind the real gate
        await using var server = new LoopbackHttpServer(context =>
        {
            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
            context.Response.Headers["Retry-After"] = "120";
        });
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            (_, _) => throw new InvalidOperationException("The harness client is not used."));
        var delays = new List<TimeSpan>();
        using var services = BuildServices();
        var downloader = CreateDownloader(services, harness.Gate, delays);

        // When
        var failure = await Assert.ThrowsAsync<IndexerRequestDeferredException>(
            () => downloader.DownloadAsync(new Uri(server.BaseUri, "book.torrent").ToString(), IndexerId));

        // Then: one wire request, no sleep, and the deferral carries the indexer's Retry-After
        Assert.Single(server.RequestPaths);
        Assert.Empty(delays);
        Assert.Equal(RequestDeferralReason.RateLimited, failure.Deferral.Reason);
        Assert.Equal(Start + TimeSpan.FromSeconds(120), failure.Deferral.UntilUtc);
    }

    [Fact]
    public async Task TorrentFetch_HopsAndRetries_AreEachOneDownloadFetchGateCall()
    {
        // Given: a download that redirects twice and then fails once with a 503 before it succeeds
        var finalHits = 0;
        await using var server = new LoopbackHttpServer(context =>
        {
            switch (context.Request.Url!.AbsolutePath)
            {
                case "/start":
                    Redirect(context, "/mid");
                    break;
                case "/mid":
                    Redirect(context, "/final");
                    break;
                default:
                    if (++finalHits == 1)
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                        break;
                    }

                    context.Response.OutputStream.Write(TorrentBytes, 0, TorrentBytes.Length);
                    break;
            }
        });
        var gate = new RecordingGate();
        using var services = BuildServices();
        var downloader = CreateDownloader(services, gate, new List<TimeSpan>());

        // When
        var result = await downloader.DownloadAsync(new Uri(server.BaseUri, "start").ToString(), IndexerId);

        // Then: each of the four wire requests is its own gate call, and every one is a grab-path fetch
        Assert.Equal(TorrentBytes, result.TorrentBytes);
        Assert.Equal(new[] { "/start", "/mid", "/final", "/final" }, server.RequestPaths);
        Assert.Equal(4, gate.Contexts.Count);
        Assert.All(gate.Contexts, context =>
        {
            Assert.Equal(IndexerRequestKind.DownloadFetch, context.Kind);
            Assert.Equal(IndexerId, context.IndexerId);
        });
    }

    [Fact]
    public async Task TorrentFetch_WithoutIndexer_IsUnattributed()
    {
        // Given: a download with no indexer id
        await using var server = new LoopbackHttpServer(context =>
            context.Response.OutputStream.Write(TorrentBytes, 0, TorrentBytes.Length));
        var gate = new RecordingGate();
        using var services = BuildServices();
        var downloader = CreateDownloader(services, gate, new List<TimeSpan>());

        // When
        await downloader.DownloadAsync(new Uri(server.BaseUri, "book.torrent").ToString(), indexerId: null);

        // Then: the gate sees an unattributed request
        Assert.Equal(0, Assert.Single(gate.Contexts).IndexerId);
    }

    private static TorrentFileDownloader CreateDownloader(
        ServiceProvider services,
        IIndexerRequestGate gate,
        List<TimeSpan> delays)
        => new(
            NullLogger<TorrentFileDownloader>.Instance,
            services.GetRequiredService<IHttpClientFactory>(),
            gate,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

    // The production HTTP client registrations.
    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddPlatformHttpClients(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static void Redirect(HttpListenerContext context, string location)
    {
        context.Response.StatusCode = (int)HttpStatusCode.Redirect;
        context.Response.RedirectLocation = location;
    }

    // Records every request context and then dispatches, as the pass-through gate does.
    private sealed class RecordingGate : IIndexerRequestGate
    {
        public List<IndexerRequestContext> Contexts { get; } = new();

        public async Task<IndexerRequestResult> SendAsync(
            IndexerRequestContext context,
            Func<CancellationToken, Task<HttpResponseMessage>> dispatch,
            CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            return new IndexerRequestResult.Sent(await dispatch(cancellationToken));
        }

        public Task<RequestDeferral?> GetDeferralAsync(int indexerId, SearchOrigin origin, CancellationToken cancellationToken)
            => Task.FromResult<RequestDeferral?>(null);
    }
}
