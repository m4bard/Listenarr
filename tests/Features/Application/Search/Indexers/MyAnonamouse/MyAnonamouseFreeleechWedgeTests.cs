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
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Search.Indexers.MyAnonamouse;

[Trait("Name", "MyAnonamouseFreeleechWedgeTests")]
[Trait("Category", "MyAnonamouseFreeleechWedge")]
public sealed class MyAnonamouseFreeleechWedgeTests : BaseTests
{
    private const string PaidItem = """
    [
      { "id": 123, "dl": "abc123", "title": "Paid Release", "size": 12345, "free": false, "personal_freeleech": false }
    ]
    """;

    [Theory]
    [InlineData("Preferred")]
    [InlineData("Required")]
    public void DownloadUrl_SpendsWedge_WhenPreferenceAsksForOne(string preference)
    {
        var results = MyAnonamouseResponseParser.Parse(PaidItem, CreateIndexer(preference), NullLogger.Instance);

        Assert.Contains("fl=1", Assert.Single(results).TorrentUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadUrl_SpendsWedge_WhenPreferenceIsStoredUnderMamOptions()
    {
        var indexer = CreateIndexer(null);
        indexer.AdditionalSettings = """{ "mam_id": "test_mam", "mam_options": { "freeleechWedge": "Preferred" } }""";

        var results = MyAnonamouseResponseParser.Parse(PaidItem, indexer, NullLogger.Instance);

        Assert.Contains("fl=1", Assert.Single(results).TorrentUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadUrl_KeepsWedge_WhenNoPreferenceIsConfigured()
    {
        var results = MyAnonamouseResponseParser.Parse(PaidItem, CreateIndexer(null), NullLogger.Instance);

        Assert.DoesNotContain("fl=1", Assert.Single(results).TorrentUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadUrl_KeepsWedge_WhenPreferenceIsNever()
    {
        var results = MyAnonamouseResponseParser.Parse(PaidItem, CreateIndexer("Never"), NullLogger.Instance);

        Assert.DoesNotContain("fl=1", Assert.Single(results).TorrentUrl, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"""free"": true, ""personal_freeleech"": false")]
    [InlineData(@"""free"": false, ""personal_freeleech"": true")]
    [InlineData(@"""free"": 1, ""personal_freeleech"": 0")]
    [InlineData(@"""free"": ""1"", ""personal_freeleech"": ""0""")]
    [InlineData(@"""free"": false, ""personal_freeleech"": false, ""fl_vip"": true")]
    public void DownloadUrl_KeepsWedge_WhenTheReleaseIsAlreadyFree(string freeleechFields)
    {
        var json = $$"""
        [
          { "id": 123, "dl": "abc123", "title": "Free Release", "size": 12345, {{freeleechFields}} }
        ]
        """;

        var results = MyAnonamouseResponseParser.Parse(json, CreateIndexer("Required"), NullLogger.Instance);

        Assert.DoesNotContain("fl=1", Assert.Single(results).TorrentUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadUrl_KeepsTheMamIdItAlreadyCarried()
    {
        var results = MyAnonamouseResponseParser.Parse(PaidItem, CreateIndexer("Preferred"), NullLogger.Instance);

        Assert.Equal(
            "https://www.myanonamouse.net/tor/download.php?tid=123&fl=1&mam_id=test_mam",
            Assert.Single(results).TorrentUrl);
    }

    [Fact]
    public void DownloadUrl_ComesFromTheItem_WhenTheItemSuppliesOne()
    {
        // PopulateDownloadLinks prefers a downloadUrl field on the item over the URL built here,
        // so a wedge would not reach the grab. MyAnonamouse does not send that field, going by the
        // model Prowlarr deserialises its search response into (MyAnonamouseTorrent has no such
        // property), but nothing here guarantees it never will, and this pins what happens if it
        // does. An unspent wedge is the safe side of that: it costs ratio rather than a consumable.
        var json = """
        [
          {
            "id": 123,
            "dl": "abc123",
            "title": "Paid Release",
            "size": 12345,
            "free": false,
            "personal_freeleech": false,
            "downloadUrl": "https://www.myanonamouse.net/some/other/path.torrent"
          }
        ]
        """;

        var result = Assert.Single(MyAnonamouseResponseParser.Parse(json, CreateIndexer("Required"), NullLogger.Instance));

        Assert.Equal("https://www.myanonamouse.net/some/other/path.torrent", result.TorrentUrl);
        Assert.DoesNotContain("fl=1", result.TorrentUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadUrl_ReadsTheWedgeFromMamOptionsAlone_WhenBothShapesArePresent()
    {
        // The settings form writes options under mam_options and reads flat properties only when
        // that object is absent (IndexerFormModal.vue), and IndexerAdditionalSettingsParser follows
        // the same rule. A flat property beside a mam_options object is therefore a leftover from
        // the older shape, not an override, and one setting must not have two parsers reading it
        // two different ways.
        var indexer = CreateIndexer(null);
        indexer.AdditionalSettings = """{ "mam_id": "test_mam", "freeleechWedge": "Required", "mam_options": { "language": "1" } }""";

        var results = MyAnonamouseResponseParser.Parse(PaidItem, indexer, NullLogger.Instance);

        Assert.DoesNotContain("fl=1", Assert.Single(results).TorrentUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadUrl_SpendsWedge_WhenTheSearchAsksForOne()
    {
        // mamFreeleechWedge arrives on the search request from the manual search modal. It used to
        // feed only tor[freeleechWedge], which MyAnonamouse ignored, so it has to reach the grab or
        // it does nothing at all.
        var results = MyAnonamouseResponseParser.Parse(
            PaidItem,
            CreateIndexer(null),
            NullLogger.Instance,
            MamFreeleechWedge.Preferred);

        Assert.Contains("fl=1", Assert.Single(results).TorrentUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadUrl_LetsTheSearchOverrideTheStoredPreference()
    {
        var results = MyAnonamouseResponseParser.Parse(
            PaidItem,
            CreateIndexer("Never"),
            NullLogger.Instance,
            MamFreeleechWedge.Required);

        Assert.Contains("fl=1", Assert.Single(results).TorrentUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadUrl_KeepsWedge_WhenTheItemCarriesNoTorrentId()
    {
        // An item with no "id" gets a generated one so the result has a key, and that generated id
        // is not a torrent id. The download URL has to stay on the hash, which works, rather than
        // move to a tid that does not exist in order to carry a wedge.
        var json = """
        [
          { "dl": "abc123", "title": "Paid Release", "size": 12345, "free": false, "personal_freeleech": false }
        ]
        """;

        var result = Assert.Single(MyAnonamouseResponseParser.Parse(json, CreateIndexer("Required"), NullLogger.Instance));

        Assert.Equal("https://www.myanonamouse.net/tor/download.php/abc123?mam_id=test_mam", result.TorrentUrl);
    }

    private static Indexer CreateIndexer(string? freeleechWedge) => new()
    {
        Name = "MyAnonamouse",
        Url = "https://www.myanonamouse.net",
        Type = "Torrent",
        Implementation = "MyAnonamouse",
        AdditionalSettings = freeleechWedge == null
            ? """{ "mam_id": "test_mam" }"""
            : $$"""{ "mam_id": "test_mam", "freeleechWedge": "{{freeleechWedge}}" }"""
    };
}
