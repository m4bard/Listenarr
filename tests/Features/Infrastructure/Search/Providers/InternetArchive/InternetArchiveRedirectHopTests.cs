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
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Search.Providers.InternetArchive;

[Trait("Name", "InternetArchiveRedirectHopTests")]
[Trait("Category", "InternetArchiveSearchProvider")]
public sealed class InternetArchiveRedirectHopTests : BaseTests
{
    private const string SearchResponse = """
{"response": {"docs": [{"identifier": "princess_of_mars", "title": "A Princess of Mars", "creator": "Edgar Rice Burroughs", "date": "2012-06-23"}]}}
""";

    private const string AudioMetadata = """
{
  "metadata": { "language": "English" },
  "files": [
    { "name": "princess_of_mars.m4b", "format": "LibriVox Apple Audiobook", "size": "900" }
  ]
}
""";

    [Fact]
    public async Task RedirectHops_FallbackQuery_EachHopIsAGateCallWithItsKind()
    {
        // Given: a fallback-query search where every search and metadata request is redirected once
        var wirePaths = new List<string>();
        var gate = new RecordingGate();
        var provider = CreateProvider(gate, wirePaths);
        var context = new IndexerSearchContext(SearchOrigin.Sweep, new CycleRequestAllowance(50), IndexerRequestKind.FallbackQuery);

        // When
        var outcome = await provider.SearchAsync(CreateIndexer(), "A Princess of Mars", null, null, context, CancellationToken.None);

        // Then: one gate call per wire request; query sends carry the fallback kind, metadata sends
        // stay enrichment, and every followed hop is a redirect
        Assert.Equal(IndexerSearchStatus.Completed, outcome.Status);
        Assert.NotEmpty(outcome.Results);
        Assert.Equal(wirePaths.Count, gate.Contexts.Count);
        Assert.Equal(wirePaths.Count, outcome.RequestsSent);
        var sends = wirePaths.Zip(gate.Contexts, (path, sent) => (Path: path, sent.Kind)).ToList();
        Assert.Contains(sends, send => send.Path == "/metadata/princess_of_mars");
        Assert.All(sends, send => Assert.Equal(ExpectedKind(send.Path), send.Kind));
    }

    private static IndexerRequestKind ExpectedKind(string path) => path switch
    {
        "/advancedsearch.php" => IndexerRequestKind.FallbackQuery,
        "/metadata/princess_of_mars" => IndexerRequestKind.Enrichment,
        _ => IndexerRequestKind.Redirect
    };

    private static InternetArchiveSearchProvider CreateProvider(IIndexerRequestGate gate, List<string> wirePaths)
    {
        var handler = new DelegatingHandlerMock((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            wirePaths.Add(path);
            return Task.FromResult(path switch
            {
                "/advancedsearch.php" => Redirect("/search-moved.php"),
                "/search-moved.php" => Ok(SearchResponse),
                "/metadata/princess_of_mars" => Redirect("/metadata-moved/princess_of_mars"),
                _ => Ok(AudioMetadata)
            });
        });

        var configurationService = new Mock<IConfigurationService>();
        configurationService
            .Setup(service => service.GetApplicationSettingsAsync())
            .ReturnsAsync(new ApplicationSettingsBuilder().WithExtractArchive().Build());

        return new InternetArchiveSearchProvider(
            new HttpClient(handler),
            gate,
            configurationService.Object,
            NullLogger<InternetArchiveSearchProvider>.Instance);
    }

    private static Indexer CreateIndexer()
    {
        return new IndexerBuilder()
            .WithId(42)
            .WithName("Internet Archive")
            .WithType("DirectDownload")
            .WithImplementation("InternetArchive")
            .WithUrl("https://archive.org")
            .Build();
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new Uri(location, UriKind.Relative);
        return response;
    }

    private static HttpResponseMessage Ok(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content)
    };

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
        {
            return Task.FromResult<RequestDeferral?>(null);
        }
    }
}
