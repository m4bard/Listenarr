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
using Listenarr.Application.Search.Indexers.Torznab;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Search.Indexers.Torznab;

[Trait("Name", "TorznabResponseParserCommentsTests")]
[Trait("Category", "TorznabResponseParser")]
public sealed class TorznabResponseParserCommentsTests : BaseTests
{
    private const string Feed = """
        <rss><channel>
          <item>
            <title>A Princess of Mars</title>
            <guid>guid-0</guid>
            <comments>https://althub.example/details/0</comments>
            <enclosure url="https://althub.example/download/0.torrent" length="1234" />
          </item>
          <item>
            <title>The Wonderful Wizard of Oz</title>
            <guid>guid-1</guid>
            <comments>https://althub.example/details/1</comments>
            <enclosure url="https://althub.example/download/1.torrent" length="1234" />
          </item>
        </channel></rss>
        """;

    [Fact]
    public async Task CommentsPage_IsSentThroughTheGateAsInteractiveEnrichment()
    {
        // Given: two zero-grab results whose comments pages show seven comments
        var requests = new List<Uri>();
        var gate = new RecordingGate(deferAfter: int.MaxValue);
        var parser = CreateParser(gate, requests);
        using var cancellation = new CancellationTokenSource();

        // When
        var results = await parser.ParseAsync(Feed, CreateIndexer(), cancellation.Token);

        // Then: each comments page is one gate call carrying the caller's token
        Assert.Equal(
            [
                new IndexerRequestContext(11, SearchOrigin.Interactive, IndexerRequestKind.Enrichment),
                new IndexerRequestContext(11, SearchOrigin.Interactive, IndexerRequestKind.Enrichment)
            ],
            gate.Contexts);
        Assert.All(gate.Tokens, token => Assert.Equal(cancellation.Token, token));
        Assert.Equal(2, requests.Count);
        Assert.Equal([7, 7], results.Select(result => result.Grabs));
    }

    [Fact]
    public async Task DeferredCommentsPage_KeepsEveryResultAndStopsFetching()
    {
        // Given: a gate that defers every request after the first
        var requests = new List<Uri>();
        var gate = new RecordingGate(deferAfter: 1);
        var parser = CreateParser(gate, requests);

        // When
        var results = await parser.ParseAsync(Feed, CreateIndexer());

        // Then: both results are returned, the deferred one without grabs, and no further gate call is made
        Assert.Equal(["A Princess of Mars", "The Wonderful Wizard of Oz"], results.Select(result => result.Title));
        Assert.Equal([7, 0], results.Select(result => result.Grabs));
        Assert.Equal(2, gate.Contexts.Count);
        Assert.Single(requests);
    }

    [Fact]
    public async Task UnreadableFeed_StillReturnsAnEmptyList()
    {
        var parser = CreateParser(new RecordingGate(deferAfter: int.MaxValue), new List<Uri>());

        var results = await parser.ParseAsync("not xml at all", CreateIndexer());

        Assert.Empty(results);
    }

    private static TorznabResponseParser CreateParser(IIndexerRequestGate gate, List<Uri> requests)
    {
        var handler = new DelegatingHandlerMock((request, _) =>
        {
            requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body><p>7 comments</p></body></html>")
            });
        });

        return new TorznabResponseParser(new HttpClient(handler), NullLogger.Instance, requestGate: gate);
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

    // Records each request's context and token. Dispatches the first deferAfter requests and defers the rest.
    private sealed class RecordingGate : IIndexerRequestGate
    {
        private readonly int _deferAfter;

        public RecordingGate(int deferAfter)
        {
            _deferAfter = deferAfter;
        }

        public List<IndexerRequestContext> Contexts { get; } = new();

        public List<CancellationToken> Tokens { get; } = new();

        public async Task<IndexerRequestResult> SendAsync(
            IndexerRequestContext context,
            Func<CancellationToken, Task<HttpResponseMessage>> dispatch,
            CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            Tokens.Add(cancellationToken);
            if (Contexts.Count > _deferAfter)
            {
                return new IndexerRequestResult.Deferred(
                    new RequestDeferral(DateTime.UtcNow.AddMinutes(5), RequestDeferralReason.CycleRequestBound));
            }

            return new IndexerRequestResult.Sent(await dispatch(cancellationToken));
        }

        public Task<RequestDeferral?> GetDeferralAsync(int indexerId, SearchOrigin origin, CancellationToken cancellationToken)
        {
            return Task.FromResult<RequestDeferral?>(null);
        }
    }
}
