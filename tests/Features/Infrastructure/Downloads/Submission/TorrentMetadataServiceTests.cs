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

namespace Listenarr.Tests.Features.Infrastructure.Downloads.Submission;

[Trait("Name", "TorrentMetadataServiceTests")]
[Trait("Category", "SeedCriteria")]
public sealed class TorrentMetadataServiceTests : BaseTests
{
    [Fact]
    public void Prepare_CarriesTheGrabbingIndexerIdOntoThePreparedSubmission()
    {
        var searchResult = new SearchResult
        {
            Title = "Book",
            MagnetLink = "magnet:?xt=urn:btih:ABCDEF1234567890ABCDEF1234567890ABCDEF12",
            IndexerId = 42
        };
        var candidate = TrustedDownloadCandidateFactory.Create(searchResult);

        var prepared = new TorrentMetadataService().Prepare(
            candidate,
            torrentBytes: null,
            magnetUri: searchResult.MagnetLink,
            originalLocator: searchResult.MagnetLink);

        Assert.Equal(42, prepared.IndexerId);
    }

    [Fact]
    public void Prepare_WhenSearchResultHasNoIndexerId_LeavesItNull()
    {
        var searchResult = new SearchResult
        {
            Title = "Book",
            MagnetLink = "magnet:?xt=urn:btih:ABCDEF1234567890ABCDEF1234567890ABCDEF12"
        };
        var candidate = TrustedDownloadCandidateFactory.Create(searchResult);

        var prepared = new TorrentMetadataService().Prepare(
            candidate,
            torrentBytes: null,
            magnetUri: searchResult.MagnetLink,
            originalLocator: searchResult.MagnetLink);

        Assert.Null(prepared.IndexerId);
    }
}
