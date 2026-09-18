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

[Trait("Name", "MyAnonamouseIndexerFlagTests")]
[Trait("Category", "MyAnonamouseIndexerFlag")]
public sealed class MyAnonamouseIndexerFlagTests : BaseTests
{
    [Theory]
    [InlineData(@"""free"": true")]
    [InlineData(@"""personal_freeleech"": true")]
    public void FreeReleasesAreFlaggedFreeleech(string freeleechField)
    {
        var results = Parse($$"""
        [
          { "id": 1, "dl": "abc", "title": "Free Release", "size": 100, {{freeleechField}} }
        ]
        """);

        Assert.Equal(["freeleech"], Assert.Single(results).IndexerFlags);
    }

    [Fact]
    public void PaidReleasesCarryNoFlags()
    {
        var results = Parse("""
        [
          { "id": 1, "dl": "abc", "title": "Paid Release", "size": 100, "free": false, "personal_freeleech": false }
        ]
        """);

        Assert.Empty(Assert.Single(results).IndexerFlags);
    }

    [Fact]
    public void ReleasesWithNoFreeleechFieldsCarryNoFlags()
    {
        var results = Parse("""
        [
          { "id": 1, "dl": "abc", "title": "Unknown Release", "size": 100 }
        ]
        """);

        Assert.Empty(Assert.Single(results).IndexerFlags);
    }

    [Fact]
    public void VipFreeleechIsNotAFreeleechFlag()
    {
        // fl_vip is free only to a VIP or Elite VIP member, so calling it freeleech would tell a
        // reader who is not one that a release costs them nothing. It withholds the wedge, which is
        // a separate question, and the two decisions sit on adjacent lines in the parser: this is
        // here so that collapsing one into the other goes red instead of quietly mislabelling.
        var results = Parse("""
        [
          { "id": 1, "dl": "abc", "title": "VIP Freeleech Release", "size": 100, "free": false, "personal_freeleech": false, "fl_vip": true }
        ]
        """);

        Assert.Empty(Assert.Single(results).IndexerFlags);
    }

    [Fact]
    public void TheFreeleechFlagReachesThePublicIndexerResultDto()
    {
        var results = Parse("""
        [
          { "id": 1, "dl": "abc", "title": "Free Release", "size": 100, "free": true }
        ]
        """);

        var dto = SearchResultConverters.ToIndexerResultDto(Assert.Single(results));

        Assert.Equal(["freeleech"], dto.IndexerFlags);
    }

    private static List<IndexerSearchResult> Parse(string json) =>
        MyAnonamouseResponseParser.Parse(
            json,
            new Indexer
            {
                Name = "MyAnonamouse",
                Url = "https://www.myanonamouse.net",
                Type = "Torrent",
                Implementation = "MyAnonamouse",
                AdditionalSettings = """{ "mam_id": "test_mam" }"""
            },
            NullLogger.Instance);
}
