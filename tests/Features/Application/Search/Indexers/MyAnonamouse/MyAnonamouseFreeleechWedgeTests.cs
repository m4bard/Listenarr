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
            "https://www.myanonamouse.net/tor/download.php/abc123?fl=1&mam_id=test_mam",
            Assert.Single(results).TorrentUrl);
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
