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
using System.Security;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Search.Providers.Torznab;

/// <summary>
/// Feed responses, a recording gate and a provider wired to a stub indexer, shared by the
/// Torznab feed adapter tests.
/// </summary>
internal static class TorznabFeedTestKit
{
    public const int IndexerId = 7;

    // Corpus entries, written as "Author - Title".
    public static readonly string[] Titles =
    [
        "Edgar Rice Burroughs - A Princess of Mars",
        "Jules Verne - Twenty Thousand Leagues Under the Sea",
        "Jules Verne - 20,000 Leagues Under the Sea"
    ];

    public static IndexerSearchContext FeedPoll { get; } =
        new(SearchOrigin.Discovery, CycleAllowance: null) { QueryKind = IndexerRequestKind.FeedPoll };

    public static IndexerSearchContext FeedPage { get; } =
        new(SearchOrigin.Discovery, CycleAllowance: null) { QueryKind = IndexerRequestKind.FeedPage };

    public static Indexer CreateIndexer(string implementation = "Torznab", string url = "https://indexer.example")
    {
        var indexer = new IndexerBuilder()
            .WithId(IndexerId)
            .WithName("Feed")
            .WithType("Torrent")
            .WithImplementation(implementation)
            .WithUrl(url)
            .WithApiKey("feed-key")
            .Build();
        indexer.Categories = "3030";
        return indexer;
    }

    /// <summary>
    /// A feed of <paramref name="count"/> items, each with a distinct size and download link.
    /// </summary>
    public static string Feed(
        int count,
        int? total = null,
        bool withGuid = true,
        string? pubDate = "Fri, 13 Nov 2026 10:00:00 +0200",
        string? commentsUrl = null)
    {
        var items = Enumerable.Range(0, count).Select(number =>
            "<item>"
            + $"<title>{SecurityElement.Escape(Titles[number % Titles.Length])}</title>"
            + (withGuid ? $"<guid>feed-item-{number}</guid>" : string.Empty)
            + (pubDate is null ? string.Empty : $"<pubDate>{pubDate}</pubDate>")
            + (commentsUrl is null ? string.Empty : $"<comments>{commentsUrl}{number}#comments</comments>")
            + $"<enclosure url=\"https://indexer.example/download/{number}\" length=\"{1000 + number}\" />"
            + "</item>");
        var response = total is null ? string.Empty : $"<newznab:response offset=\"0\" total=\"{total}\" />";
        return "<rss xmlns:newznab=\"http://www.newznab.com/DTD/2010/feeds/attributes/\"><channel>"
            + response + string.Concat(items) + "</channel></rss>";
    }

    public static TorznabNewznabSearchProvider CreateProvider(IIndexerRequestGate gate, WireLog wire, HttpClient? client = null)
    {
        return new TorznabNewznabSearchProvider(
            client ?? new HttpClient(wire.Handler()),
            gate,
            NullLogger<TorznabNewznabSearchProvider>.Instance);
    }
}

/// <summary>
/// Answers every wire request with the next scripted response and records its URL.
/// </summary>
internal sealed class WireLog
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    public List<Uri> Requests { get; } = new();

    public WireLog Respond(string xml) => Respond(HttpStatusCode.OK, xml);

    public WireLog Respond(HttpStatusCode status, string body)
    {
        _responses.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }));
        return this;
    }

    public WireLog Respond(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
    {
        _responses.Enqueue(response);
        return this;
    }

    public DelegatingHandlerMock Handler()
    {
        return new DelegatingHandlerMock((request, token) =>
        {
            Requests.Add(request.RequestUri!);
            return _responses.Count > 0
                ? _responses.Dequeue()(request, token)
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>detail page: 12 comments</html>") });
        });
    }
}

/// <summary>
/// Records every request context. Dispatches as the pass-through gate does, except for the
/// calls listed in <see cref="DeferCalls"/>, which it defers without dispatching.
/// </summary>
internal sealed class RecordingFeedGate : IIndexerRequestGate
{
    public static readonly RequestDeferral Deferral =
        new(new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc), RequestDeferralReason.CycleRequestBound);

    public List<IndexerRequestContext> Contexts { get; } = new();

    public HashSet<int> DeferCalls { get; } = new();

    public async Task<IndexerRequestResult> SendAsync(
        IndexerRequestContext context,
        Func<CancellationToken, Task<HttpResponseMessage>> dispatch,
        CancellationToken cancellationToken)
    {
        Contexts.Add(context);
        if (DeferCalls.Contains(Contexts.Count))
        {
            return new IndexerRequestResult.Deferred(Deferral);
        }

        return new IndexerRequestResult.Sent(await dispatch(cancellationToken));
    }

    public Task<RequestDeferral?> GetDeferralAsync(int indexerId, SearchOrigin origin, CancellationToken cancellationToken)
    {
        return Task.FromResult<RequestDeferral?>(null);
    }
}
