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

namespace Listenarr.Tests.Features.Application.Search.Indexers.MyAnonamouse;

[Trait("Name", "MyAnonamouseDownloadUrlBuilderTests")]
[Trait("Category", "MyAnonamouseDownloadUrlBuilder")]
public sealed class MyAnonamouseDownloadUrlBuilderTests : BaseTests
{
    [Fact]
    public void Build_WithoutAWedge_PrefersTheHashForm()
    {
        var url = MyAnonamouseDownloadUrlBuilder.Build("abc123", "456", CreateIndexer());

        Assert.Equal("https://www.myanonamouse.net/tor/download.php/abc123", url);
    }

    [Fact]
    public void Build_WithoutAHash_UsesTheTorrentIdForm()
    {
        var url = MyAnonamouseDownloadUrlBuilder.Build(string.Empty, "456", CreateIndexer());

        Assert.Equal("https://www.myanonamouse.net/tor/download.php?tid=456", url);
    }

    [Fact]
    public void Build_SpendingAWedge_UsesTheOnlyFormThatIsEvidenced()
    {
        // Prowlarr adds fl=1 to /tor/download.php?tid={id} and to nothing else
        // (src/NzbDrone.Core/Indexers/Definitions/MyAnonamouse.cs, GetDownloadUrl). Whether
        // MyAnonamouse reads fl on the /download.php/{hash} form is unknown here, so a grab
        // that spends a wedge takes the evidenced shape even when a hash is available.
        var url = MyAnonamouseDownloadUrlBuilder.Build("abc123", "456", CreateIndexer(), spendFreeleechWedge: true);

        Assert.Equal("https://www.myanonamouse.net/tor/download.php?tid=456&fl=1", url);
        Assert.DoesNotContain("download.php/abc123", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_SpendingAWedgeWithNoTorrentId_LeavesTheWedgeUnspent()
    {
        // No torrent id means no evidenced URL to put fl on. Not spending a wedge costs ratio,
        // which seeding earns back; guessing at a URL shape risks the grab itself.
        var url = MyAnonamouseDownloadUrlBuilder.Build("abc123", string.Empty, CreateIndexer(), spendFreeleechWedge: true);

        Assert.Equal("https://www.myanonamouse.net/tor/download.php/abc123", url);
        Assert.DoesNotContain("fl=1", url, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("8f14e45f-ceea-467a-9575-28bd1b1a5f70")]
    [InlineData("abc123")]
    [InlineData("12.5")]
    [InlineData(" 456 ")]
    [InlineData("0")]
    [InlineData("-1")]
    public void Build_SpendingAWedgeOnSomethingThatIsNotATorrentId_KeepsTheHashForm(string torrentId)
    {
        // A MyAnonamouse torrent id is a number: Prowlarr deserialises it as int
        // (src/NzbDrone.Core/Indexers/Definitions/MyAnonamouse.cs:799) and passes it to
        // GetDownloadUrl as int. Anything else in tid would build a URL that cannot resolve, and
        // the caller substitutes a GUID for an item that carries no id at all, so "non-empty" is
        // not good enough a test. A usable hash is worth more than a wedge spent on a dead URL.
        var url = MyAnonamouseDownloadUrlBuilder.Build("abc123", torrentId, CreateIndexer(), spendFreeleechWedge: true);

        Assert.Equal("https://www.myanonamouse.net/tor/download.php/abc123", url);
        Assert.DoesNotContain("fl=1", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithNeitherHashNorTorrentId_ReturnsNothing()
    {
        Assert.Equal(string.Empty, MyAnonamouseDownloadUrlBuilder.Build(string.Empty, string.Empty, CreateIndexer(), spendFreeleechWedge: true));
    }

    private static Indexer CreateIndexer() => new()
    {
        Name = "MyAnonamouse",
        Url = "https://www.myanonamouse.net",
        Type = "Torrent",
        Implementation = "MyAnonamouse"
    };
}
