/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using System.Net;
using Listenarr.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Tests.Features.Api.Features.Search;

/// <summary>
/// The Audible catalog search is the one caller of the raising client that should keep failing
/// rather than degrade: an empty result list is a claim about the catalog, and a provider that
/// did not answer has made no such claim. It must fail as a status the caller can act on,
/// though, and without handing back the URL that was asked.
/// </summary>
[Trait("Area", "Search")]
[Trait("Name", "SearchController_ProviderUnavailableTests")]
[Trait("Category", "Api")]
public sealed class SearchController_ProviderUnavailableTests : BaseTests
{
    private static SearchController Controller(HttpStatusCode status, out HttpClient client)
    {
        client = new HttpClient(new DelegatingHandlerMock((_, _) =>
            Task.FromResult(new HttpResponseMessage(status)))
        {
            InnerHandler = new HttpClientHandler()
        });

        return new SearchController(
            Mock.Of<ISearchService>(),
            Mock.Of<ILogger<SearchController>>(),
            new AudibleService(client, Mock.Of<ILogger<AudibleService>>()),
            Mock.Of<IAudiobookMetadataService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Fact]
    [Trait("Scenario", "ThrottledProviderIsAServiceStatus")]
    public async Task SearchAudible_Returns503_WhenTheProviderAsksForLessTraffic()
    {
        var controller = Controller(HttpStatusCode.TooManyRequests, out var client);
        using var _ = client;

        var result = await controller.SearchAudible("a voyage downriver");

        // A 500 said the fault was ours and gave the caller nothing to act on. Returning an
        // empty result set, which is what the client did before it learned to raise, said the
        // catalog has no such book.
        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status.StatusCode);

        // And the body is the service's own fixed text. The message the client raises used to
        // be the request URL, search terms and all.
        var body = Assert.IsType<string>(status.Value);
        Assert.DoesNotContain("http", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("voyage", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Scenario", "AnEmptyCatalogIsStillNotFound")]
    public async Task SearchAudible_DoesNotReport503_WhenTheProviderAnsweredWithNothing()
    {
        // The control. A provider that answers and has nothing must not be reported as an
        // outage, or the 503 above says nothing at all.
        var controller = Controller(HttpStatusCode.NotFound, out var client);
        using var _ = client;

        var result = await controller.SearchAudible("a voyage downriver");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<AudibleSearchResponse>(ok.Value);
        Assert.Empty(payload.Results);
    }
}
