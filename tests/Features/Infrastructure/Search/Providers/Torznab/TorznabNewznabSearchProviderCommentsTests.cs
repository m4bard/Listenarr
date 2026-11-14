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
using System.Text;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Search.Providers.Torznab;

[Trait("Name", "TorznabNewznabSearchProviderCommentsTests")]
[Trait("Category", "TorznabNewznabSearchProvider")]
public sealed class TorznabNewznabSearchProviderCommentsTests : BaseTests
{
    private static readonly string[] Titles =
    [
        "A Princess of Mars",
        "Twenty Thousand Leagues Under the Sea",
        "The Wonderful Wizard of Oz"
    ];

    [Fact]
    public async Task CommentsPages_BeyondTheAllowance_AreDeferredWithEveryResultKept()
    {
        // Given: a feed of three zero-grab results and an allowance of two requests
        var server = new StubServer(FeedWithThreeZeroGrabResults());
        var allowance = new CycleRequestAllowance(2);
        var provider = CreateProvider(server, new RecordingGate());

        // When
        var outcome = await provider.SearchAsync(
            CreateIndexer(), "query", null, null, new IndexerSearchContext(SearchOrigin.Sweep, allowance), CancellationToken.None);

        // Then: the search and one comments page were sent, the second comments page was held back
        Assert.Equal(2, server.RequestCount);
        Assert.Equal(1, server.CommentsRequestCount);
        Assert.Equal(IndexerSearchStatus.Deferred, outcome.Status);
        Assert.Equal(RequestDeferralReason.CycleRequestBound, outcome.Deferral!.Value.Reason);
        Assert.Equal(Titles, outcome.Results.Select(result => result.Title));
        Assert.Equal(2, outcome.RequestsSent);
        Assert.Equal(2, allowance.Taken);
        Assert.Equal([7, 0, 0], outcome.Results.Select(result => result.Grabs));
    }

    [Fact]
    public async Task CommentsPages_AreEnrichmentRequestsWithTheCallersTokenAndAllowance()
    {
        // Given: an allowance that covers the search and all three comments pages
        var server = new StubServer(FeedWithThreeZeroGrabResults());
        var gate = new RecordingGate();
        var allowance = new CycleRequestAllowance(4);
        var provider = CreateProvider(server, gate);
        using var cancellation = new CancellationTokenSource();

        // When
        var outcome = await provider.SearchAsync(
            CreateIndexer(), "query", null, null, new IndexerSearchContext(SearchOrigin.Sweep, allowance), cancellation.Token);

        // Then: the search is a Search request and each comments page is an Enrichment request, all through the gate
        Assert.Equal(
            [
                new IndexerRequestContext(11, SearchOrigin.Sweep, IndexerRequestKind.Search, allowance),
                new IndexerRequestContext(11, SearchOrigin.Sweep, IndexerRequestKind.Enrichment, allowance),
                new IndexerRequestContext(11, SearchOrigin.Sweep, IndexerRequestKind.Enrichment, allowance),
                new IndexerRequestContext(11, SearchOrigin.Sweep, IndexerRequestKind.Enrichment, allowance)
            ],
            gate.Contexts);
        Assert.All(gate.Tokens, token => Assert.Equal(cancellation.Token, token));
        Assert.Equal(4, server.RequestCount);
        Assert.Equal(IndexerSearchStatus.Completed, outcome.Status);
        Assert.Equal(4, outcome.RequestsSent);
        Assert.Equal([7, 7, 7], outcome.Results.Select(result => result.Grabs));
    }

    [Theory]
    [InlineData("<rss><channel><item><title>A Princess of Mars")]
    [InlineData("not xml at all")]
    [InlineData("<error code=\"100\" description=\"Incorrect credentials\" />")]
    public async Task UnreadableFeed_IsFailedAsUnparseable(string body)
    {
        // Given: an answer that is not a feed
        var server = new StubServer(body);
        var provider = CreateProvider(server, new RecordingGate());

        // When
        var outcome = await provider.SearchAsync(
            CreateIndexer(), "query", null, null, new IndexerSearchContext(SearchOrigin.Interactive, CycleAllowance: null), CancellationToken.None);

        // Then: it is a failure, not a completed search with no results
        Assert.Equal(IndexerSearchStatus.Failed, outcome.Status);
        Assert.Equal("unparseable response", outcome.FailureReason);
        Assert.Empty(outcome.Results);
        Assert.Equal(1, outcome.RequestsSent);
    }

    private static string FeedWithThreeZeroGrabResults()
    {
        var items = new StringBuilder();
        for (var index = 0; index < Titles.Length; index++)
        {
            items.Append($"""
                <item>
                  <title>{Titles[index]}</title>
                  <guid>guid-{index}</guid>
                  <comments>https://althub.example/details/{index}</comments>
                  <enclosure url="https://althub.example/download/{index}.torrent" length="1234" />
                </item>
                """);
        }

        return $"<rss><channel>{items}</channel></rss>";
    }

    private static TorznabNewznabSearchProvider CreateProvider(StubServer server, IIndexerRequestGate gate)
    {
        return new TorznabNewznabSearchProvider(
            new HttpClient(new DelegatingHandlerMock(server.HandleAsync)),
            gate,
            NullLogger<TorznabNewznabSearchProvider>.Instance);
    }

    private static Indexer CreateIndexer()
    {
        return new IndexerBuilder()
            .WithId(11)
            .WithName("Torznab")
            .WithImplementation("Torznab")
            .WithUrl("https://althub.example")
            .Build();
    }

    // Answers the search with the feed and every comments page with a page that shows seven comments.
    private sealed class StubServer
    {
        private readonly string _feed;
        private int _requestCount;
        private int _commentsRequestCount;

        public StubServer(string feed)
        {
            _feed = feed;
        }

        public int RequestCount => Volatile.Read(ref _requestCount);

        public int CommentsRequestCount => Volatile.Read(ref _commentsRequestCount);

        public Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            if (request.RequestUri!.AbsolutePath.StartsWith("/details/", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _commentsRequestCount);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html><body><p>7 comments</p></body></html>")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_feed) });
        }
    }

    // Records each request's context and token, then lets the pass-through gate apply the allowance.
    private sealed class RecordingGate : IIndexerRequestGate
    {
        private readonly PassThroughIndexerRequestGate _inner = new(TimeProvider.System);

        public List<IndexerRequestContext> Contexts { get; } = new();

        public List<CancellationToken> Tokens { get; } = new();

        public Task<IndexerRequestResult> SendAsync(
            IndexerRequestContext context,
            Func<CancellationToken, Task<HttpResponseMessage>> dispatch,
            CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            Tokens.Add(cancellationToken);
            return _inner.SendAsync(context, dispatch, cancellationToken);
        }

        public Task<RequestDeferral?> GetDeferralAsync(int indexerId, SearchOrigin origin, CancellationToken cancellationToken)
        {
            return Task.FromResult<RequestDeferral?>(null);
        }
    }
}
