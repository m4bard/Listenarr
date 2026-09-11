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

namespace Listenarr.Tests.Features.Api.Features.Search;

/// <summary>
/// The Audible catalog search is the one caller of a raising client that should keep failing
/// rather than degrade: an empty result list is a claim about the catalog, and a provider that
/// did not answer has made no such claim. It must fail as a status the caller can act on,
/// though, and without handing back the URL that was asked.
/// </summary>
[Trait("Area", "Search")]
[Trait("Name", "SearchController_ProviderUnavailableTests")]
[Trait("Category", "Api")]
public sealed class SearchController_ProviderUnavailableTests : BaseTests
{
    private static SearchController Controller(Mock<AudibleService> audible)
    {
        return new SearchController(
            Mock.Of<ISearchService>(),
            Mock.Of<ILogger<SearchController>>(),
            audible.Object,
            Mock.Of<IAudiobookMetadataService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static Mock<AudibleService> Audible(out HttpClient client)
    {
        client = new HttpClient();
        return new Mock<AudibleService>(client, Mock.Of<ILogger<AudibleService>>());
    }

    [Theory]
    [Trait("Scenario", "ProviderFaultIsAServiceStatus")]
    [InlineData("throttled")]
    [InlineData("transport")]
    public async Task SearchAudible_Returns503_WhenTheProviderDidNotAnswer(string fault)
    {
        Exception raised = fault == "throttled"
            ? new MetadataProviderThrottledException("the provider asked for less traffic", TimeSpan.FromSeconds(30))
            : new HttpRequestException("the request timed out");

        var audible = Audible(out var client);
        using var _ = client;
        audible
            .Setup(service => service.SearchBooksAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ThrowsAsync(raised);

        var result = await Controller(audible).SearchAudible("a voyage downriver");

        // A 500 said the fault was ours and gave the caller nothing to act on.
        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status.StatusCode);

        // And the body is the service's own fixed text. A client that raises composes its
        // message from the request it made, search terms and all.
        var body = Assert.IsType<string>(status.Value);
        Assert.DoesNotContain("http", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("voyage", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Scenario", "AnEmptyCatalogIsStillNotAnOutage")]
    public async Task SearchAudible_DoesNotReport503_WhenTheProviderAnsweredWithNothing()
    {
        // The control. A provider that answers and has nothing must not be reported as an
        // outage, or the 503 above says nothing at all.
        var audible = Audible(out var client);
        using var _ = client;
        audible
            .Setup(service => service.SearchBooksAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(new AudibleSearchResponse { Results = [] });

        var result = await Controller(audible).SearchAudible("a voyage downriver");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsType<AudibleSearchResponse>(ok.Value).Results);
    }

    [Fact]
    [Trait("Scenario", "ANullSearchResponseIsStillNotFound")]
    public async Task SearchAudible_Returns404_WhenTheClientHandsBackNothingAtAll()
    {
        // The other control, and the one the empty-list case does not cover. The client hands
        // back null for a search it could not turn into a result at all, and this endpoint has
        // always answered that as a 404. Raising on everything would satisfy the 503 above and
        // quietly take this branch with it.
        var audible = Audible(out var client);
        using var _ = client;
        audible
            .Setup(service => service.SearchBooksAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((AudibleSearchResponse?)null);

        var result = await Controller(audible).SearchAudible("a voyage downriver");

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("No results found", notFound.Value);
    }
}
