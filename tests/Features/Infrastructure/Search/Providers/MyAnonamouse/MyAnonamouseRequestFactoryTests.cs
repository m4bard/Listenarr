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

    [Theory]
    [InlineData("Ready Player Two")]
    [InlineData("Ready Player Two by Ernest Cline")]
    [InlineData("Ready Player Two - Ernest Cline")]
    [InlineData("Cline, Ready Player Two")]
    public void BuildSearchUri_QueryShapeDoesNotChangeSearchType(string query)
    {
        // A query shaped with " by ", with " - " or with a comma used to be split into a title and
        // an author, and the result decided tor[searchType]: title, author, or all. That is not one
        // of the values the API accepts and it collides with the filter. Which fields are matched is
        // tor[srchIn][...] territory, and all four shapes leave it alone. All four rows because the
        // three splitting branches were separate code paths.
        var uri = MyAnonamouseRequestFactory.BuildSearchUri(
            CreateIndexer(),
            query,
            new SearchRequest { MyAnonamouse = new MyAnonamouseOptions { Filter = MamTorrentFilter.Freeleech } });

        Assert.Equal("fl", SingleValueOf(uri, "tor[searchType]"));
        Assert.Contains(Uri.EscapeDataString("tor[srchIn][title]") + "=true", uri.Query, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("tor[srchIn][author]") + "=true", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSearchUri_SendsPerPageAsAPlainParameter()
    {
        // MyAnonamouse reads the page size as a top-level "perpage", not inside the tor[] array.
        // Prowlarr sends it that way (src/NzbDrone.Core/Indexers/Definitions/MyAnonamouse.cs), and
        // so does this codebase's own debug search (IndexerDebugSearchWorkflow.BuildMamSearchRequest).
        var uri = MyAnonamouseRequestFactory.BuildSearchUri(CreateIndexer(), "Ready Player Two", perPage: 37);

        Assert.Equal("37", SingleValueOf(uri, "perpage"));
        Assert.DoesNotContain(Uri.EscapeDataString("tor[perpage]"), uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSearchUri_SendsTheRequestedSearchLanguage()
    {
        // The language option was threaded from the query string to MyAnonamouseOptions and then
        // dropped: tor[browse_lang][] went out hardcoded to 1, English. MyAnonamouse numbers its
        // languages, and Prowlarr sends the chosen ids in tor[browse_lang]
        // (src/NzbDrone.Core/Indexers/Definitions/MyAnonamouse.cs, MyAnonamouseSearchLanguages).
        var uri = MyAnonamouseRequestFactory.BuildSearchUri(
            CreateIndexer(),
            "Ready Player Two",
            new SearchRequest { MyAnonamouse = new MyAnonamouseOptions { SearchLanguage = "2" } });

        Assert.Equal("2", SingleValueOf(uri, "tor[browse_lang][]"));
    }

    [Fact]
    public void BuildSearchUri_WithoutASearchLanguage_KeepsEnglish()
    {
        var uri = MyAnonamouseRequestFactory.BuildSearchUri(CreateIndexer(), "Ready Player Two");

        Assert.Equal("1", SingleValueOf(uri, "tor[browse_lang][]"));
    }

    [Theory]
    [InlineData("english")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2; drop")]
    public void BuildSearchUri_KeepsEnglishForALanguageMyAnonamouseCannotRead(string language)
    {
        // MyAnonamouse identifies languages by number. Anything else would be a parameter it
        // ignores, which is the bug this is fixing, so the default stands instead.
        var uri = MyAnonamouseRequestFactory.BuildSearchUri(
            CreateIndexer(),
            "Ready Player Two",
            new SearchRequest { MyAnonamouse = new MyAnonamouseOptions { SearchLanguage = language } });

        Assert.Equal("1", SingleValueOf(uri, "tor[browse_lang][]"));
    }

    [Theory]
    [InlineData(MamFreeleechWedge.Never)]
    [InlineData(MamFreeleechWedge.Preferred)]
    [InlineData(MamFreeleechWedge.Required)]
    public void BuildSearchUri_DoesNotAskTheSearchForAFreeleechWedge(MamFreeleechWedge wedge)
    {
        // A wedge is applied to a download, not to a search. Prowlarr carries the preference as a
        // setting and spends it when it builds the download URL
        // (src/NzbDrone.Core/Indexers/Definitions/MyAnonamouse.cs, GetDownloadUrl); nothing sends it
        // with the query. tor[freeleechWedge] was the fourth parameter in this function that
        // MyAnonamouse had no use for, and it is the one that looked like it was doing something.
        var uri = MyAnonamouseRequestFactory.BuildSearchUri(
            CreateIndexer(),
            "Ready Player Two",
            new SearchRequest { MyAnonamouse = new MyAnonamouseOptions { FreeleechWedge = wedge } });

        Assert.DoesNotContain(Uri.EscapeDataString("tor[freeleechWedge]"), uri.Query, StringComparison.Ordinal);
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
