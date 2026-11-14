/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Search.Providers.MyAnonamouse;

[Trait("Name", "MyAnonamouseSearchProviderGateTests")]
[Trait("Category", "MyAnonamouseSearchProvider")]
public sealed class MyAnonamouseSearchProviderGateTests : BaseTests
{
    private const string SearchPath = "/tor/js/loadSearchJSONbasic.php";
    private const string DetailPath = "/tor/js/loadTorrentJSONBasic.php";

    private const string TwoResults = """
[
  { "id": "701", "title": "A Princess of Mars", "size": "1234" },
  { "id": "702", "title": "The Wonderful Wizard of Oz", "size": "2345" }
]
""";

    [Fact]
    public async Task CycleRequestBound_CountsEachFollowedRedirectHop()
    {
        // Given: the search is answered after two followed redirects
        var server = new StubServer(redirects: 2, body: "[]");
        var gate = new RecordingGate();
        var allowance = new CycleRequestAllowance(10);
        var provider = CreateProvider(server, gate);

        // When
        var outcome = await provider.SearchAsync(
            CreateIndexer(), "query", null, null, new IndexerSearchContext(SearchOrigin.Sweep, allowance), CancellationToken.None);

        // Then: every request the server saw was one gate call
        Assert.Equal(3, server.RequestCount);
        Assert.Equal(3, gate.Contexts.Count);
        Assert.Equal(3, allowance.Taken);
        Assert.Equal(IndexerSearchStatus.Completed, outcome.Status);
        Assert.Equal(3, outcome.RequestsSent);
    }

    [Fact]
    public async Task CycleRequestBound_HopThatDoesNotFit_IsDeferredAndNeverSent()
    {
        // Given: an allowance of two for a search that needs three requests
        var server = new StubServer(redirects: 2, body: "[]");
        var allowance = new CycleRequestAllowance(2);
        var provider = CreateProvider(server, new RecordingGate());

        // When
        var outcome = await provider.SearchAsync(
            CreateIndexer(), "query", null, null, new IndexerSearchContext(SearchOrigin.Sweep, allowance), CancellationToken.None);

        // Then
        Assert.Equal(2, server.RequestCount);
        Assert.Equal(IndexerSearchStatus.Deferred, outcome.Status);
        Assert.Equal(RequestDeferralReason.CycleRequestBound, outcome.Deferral!.Value.Reason);
        Assert.Empty(outcome.Results);
        Assert.Equal(2, outcome.RequestsSent);
    }

    [Fact]
    public async Task Provider_UsesQueryKindForPrimarySend()
    {
        // Given: a fallback-query search from the sweep that is redirected once and enriches one result
        var server = new StubServer(redirects: 1, body: TwoResults);
        var gate = new RecordingGate();
        var allowance = new CycleRequestAllowance(10);
        var provider = CreateProvider(server, gate);
        var context = new IndexerSearchContext(SearchOrigin.Sweep, allowance, IndexerRequestKind.FallbackQuery);

        // When
        var outcome = await provider.SearchAsync(CreateIndexer(), "query", null, EnrichRequest(1), context, CancellationToken.None);

        // Then: the first hop carries the query kind, the followed hop is a redirect, the detail request enrichment
        Assert.Equal(
            [
                new IndexerRequestContext(9, SearchOrigin.Sweep, IndexerRequestKind.FallbackQuery, allowance),
                new IndexerRequestContext(9, SearchOrigin.Sweep, IndexerRequestKind.Redirect, allowance),
                new IndexerRequestContext(9, SearchOrigin.Sweep, IndexerRequestKind.Enrichment, allowance)
            ],
            gate.Contexts);
        Assert.Equal(3, server.RequestCount);
        Assert.Equal(IndexerSearchStatus.Completed, outcome.Status);
        Assert.Equal(3, outcome.RequestsSent);
    }

    [Fact]
    public async Task Provider_DefaultContext_SendsSearchAsSearch()
    {
        var gate = new RecordingGate();
        var provider = CreateProvider(new StubServer(redirects: 0, body: "[]"), gate);

        await provider.SearchAsync(
            CreateIndexer(), "query", null, null, new IndexerSearchContext(SearchOrigin.Interactive, CycleAllowance: null), CancellationToken.None);

        Assert.Equal(IndexerRequestKind.Search, Assert.Single(gate.Contexts).Kind);
    }

