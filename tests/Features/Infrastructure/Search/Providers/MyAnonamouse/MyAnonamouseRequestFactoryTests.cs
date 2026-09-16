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

namespace Listenarr.Tests.Features.Infrastructure.Search.Providers.MyAnonamouse;

[Trait("Name", "MyAnonamouseRequestFactoryTests")]
[Trait("Category", "MyAnonamouseRequestFactory")]
public sealed class MyAnonamouseRequestFactoryTests : BaseTests
{
    [Theory]
    [InlineData(MamTorrentFilter.SearchEverything, "all")]
    [InlineData(MamTorrentFilter.Active, "active")]
    [InlineData(MamTorrentFilter.Freeleech, "fl")]
    [InlineData(MamTorrentFilter.FreeleechOrVip, "fl-VIP")]
    [InlineData(MamTorrentFilter.Vip, "VIP")]
    [InlineData(MamTorrentFilter.NotVip, "nVIP")]
    public void BuildSearchUri_SendsFilterAsSearchType(MamTorrentFilter filter, string expected)
    {
        var uri = MyAnonamouseRequestFactory.BuildSearchUri(
            CreateIndexer(),
            "Ready Player Two",
            new SearchRequest { MyAnonamouse = new MyAnonamouseOptions { Filter = filter } });

        Assert.Equal(expected, SingleValueOf(uri, "tor[searchType]"));
    }

    [Fact]
    public void BuildSearchUri_WithoutOptions_SearchesEverything()
    {
        var uri = MyAnonamouseRequestFactory.BuildSearchUri(CreateIndexer(), "Ready Player Two");

        Assert.Equal("all", SingleValueOf(uri, "tor[searchType]"));
    }

    [Fact]
    public void BuildSearchUri_DoesNotSendBooleanFilterParameters()
    {
        var uri = MyAnonamouseRequestFactory.BuildSearchUri(
            CreateIndexer(),
            "Ready Player Two",
            new SearchRequest { MyAnonamouse = new MyAnonamouseOptions { Filter = MamTorrentFilter.NotVip } });

        foreach (var rejected in new[] { "tor[onlyActive]", "tor[onlyFreeleech]", "tor[freeleechOrVip]", "tor[onlyVip]", "tor[notVip]" })
        {
            Assert.DoesNotContain(Uri.EscapeDataString(rejected), uri.Query, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BuildSearchUri_QueryShapeDoesNotChangeSearchType()
    {
        // "Title by Author" used to be turned into tor[searchType]=title, which collides with the
        // filter parameter. Which fields are matched is tor[srchIn][...] territory.
        var uri = MyAnonamouseRequestFactory.BuildSearchUri(
            CreateIndexer(),
            "Ready Player Two by Ernest Cline",
            new SearchRequest { MyAnonamouse = new MyAnonamouseOptions { Filter = MamTorrentFilter.Freeleech } });

        Assert.Equal("fl", SingleValueOf(uri, "tor[searchType]"));
        Assert.Contains(Uri.EscapeDataString("tor[srchIn][title]") + "=true", uri.Query, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("tor[srchIn][author]") + "=true", uri.Query, StringComparison.Ordinal);
    }

    private static Indexer CreateIndexer() => new()
    {
        Id = 7,
        Name = "MyAnonamouse",
        Url = "https://mam.example",
        Implementation = "MyAnonamouse",
        Type = "Torrent"
    };

    private static string SingleValueOf(Uri uri, string parameterName)
    {
        var encoded = Uri.EscapeDataString(parameterName) + "=";
        var matches = uri.Query.TrimStart('?')
            .Split('&')
            .Where(pair => pair.StartsWith(encoded, StringComparison.Ordinal))
            .Select(pair => Uri.UnescapeDataString(pair[encoded.Length..]))
            .ToList();

        return Assert.Single(matches);
    }
}
