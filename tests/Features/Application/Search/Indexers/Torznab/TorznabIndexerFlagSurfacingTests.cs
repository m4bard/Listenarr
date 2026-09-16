/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Application.Search.Indexers.Torznab;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Search.Indexers.Torznab;

[Trait("Name", "TorznabIndexerFlagSurfacingTests")]
[Trait("Category", "TorznabIndexerFlagSurfacing")]
public sealed class TorznabIndexerFlagSurfacingTests : BaseTests
{
    private const string FeedWithFlags = """
    <?xml version="1.0" encoding="UTF-8"?>
    <rss version="2.0" xmlns:torznab="http://torznab.com/schemas/2015/feed">
      <channel>
        <item>
          <title>A Free Book</title>
          <guid>https://tracker.example/details/1</guid>
          <enclosure url="https://tracker.example/download/1.torrent" length="1048576" type="application/x-bittorrent" />
          <torznab:attr name="size" value="1048576" />
          <torznab:attr name="seeders" value="9" />
          <torznab:attr name="downloadvolumefactor" value="0" />
          <torznab:attr name="uploadvolumefactor" value="2" />
          <torznab:attr name="tag" value="internal" />
        </item>
        <item>
          <title>A Paid Book</title>
          <guid>https://tracker.example/details/2</guid>
          <enclosure url="https://tracker.example/download/2.torrent" length="1048576" type="application/x-bittorrent" />
          <torznab:attr name="size" value="1048576" />
          <torznab:attr name="seeders" value="4" />
          <torznab:attr name="downloadvolumefactor" value="1" />
          <torznab:attr name="uploadvolumefactor" value="1" />
        </item>
      </channel>
    </rss>
    """;

    [Fact]
    public async Task ParseAsync_ReadsFlagsFromTheStandardTorznabAttributes()
    {
        using var httpClient = new HttpClient();
        var parser = new TorznabResponseParser(httpClient, NullLogger.Instance);

        var results = await parser.ParseAsync(FeedWithFlags, CreateIndexer());

        Assert.Equal(2, results.Count);
        Assert.Equal(["freeleech", "doubleupload", "internal"], results[0].IndexerFlags);
        Assert.Empty(results[1].IndexerFlags);
    }

    [Fact]
    public async Task ParsedFlagsReachThePublicIndexerResultDto()
    {
        using var httpClient = new HttpClient();
        var parser = new TorznabResponseParser(httpClient, NullLogger.Instance);

        var results = await parser.ParseAsync(FeedWithFlags, CreateIndexer());
        var dto = SearchResultConverters.ToIndexerResultDto(results[0]);

        Assert.Equal(["freeleech", "doubleupload", "internal"], dto.IndexerFlags);
    }

    [Fact]
    public async Task ParsedFlagsSurviveTheLegacySearchResultRoundTrip()
    {
        using var httpClient = new HttpClient();
        var parser = new TorznabResponseParser(httpClient, NullLogger.Instance);

        var results = await parser.ParseAsync(FeedWithFlags, CreateIndexer());
        var legacy = SearchResultConverters.ToSearchResult(results[0]);
        var roundTripped = SearchResultConverters.ToIndexerSearchResult(legacy);

        Assert.Equal(["freeleech", "doubleupload", "internal"], legacy.IndexerFlags);
        Assert.Equal(["freeleech", "doubleupload", "internal"], roundTripped.IndexerFlags);
    }

    [Fact]
    public async Task TheInfrastructureProviderReadsTheSameFlags()
    {
        // Torznab attributes are parsed in two places. Both have to learn about the flags, or a
        // result carries them only on whichever code path happened to handle it.
        using var httpClient = new HttpClient();
        var provider = new TorznabNewznabSearchProvider(httpClient, NullLogger<TorznabNewznabSearchProvider>.Instance);
        var parse = typeof(TorznabNewznabSearchProvider).GetMethod(
            "ParseTorznabResponseAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(parse);

        var results = await (Task<List<IndexerSearchResult>>)parse!.Invoke(
            provider,
            [FeedWithFlags, CreateIndexer()])!;

        Assert.Equal(2, results.Count);
        Assert.Equal(["freeleech", "doubleupload", "internal"], results[0].IndexerFlags);
        Assert.Empty(results[1].IndexerFlags);
    }

    private static Indexer CreateIndexer() => new()
    {
        Id = 3,
        Name = "Torznab",
        Url = "https://tracker.example/api",
        Implementation = "Torznab",
        Type = "Torrent"
    };
}
