/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Net;
using Listenarr.Infrastructure.DependencyInjection.DownloadClients;
using Listenarr.Infrastructure.DependencyInjection.Platform;
using Listenarr.Infrastructure.Search.RequestGate;
using Listenarr.Tests.Common;
using Listenarr.Tests.Features.Infrastructure.Search.RequestGate;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Downloads.Submission;

/// <summary>
/// Runs the NZB downloader as the application composes it, against a real loopback server, so
/// every wire request is counted independently of the gate and no HTTP client policy can add one.
/// </summary>
[Trait("Name", "NzbFileDownloaderGateTests")]
[Trait("Category", "NzbFileDownloader")]
public sealed class NzbFileDownloaderGateTests : BaseTests
{
    private const int IndexerId = 7;

    [Fact]
    public async Task NzbFetch_NoHiddenRetries()
    {
        // Given: an indexer that answers every request with a 503, behind the real gate
        await using var server = new LoopbackHttpServer(context => context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable);
        var store = new IndexerRequestGateHarness.RecordingStateStore();
        var gate = new IndexerRequestGate(
            new IndexerRequestStateCache(store, NullLogger<IndexerRequestStateCache>.Instance),
            [],
            TimeProvider.System,
            NullLogger<IndexerRequestGate>.Instance);

        // When
        var failure = await Record.ExceptionAsync(() => DownloadAsync(server.BaseUri + "get.nzb", IndexerId, gate));

        // Then: the failed fetch is one wire request, and the gate recorded it against the indexer
        Assert.Single(server.RequestPaths);
        Assert.Contains("503", Assert.IsType<DownloadClientSubmissionException>(failure).Message);
        Assert.Equal(1, Assert.IsType<IndexerRequestState>(store.Row(IndexerId)).ConsecutiveFailures);
    }

    [Fact]
    public async Task NzbFetch_EveryHopIsADownloadFetchGateCall()
    {
        // Given: an indexer whose NZB link answers 302 to a second path, which answers 200
        await using var server = new LoopbackHttpServer(context =>
        {
            if (context.Request.Url!.AbsolutePath == "/get.nzb")
            {
                context.Response.StatusCode = (int)HttpStatusCode.Redirect;
                context.Response.RedirectLocation = "/moved.nzb";
                return;
            }

            context.Response.OutputStream.Write([1, 2, 3], 0, 3);
        });
        var gate = new RecordingGate();

        // When
        var bytes = await DownloadAsync(server.BaseUri + "get.nzb", IndexerId, gate);

        // Then: both wire requests are gate calls for the candidate's indexer, and neither is a redirect kind
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
        Assert.Equal(new[] { "/get.nzb", "/moved.nzb" }, server.RequestPaths);
        Assert.Equal(2, gate.Contexts.Count);
        Assert.All(gate.Contexts, context => Assert.Equal(IndexerRequestKind.DownloadFetch, context.Kind));
        Assert.All(gate.Contexts, context => Assert.Equal(IndexerId, context.IndexerId));
    }

    [Fact]
    public async Task NzbFetch_WithoutAnIndexer_IsUnattributed()
    {
        // Given: a candidate with no indexer
        await using var server = new LoopbackHttpServer(context => context.Response.OutputStream.Write([1], 0, 1));
        var gate = new RecordingGate();

        // When
        await DownloadAsync(server.BaseUri + "get.nzb", indexerId: null, gate);

        // Then: the single gate call carries indexer id 0, which the gate treats as unattributed
        Assert.Equal(0, Assert.Single(gate.Contexts).IndexerId);
    }

    [Fact]
    public async Task NzbFetch_GateDeferral_ThrowsDeferredAndSendsNothing()
    {
        // Given: a gate that defers the request
        await using var server = new LoopbackHttpServer(context => context.Response.OutputStream.Write([1], 0, 1));
        var deferral = new RequestDeferral(new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc), RequestDeferralReason.RateLimited);
        var gate = new RecordingGate(deferral);

        // When
        var thrown = await Assert.ThrowsAsync<IndexerRequestDeferredException>(
            () => DownloadAsync(server.BaseUri + "get.nzb", IndexerId, gate));

        // Then
        Assert.Equal(deferral, thrown.Deferral);
        Assert.Empty(server.RequestPaths);
    }

    private static async Task<byte[]> DownloadAsync(string url, int? indexerId, IIndexerRequestGate gate)
    {
        // The production HTTP clients, with the gate supplied by the test.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddPlatformHttpClients(new ConfigurationBuilder().Build());
        services.AddDownloadClientHttpClients();
        services.AddSingleton(Mock.Of<IIndexerRepository>());
        services.AddSingleton(gate);
        services.AddScoped<INzbFileDownloader, NzbFileDownloader>();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<INzbFileDownloader>()
            .DownloadAsync(url, indexerId, CancellationToken.None);
    }

    // Records every request context and then dispatches, or defers every request when given a deferral.
    private sealed class RecordingGate(RequestDeferral? deferral = null) : IIndexerRequestGate
    {
        public List<IndexerRequestContext> Contexts { get; } = new();

        public async Task<IndexerRequestResult> SendAsync(
            IndexerRequestContext context,
            Func<CancellationToken, Task<HttpResponseMessage>> dispatch,
            CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            return deferral is { } deferred
                ? new IndexerRequestResult.Deferred(deferred)
                : new IndexerRequestResult.Sent(await dispatch(cancellationToken));
        }

        public Task<RequestDeferral?> GetDeferralAsync(int indexerId, SearchOrigin origin, CancellationToken cancellationToken)
            => Task.FromResult<RequestDeferral?>(null);
    }
}
