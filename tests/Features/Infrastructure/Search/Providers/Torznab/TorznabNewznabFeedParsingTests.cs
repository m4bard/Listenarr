/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Application.Search.Discovery;
using Listenarr.Tests.Common;
using Listenarr.Tests.Features.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static Listenarr.Tests.Features.Infrastructure.Search.Providers.Torznab.TorznabFeedTestKit;

namespace Listenarr.Tests.Features.Infrastructure.Search.Providers.Torznab;

[Trait("Name", "TorznabNewznabFeedParsingTests")]
[Trait("Category", "TorznabNewznabSearchProvider")]
public sealed class TorznabNewznabFeedParsingTests : BaseTests
{
    [Fact]
    public async Task FetchPageAsync_GuidlessItemOnTwoPolls_HasOneReleaseIdAndIsStoredOnce()
    {
        // Given: the same guid-less item on two polls; the parser gives it a fresh random id each time
        var database = await DiscoveryStoreTestDatabase.CreateAsync(FileService.GetTempPath());
        var indexerId = database.IndexerIds[0];
        var wire = new WireLog().Respond(Feed(1, withGuid: false)).Respond(Feed(1, withGuid: false));
        var provider = CreateProvider(new RecordingFeedGate(), wire);
        var indexer = CreateIndexer();
        indexer.Id = indexerId;
        var store = database.CreateStore();
        var pollUtc = new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);

        // When: both polls are read and stored
        var first = Assert.Single((await provider.FetchPageAsync(indexer, 0, FeedPoll, CancellationToken.None)).Items);
        var second = Assert.Single((await provider.FetchPageAsync(indexer, 0, FeedPoll, CancellationToken.None)).Items);
        foreach (var (item, offset) in new[] { (first, 0), (second, 1) })
        {
            await store.CommitPollAsync(
                indexerId,
                [new DiscoveredReleaseCandidate(item.ReleaseId, item.PublishedUtc, item.Result.Title, "{}")],
                advance: null,
                gap: null,
                futureDatedDelta: 0,
                undatedDelta: 0,
                pollUtc.AddMinutes(15 * offset),
                CancellationToken.None);
        }

        // Then: one release id and one row, although the parser's ids differ
        Assert.NotEqual(first.Result.Id, second.Result.Id);
        Assert.Equal(first.ReleaseId, second.ReleaseId);
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.DiscoveredReleases.CountAsync(release => release.IndexerId == indexerId));
    }

    [Fact]
    public async Task FetchPageAsync_AlthubFeedWithCommentLinks_SendsExactlyOneRequest()
    {
        // Given: an althub-style feed whose items link to comment pages, which a search scrapes for grabs
        var gate = new RecordingFeedGate();
        var wire = new WireLog().Respond(Feed(3, commentsUrl: "https://api.althub.co.za/details/"));
        var provider = CreateProvider(gate, wire);
        var indexer = CreateIndexer("Newznab", "https://api.althub.co.za");

        // When
        var page = await provider.FetchPageAsync(indexer, 0, FeedPoll, CancellationToken.None);

        // Then: the page is one request; no comment page was fetched
        Assert.Equal(IndexerSearchStatus.Completed, page.Status);
        Assert.Equal(3, page.Items.Count);
        Assert.Single(wire.Requests);
        Assert.Single(gate.Contexts);
        Assert.All(page.Items, item => Assert.Equal(0, item.Result.Grabs));
    }

    [Fact]
    public async Task FetchPageAsync_NewznabResponseTotal_IsRead()
    {
        var provider = CreateProvider(new RecordingFeedGate(), new WireLog().Respond(Feed(2, total: 1234)));

        var page = await provider.FetchPageAsync(CreateIndexer(), 0, FeedPoll, CancellationToken.None);

        Assert.Equal(1234, page.TotalAvailable);
    }

    [Fact]
    public async Task FetchPageAsync_ItemWithoutDownloadLink_IsLeftOutButCountedAsReceived()
    {
        // Given: three items, the last with no enclosure or link
        var xml = Feed(3).Replace("<enclosure url=\"https://indexer.example/download/2\" length=\"1002\" />", string.Empty);
        var provider = CreateProvider(new RecordingFeedGate(), new WireLog().Respond(xml));

        var page = await provider.FetchPageAsync(CreateIndexer(), 0, FeedPoll, CancellationToken.None);

        Assert.Equal(3, page.ItemsReceived);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task FetchPageAsync_PubDateWithOffset_IsPublishedUtc()
    {
        var provider = CreateProvider(new RecordingFeedGate(), new WireLog().Respond(Feed(1, pubDate: "Fri, 13 Nov 2026 10:00:00 +0200")));

        var item = Assert.Single((await provider.FetchPageAsync(CreateIndexer(), 0, FeedPoll, CancellationToken.None)).Items);

        Assert.Equal(new DateTime(2026, 11, 13, 8, 0, 0, DateTimeKind.Utc), item.PublishedUtc);
        Assert.Equal(DateTimeKind.Utc, item.PublishedUtc!.Value.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not a date")]
    public async Task FetchPageAsync_MissingOrUnparseablePubDate_HasNoPublishedUtc(string? pubDate)
    {
        var provider = CreateProvider(new RecordingFeedGate(), new WireLog().Respond(Feed(1, pubDate: pubDate)));

        var item = Assert.Single((await provider.FetchPageAsync(CreateIndexer(), 0, FeedPoll, CancellationToken.None)).Items);

        Assert.Null(item.PublishedUtc);
    }

    [Theory]
    [InlineData("2026-11-13T10:00:00.0000000+02:00", 8)]
    [InlineData("2026-11-13T10:00:00.0000000Z", 10)]
    [InlineData("2026-11-13T10:00:00", 10)]
    public void ParsePublishedUtc_RoundTripValue_IsUtc(string value, int expectedHour)
    {
        var published = TorznabNewznabSearchProvider.ParsePublishedUtc(value);

        Assert.Equal(new DateTime(2026, 11, 13, expectedHour, 0, 0, DateTimeKind.Utc), published);
        Assert.Equal(DateTimeKind.Utc, published!.Value.Kind);
    }

    [Fact]
    public void ToFeedItems_ResultWithNoReleaseIdentity_IsSkippedAndCounted()
    {
        var withIdentity = new IndexerSearchResult { Id = "a", Title = Titles[0], TorrentUrl = "https://indexer.example/download/1" };
        var withoutIdentity = new IndexerSearchResult { Id = "b", Title = " " };

        var items = TorznabNewznabSearchProvider.ToFeedItems([withIdentity, withoutIdentity], out var skipped);

        Assert.Same(withIdentity, Assert.Single(items).Result);
        Assert.Equal(1, skipped);
    }
}
