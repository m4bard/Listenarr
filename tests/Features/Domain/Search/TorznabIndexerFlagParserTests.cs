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

namespace Listenarr.Tests.Features.Domain.Search;

[Trait("Name", "TorznabIndexerFlagParserTests")]
[Trait("Category", "TorznabIndexerFlagParser")]
public sealed class TorznabIndexerFlagParserTests : BaseTests
{
    [Theory]
    [InlineData("0", "freeleech")]
    [InlineData("0.0", "freeleech")]
    [InlineData("0.25", "freeleech75")]
    [InlineData("0.5", "halfleech")]
    [InlineData("0.75", "freeleech25")]
    public void Parse_MapsDownloadVolumeFactorToTheSharedArrNames(string factor, string expected)
    {
        var flags = TorznabIndexerFlagParser.Parse([new("downloadvolumefactor", factor)]);

        Assert.Equal([expected], flags);
    }

    [Fact]
    public void Parse_ReturnsNothingForAnOrdinaryPaidRelease()
    {
        var flags = TorznabIndexerFlagParser.Parse(
            [new("downloadvolumefactor", "1"), new("uploadvolumefactor", "1")]);

        Assert.Empty(flags);
    }

    [Fact]
    public void Parse_ReadsDoubleUploadFromUploadVolumeFactor()
    {
        var flags = TorznabIndexerFlagParser.Parse([new("uploadvolumefactor", "2.0")]);

        Assert.Equal(["doubleupload"], flags);
    }

    [Fact]
    public void Parse_ReadsInternalAndSceneFromTags()
    {
        var flags = TorznabIndexerFlagParser.Parse([new("tag", "Internal"), new("tag", "SCENE")]);

        Assert.Equal(["internal", "scene"], flags);
    }

    [Fact]
    public void Parse_CombinesFactorsAndTags()
    {
        var flags = TorznabIndexerFlagParser.Parse(
        [
            new("downloadvolumefactor", "0"),
            new("uploadvolumefactor", "2"),
            new("tag", "internal")
        ]);

        Assert.Equal(["freeleech", "doubleupload", "internal"], flags);
    }

    [Fact]
    public void Parse_IgnoresAttributeNameCasing()
    {
        var flags = TorznabIndexerFlagParser.Parse([new("downloadVolumeFactor", "0")]);

        Assert.Equal(["freeleech"], flags);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    public void Parse_TreatsAnUnreadableFactorAsAbsent(string factor)
    {
        var flags = TorznabIndexerFlagParser.Parse([new("downloadvolumefactor", factor)]);

        Assert.Empty(flags);
    }

    [Fact]
    public void Parse_IgnoresUnrelatedAttributes()
    {
        var flags = TorznabIndexerFlagParser.Parse(
            [new("seeders", "12"), new("size", "1234"), new("tag", "remux")]);

        Assert.Empty(flags);
    }
}
