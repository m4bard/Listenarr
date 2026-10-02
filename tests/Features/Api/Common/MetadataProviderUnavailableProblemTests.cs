/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Api.Filters;
using Listenarr.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Listenarr.Tests.Features.Api.Common;

/// <summary>
/// Every endpoint that answers 503 because the metadata provider did not answer, read the way a
/// caller reads it: after ServerErrorProblemDetailsFilter has run. The filter rewrites any result
/// of 500 or above that is not already a ProblemDetails into a generic internal_error and, outside
/// Development, drops the detail. A 503 whose body was a string or an anonymous object therefore
/// reached the caller as "Internal server error", which hid the one thing the 503 exists to say.
/// </summary>
[Trait("Area", "Metadata")]
[Trait("Name", "MetadataProviderUnavailableProblemTests")]
[Trait("Category", "Api")]
public sealed class MetadataProviderUnavailableProblemTests : BaseTests
{
    private const string ExpectedTitle = "Metadata provider unavailable";
    private const string ExpectedCode = "metadata_provider_unavailable";

    private static readonly HttpClient SharedAudibleHttpClient = new();

    private static Exception Fault(string shape) => shape == "throttled"
        ? new MetadataProviderThrottledException("the provider asked for less traffic", TimeSpan.FromSeconds(30))
        : new HttpRequestException("GET https://api.audible.com/1.0/catalog/products/B0THROTTLD failed");