    [Fact]
    public async Task Deferral_AtDetailRequest_IsDeferredWithResultsGathered()
    {
        // Given: two results to enrich and an allowance that covers the search and one detail request
        var server = new StubServer(redirects: 0, body: TwoResults);
        var allowance = new CycleRequestAllowance(2);
        var provider = CreateProvider(server, new RecordingGate());

        // When
        var outcome = await provider.SearchAsync(
            CreateIndexer(), "query", null, EnrichRequest(2), new IndexerSearchContext(SearchOrigin.Sweep, allowance), CancellationToken.None);

        // Then: the search results are kept and the outcome says the search was held back
        Assert.Equal(IndexerSearchStatus.Deferred, outcome.Status);
        Assert.Equal(RequestDeferralReason.CycleRequestBound, outcome.Deferral!.Value.Reason);
        Assert.Equal(2, outcome.Results.Count);
        Assert.Equal(2, outcome.RequestsSent);
        Assert.Equal(2, server.RequestCount);
        Assert.Equal(1, server.DetailRequestCount);
    }

    [Fact]
    public async Task Deferral_AtFirstHop_IsDeferredWithoutARequest()
    {
        var server = new StubServer(redirects: 0, body: TwoResults);
        var provider = CreateProvider(server, new RecordingGate());

        var outcome = await provider.SearchAsync(
            CreateIndexer(), "query", null, null, new IndexerSearchContext(SearchOrigin.Sweep, new CycleRequestAllowance(0)), CancellationToken.None);

        Assert.Equal(IndexerSearchStatus.Deferred, outcome.Status);
        Assert.Empty(outcome.Results);
        Assert.Equal(0, outcome.RequestsSent);
        Assert.Equal(0, server.RequestCount);
    }

    private static MyAnonamouseSearchProvider CreateProvider(StubServer server, IIndexerRequestGate gate)
    {
        return new MyAnonamouseSearchProvider(
            NullLogger<MyAnonamouseSearchProvider>.Instance,
            new HttpClient(server.Handler) { BaseAddress = new Uri("https://indexer.example") },
            gate,
            Mock.Of<IIndexerRepository>());
    }

    private static Indexer CreateIndexer()
    {
        return new IndexerBuilder()
            .WithId(9)
            .WithName("MyAnonamouse")
            .WithType("Torrent")
            .WithImplementation("MyAnonamouse")
            .WithUrl("https://indexer.example")
            .WithSetting("mam_id", "test_mam")
            .Build();
    }

    private static SearchRequest EnrichRequest(int topResults)
    {
        return new SearchRequest
        {
            IncludeEnrichment = true,
            MyAnonamouse = new MyAnonamouseOptions { EnrichResults = true, EnrichTopResults = topResults }
        };
    }

    // Answers the search after a fixed number of redirects, and every detail request with a small document.
    // It never follows a redirect itself, so each counted request is one the provider sent.
    private sealed class StubServer
    {
        private int _requestCount;
        private int _detailRequestCount;

        public StubServer(int redirects, string body)
        {
            Handler = new DelegatingHandlerMock((request, _) =>
            {
                Interlocked.Increment(ref _requestCount);
                var path = request.RequestUri!.AbsolutePath;
                if (path == DetailPath)
                {
                    Interlocked.Increment(ref _detailRequestCount);
                    return Task.FromResult(Json("""{ "files": 3, "filetype": "m4b", "lang_code": "ENG" }"""));
                }

                var hop = path == SearchPath ? 0 : int.Parse(path["/hop/".Length..]);
                if (hop < redirects)
                {
                    var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                    redirect.Headers.Location = new Uri($"/hop/{hop + 1}", UriKind.Relative);
                    return Task.FromResult(redirect);
                }

                return Task.FromResult(Json(body));
            });
        }

        public DelegatingHandlerMock Handler { get; }

        public int RequestCount => Volatile.Read(ref _requestCount);

        public int DetailRequestCount => Volatile.Read(ref _detailRequestCount);

        private static HttpResponseMessage Json(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    // Records every request context, then applies the pass-through gate's allowance rule.
    private sealed class RecordingGate : IIndexerRequestGate
    {
        private readonly PassThroughIndexerRequestGate _inner = new(TimeProvider.System);
        private readonly ConcurrentQueue<IndexerRequestContext> _contexts = new();

        public IReadOnlyList<IndexerRequestContext> Contexts => _contexts.ToArray();

        public Task<IndexerRequestResult> SendAsync(
            IndexerRequestContext context,
            Func<CancellationToken, Task<HttpResponseMessage>> dispatch,
            CancellationToken cancellationToken)
        {
            _contexts.Enqueue(context);
            return _inner.SendAsync(context, dispatch, cancellationToken);
        }

        public Task<RequestDeferral?> GetDeferralAsync(int indexerId, SearchOrigin origin, CancellationToken cancellationToken)
        {
            return Task.FromResult<RequestDeferral?>(null);
        }
    }
}
