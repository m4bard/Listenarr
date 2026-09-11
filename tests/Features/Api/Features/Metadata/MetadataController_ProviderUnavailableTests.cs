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
using Microsoft.Extensions.Caching.Memory;

namespace Listenarr.Tests.Features.Api.Features.Metadata;

/// <summary>
/// The two ASIN endpoints, held to the same three answers as the ISBN one beside them. A
/// provider that would not answer is not a missing book and is not a fault in this service, and
/// what the endpoint says about the failure must not be the client's own message.
/// </summary>
[Trait("Area", "Metadata")]
[Trait("Name", "MetadataController_ProviderUnavailableTests")]
[Trait("Category", "Api")]
public sealed class MetadataController_ProviderUnavailableTests : BaseTests
{
    private static readonly HttpClient SharedAudibleHttpClient = new();

    // Everything an endpoint must not repeat back: the host it asked, the path it asked for,
    // and the identifier the query string was built from.
    private const string LeakyClientMessage =
        "GET https://api.audible.com/1.0/catalog/products/B0LEAKYAAA?response_groups=media failed";

    private static MetadataController CreateController(
        IAudiobookMetadataService metadataService,
        MemoryCache memoryCache)
    {
        return new MetadataController(
            metadataService,
            new Mock<AudibleService>(SharedAudibleHttpClient, Mock.Of<ILogger<AudibleService>>()) { CallBase = false }.Object,
            Mock.Of<IAudnexusService>(),
            Mock.Of<IImageCacheService>(),
            memoryCache,
            Mock.Of<IAudiobookRepository>(),
            Mock.Of<IAsinLookupService>(),
            Mock.Of<IAuthorCatalogService>(),
            Mock.Of<ISeriesCatalogService>(),
            Mock.Of<ILogger<MetadataController>>());
    }

    private static Exception Fault(string shape) => shape == "throttled"
        ? new MetadataProviderThrottledException("the provider asked for less traffic", TimeSpan.FromSeconds(30))
        : new HttpRequestException("the request timed out");

    [Theory]
    [Trait("Scenario", "ProviderFaultIsAServiceStatus")]
    [InlineData("throttled")]
    [InlineData("transport")]
    public async Task GetMetadata_Returns503_WhenTheProviderDidNotAnswer(string shape)
    {
        var metadataService = new Mock<IAudiobookMetadataService>();
        metadataService
            .Setup(service => service.GetMetadataAsync("B0THROTTLD", It.IsAny<string>(), It.IsAny<bool>()))
            .ThrowsAsync(Fault(shape));
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var result = await CreateController(metadataService.Object, memoryCache).GetMetadata("B0THROTTLD");

        // A 500 said the fault was ours and gave the caller nothing to act on. Retrying is the
        // right move for exactly one of the two, and the status is what says which.
        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status.StatusCode);
    }

    [Theory]
    [Trait("Scenario", "ProviderFaultIsAServiceStatus")]
    [InlineData("throttled")]
    [InlineData("transport")]
    public async Task GetAudibleMetadata_Returns503_WhenTheProviderDidNotAnswer(string shape)
    {
        var metadataService = new Mock<IAudiobookMetadataService>();
        metadataService
            .Setup(service => service.GetAudibleMetadataAsync("B0THROTTLD", It.IsAny<string>(), It.IsAny<bool>()))
            .ThrowsAsync(Fault(shape));
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var result = await CreateController(metadataService.Object, memoryCache).GetAudibleMetadata("B0THROTTLD");

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status.StatusCode);
    }

    [Fact]
    [Trait("Scenario", "TheClientsMessageStaysOutOfTheBody")]
    public async Task GetMetadata_Returns500_WithoutRepeatingTheClientsMessage()
    {
        var metadataService = new Mock<IAudiobookMetadataService>();
        metadataService
            .Setup(service => service.GetMetadataAsync("B0LEAKYAAA", It.IsAny<string>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException(LeakyClientMessage));
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var result = await CreateController(metadataService.Object, memoryCache).GetMetadata("B0LEAKYAAA");

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);

        // This endpoint used to answer with "Error fetching metadata: " and the exception's own
        // text, which on this path is the client's, composed from the request it made.
        var body = Assert.IsType<string>(status.Value);
        Assert.Equal("Error fetching metadata", body);
        Assert.DoesNotContain("audible.com", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("B0LEAKYAAA", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Scenario", "TheClientsMessageStaysOutOfTheBody")]
    public async Task GetAudibleMetadata_Returns500_WithoutRepeatingTheClientsMessage()
    {
        var metadataService = new Mock<IAudiobookMetadataService>();
        metadataService
            .Setup(service => service.GetAudibleMetadataAsync("B0LEAKYAAA", It.IsAny<string>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException(LeakyClientMessage));
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var result = await CreateController(metadataService.Object, memoryCache).GetAudibleMetadata("B0LEAKYAAA");

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);

        var body = Assert.IsType<string>(status.Value);
        Assert.DoesNotContain("audible.com", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("B0LEAKYAAA", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [Trait("Scenario", "AnAbsentBookIsStillNotFound")]
    [InlineData("plain")]
    [InlineData("audible")]
    public async Task GetMetadata_Returns404_WhenTheProviderAnsweredAndHadNothing(string endpoint)
    {
        var metadataService = new Mock<IAudiobookMetadataService>();
        metadataService
            .Setup(service => service.GetMetadataAsync("B0MISSINGX", It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync((AudiobookMetadataEnvelope?)null);
        metadataService
            .Setup(service => service.GetAudibleMetadataAsync("B0MISSINGX", It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync((AudibleBookResponse?)null);
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var controller = CreateController(metadataService.Object, memoryCache);

        var result = endpoint == "plain"
            ? (await controller.GetMetadata("B0MISSINGX")).Result
            : (await controller.GetAudibleMetadata("B0MISSINGX")).Result;

        // The control for both statuses above. A provider that answered and had nothing is a
        // 404, and neither the 503 nor the reworded 500 may swallow it.
        Assert.IsType<NotFoundObjectResult>(result);
    }
}