    private static MetadataController MetadataControllerWith(
        IAudiobookMetadataService metadataService,
        IAsinLookupService asinLookup,
        MemoryCache memoryCache,
        HttpContext httpContext)
    {
        return new MetadataController(
            metadataService,
            new Mock<AudibleService>(SharedAudibleHttpClient, Mock.Of<ILogger<AudibleService>>()) { CallBase = false }.Object,
            Mock.Of<IAudnexusService>(),
            Mock.Of<IImageCacheService>(),
            memoryCache,
            Mock.Of<IAudiobookRepository>(),
            asinLookup,
            Mock.Of<IAuthorCatalogService>(),
            Mock.Of<ISeriesCatalogService>(),
            Mock.Of<ILogger<MetadataController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    /// <summary>Runs a result through the production filter, as the pipeline does.</summary>
    private static async Task<ObjectResult> ThroughFilterAsync(IActionResult result, HttpContext httpContext)
    {
        var filter = new ServerErrorProblemDetailsFilter(new TestHostEnvironment(Environments.Production));
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var context = new ResultExecutingContext(actionContext, [], result, controller: new object());

        await filter.OnResultExecutionAsync(
            context,
            () => Task.FromResult(new ResultExecutedContext(actionContext, [], context.Result, controller: new object())));

        return Assert.IsType<ObjectResult>(context.Result);
    }

    private static void AssertProviderProblem(ObjectResult result, HttpContext httpContext, string shape)
    {
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.Status);
        Assert.Equal(ExpectedTitle, problem.Title);
        Assert.Equal(ExpectedCode, problem.Extensions["code"]);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
        Assert.Contains("application/problem+json", result.ContentTypes);

        // Fixed text only. The client composes its exception message from the request it made.
        Assert.DoesNotContain("http", problem.Detail!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("B0THROTTLD", problem.Detail!, StringComparison.OrdinalIgnoreCase);

        // The wait the provider named is passed on, and nothing is invented when it named none.
        if (shape == "throttled")
        {
            Assert.Equal("30", httpContext.Response.Headers.RetryAfter.ToString());
            Assert.Equal(30, problem.Extensions["retryAfterSeconds"]);
        }
        else
        {
            Assert.False(httpContext.Response.Headers.ContainsKey("Retry-After"));
            Assert.False(problem.Extensions.ContainsKey("retryAfterSeconds"));
        }
    }

    [Theory]
    [Trait("Scenario", "ProviderOutageSurvivesTheServerErrorFilter")]
    [InlineData("throttled")]
    [InlineData("transport")]
    public async Task GetMetadata_503BodyNamesTheProvider_AfterTheFilter(string shape)
    {
        var metadataService = new Mock<IAudiobookMetadataService>();
        metadataService
            .Setup(service => service.GetMetadataAsync("B0THROTTLD", It.IsAny<string>(), It.IsAny<bool>()))
            .ThrowsAsync(Fault(shape));
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var httpContext = new DefaultHttpContext();

        var result = await MetadataControllerWith(metadataService.Object, Mock.Of<IAsinLookupService>(), memoryCache, httpContext)
            .GetMetadata("B0THROTTLD");

        AssertProviderProblem(await ThroughFilterAsync(result.Result!, httpContext), httpContext, shape);
    }

    [Theory]
    [Trait("Scenario", "ProviderOutageSurvivesTheServerErrorFilter")]
    [InlineData("throttled")]
    [InlineData("transport")]
    public async Task GetAudibleMetadata_503BodyNamesTheProvider_AfterTheFilter(string shape)
    {
        var metadataService = new Mock<IAudiobookMetadataService>();
        metadataService
            .Setup(service => service.GetAudibleMetadataAsync("B0THROTTLD", It.IsAny<string>(), It.IsAny<bool>()))
            .ThrowsAsync(Fault(shape));
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var httpContext = new DefaultHttpContext();

        var result = await MetadataControllerWith(metadataService.Object, Mock.Of<IAsinLookupService>(), memoryCache, httpContext)
            .GetAudibleMetadata("B0THROTTLD");

        AssertProviderProblem(await ThroughFilterAsync(result.Result!, httpContext), httpContext, shape);
    }

    [Theory]
    [Trait("Scenario", "ProviderOutageSurvivesTheServerErrorFilter")]
    [InlineData("throttled")]
    [InlineData("transport")]
    public async Task GetAsinFromIsbn_503BodyNamesTheProvider_AfterTheFilter(string shape)
    {
        var asinLookup = new Mock<IAsinLookupService>();
        asinLookup
            .Setup(service => service.GetAsinFromIsbnAsync("9780000000001", It.IsAny<CancellationToken>()))
            .ThrowsAsync(Fault(shape));
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var httpContext = new DefaultHttpContext();

        var result = await MetadataControllerWith(Mock.Of<IAudiobookMetadataService>(), asinLookup.Object, memoryCache, httpContext)
            .GetAsinFromIsbn("9780000000001", CancellationToken.None);

        var filtered = await ThroughFilterAsync(result, httpContext);
        AssertProviderProblem(filtered, httpContext, shape);

        // The ISBN endpoint's other answers carry { success, error }, and this one still does.
        var problem = Assert.IsType<ProblemDetails>(filtered.Value);
        Assert.Equal(false, problem.Extensions["success"]);
        Assert.Equal(problem.Detail, problem.Extensions["error"]);
    }

    [Theory]
    [Trait("Scenario", "ProviderOutageSurvivesTheServerErrorFilter")]
    [InlineData("throttled")]
    [InlineData("transport")]
    public async Task SearchAudible_503BodyNamesTheProvider_AfterTheFilter(string shape)
    {
        using var client = new HttpClient();
        var audible = new Mock<AudibleService>(client, Mock.Of<ILogger<AudibleService>>());
        audible
            .Setup(service => service.SearchBooksAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ThrowsAsync(Fault(shape));
        var httpContext = new DefaultHttpContext();
        var controller = new SearchController(
            Mock.Of<ISearchService>(),
            Mock.Of<ILogger<SearchController>>(),
            audible.Object,
            Mock.Of<IAudiobookMetadataService>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var result = await controller.SearchAudible("a voyage downriver");

        AssertProviderProblem(await ThroughFilterAsync(result.Result!, httpContext), httpContext, shape);
    }

    [Fact]
    [Trait("Scenario", "AGenuineFaultIsStillAnInternalError")]
    public async Task GetMetadata_500IsStillRewrittenAsAnInternalError()
    {
        // The control. Only the provider's silence gets its own title; a fault in this service
        // must still come out of the filter as the generic internal_error, or the assertions
        // above could pass because the filter had stopped doing anything at all.
        var metadataService = new Mock<IAudiobookMetadataService>();
        metadataService
            .Setup(service => service.GetMetadataAsync("B0BROKENXX", It.IsAny<string>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("something in this service broke"));
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var httpContext = new DefaultHttpContext();

        var result = await MetadataControllerWith(metadataService.Object, Mock.Of<IAsinLookupService>(), memoryCache, httpContext)
            .GetMetadata("B0BROKENXX");

        var filtered = await ThroughFilterAsync(result.Result!, httpContext);
        Assert.Equal(StatusCodes.Status500InternalServerError, filtered.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(filtered.Value);
        Assert.Equal("Internal server error", problem.Title);
        Assert.Equal("internal_error", problem.Extensions["code"]);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Listenarr.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
