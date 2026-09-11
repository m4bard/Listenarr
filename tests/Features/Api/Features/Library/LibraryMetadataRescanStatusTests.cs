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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Tests.Features.Api.Features.Library;

/// <summary>
/// What the one-book rescan endpoint says for each refresh outcome. The outcomes a person sees
/// are not the outcomes the run ledger counts: two of them mean nothing was asked, and the
/// endpoint has to tell a caller which of the two it was without claiming something untrue
/// about their book.
/// </summary>
[Trait("Area", "Library")]
[Trait("Name", "LibraryMetadataRescanStatusTests")]
[Trait("Category", "Api")]
public sealed class LibraryMetadataRescanStatusTests : BaseTests
{
    private static async Task<IActionResult> RescanAsync(MetadataRefreshOutcome outcome)
    {
        var repository = new Mock<IAudiobookRepository>();
        repository
            .Setup(r => r.GetByIdSnapshotAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Audiobook { Id = 7, Title = "A Voyage Downriver" });

        var refresh = new Mock<IMetadataRefreshService>();
        refresh
            .Setup(service => service.RefreshAsync(
                7, It.IsAny<IMetadataRefreshBudget>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataRefreshResult(outcome, 0));

        var workflow = new LibraryMetadataRescanWorkflow(
            refresh.Object,
            repository.Object,
            Mock.Of<ILogger<LibraryMetadataRescanWorkflow>>());

        return await workflow.RescanAsync(7, new DefaultHttpContext());
    }

    private static string? MessageOf(object? payload) =>
        payload?.GetType().GetProperty("message")?.GetValue(payload) as string;

    [Fact]
    [Trait("Scenario", "UnusableIdentifiersAreNotMissingIdentifiers")]
    public async Task Rescan_Returns404_WhenTheBooksIdentifiersCouldNotBeUsed()
    {
        var result = await RescanAsync(MetadataRefreshOutcome.Unusable);

        // The book carries identifiers; none of them could be turned into a question. A 400
        // saying no identifiers are available sends the operator to look at a field that is
        // already filled in, and this endpoint answered the case as a 404 before the refresh
        // outcomes existed.
        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("No metadata found using the available identifiers.", MessageOf(notFound.Value));
    }

    [Fact]
    [Trait("Scenario", "NoIdentifiersAtAllIsStillABadRequest")]
    public async Task Rescan_Returns400_WhenTheBookCarriesNoIdentifiersAtAll()
    {
        // The control. The 400 exists for a real case and has to keep answering it, or the 404
        // above is just a change of message.
        var result = await RescanAsync(MetadataRefreshOutcome.Skipped);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(
            "No ASIN or ISBN identifiers are available for metadata rescan.",
            MessageOf(badRequest.Value));
    }

    [Fact]
    [Trait("Scenario", "AProviderVerdictOfNothingIsAlsoNotFound")]
    public async Task Rescan_Returns404_WhenEverySourceAnsweredAndNoneHadTheBook()
    {
        var result = await RescanAsync(MetadataRefreshOutcome.NotFound);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("No metadata found using the available identifiers.", MessageOf(notFound.Value));
    }
}
