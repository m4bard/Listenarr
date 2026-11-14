/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Tests.Common;
using static Listenarr.Tests.Features.Infrastructure.Search.Providers.Torznab.TorznabFeedTestKit;

namespace Listenarr.Tests.Features.Infrastructure.Search.Providers.Torznab;

[Trait("Name", "TorznabNewznabFeedRequestBuilderTests")]
[Trait("Category", "TorznabNewznabSearchProvider")]
public sealed class TorznabNewznabFeedRequestBuilderTests : BaseTests
{
    [Fact]
    public void BuildUrl_FeedPage_HasNoQueryAndPagesByOffset()
    {
        var url = TorznabNewznabFeedRequestBuilder.BuildUrl(CreateIndexer(), 150);

        Assert.Equal(
            "https://indexer.example/api?t=search&apikey=feed-key&cat=3030&limit=100&offset=150&extended=1",
            url);
    }

    [Fact]
    public void BuildUrl_UrlEndingInApi_IsNotGivenASecondApiPath()
    {
        var url = TorznabNewznabFeedRequestBuilder.BuildUrl(CreateIndexer("Newznab", "https://proxy.example/1/api/"), 0);

        Assert.StartsWith("https://proxy.example/1/api?t=search&", url);
        Assert.DoesNotContain("q=", url);
        Assert.Contains("&offset=0&", url);
    }

    [Fact]
    public void BuildUrl_IndexerWithoutCategories_SendsNoCategory()
    {
        var indexer = CreateIndexer();
        indexer.Categories = string.Empty;

        var url = TorznabNewznabFeedRequestBuilder.BuildUrl(indexer, 0);

        Assert.DoesNotContain("cat=", url);
    }

    [Fact]
    public void BuildUrl_NegativeOffset_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TorznabNewznabFeedRequestBuilder.BuildUrl(CreateIndexer(), -1));
    }
}
