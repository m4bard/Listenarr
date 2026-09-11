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
/// The ISBN endpoint is where a person meets the lookup service, and the lookup service was
/// taught to raise so the scheduled refresh could tell "no such ISBN" from "nobody answered".
/// A person is owed the same distinction, as a status code rather than as a stack trace.
/// </summary>
[Trait("Area", "Metadata")]
[Trait("Name", "MetadataController_AsinFromIsbnTests")]
[Trait("Category", "Api")]
public sealed class MetadataController_AsinFromIsbnTests : BaseTests
{
    private static readonly HttpClient SharedAudibleHttpClient = new();

    private static MetadataController CreateController(
        IAsinLookupService asinLookup,
        MemoryCache memoryCache)
    {
        return new MetadataController(
            Mock.Of<IAudiobookMetadataService>(),
            new Mock<AudibleService>(SharedAudibleHttpClient, Mock.Of<ILogger<AudibleService>>()) { CallBase = false }.Object,
            Mock.Of<IAudnexusService>(),
            Mock.Of<IImageCacheService>(),
            memoryCache,
            Mock.Of<IAudiobookRepository>(),
            asinLookup,
            Mock.Of<IAuthorCatalogService>(),
            Mock.Of<ISeriesCatalogService>(),
            Mock.Of<ILogger<MetadataController>>());
    }

    [Theory]
    [Trait("Scenario", "ProviderFaultIsAServiceStatus")]
    [InlineData("throttled")]
    [InlineData("transport")]
    public async Task GetAsinFromIsbn_Returns503_WhenTheProviderDidNotAnswer(string fault)
    {
        Exception raised = fault == "throttled"
            ? new MetadataProviderThrottledException("slow down", TimeSpan.FromSeconds(30))
            : new HttpRequestException("Audible API request timed out");

        var asinLookup = new Mock<IAsinLookupService>();
        asinLookup
            .Setup(service => service.GetAsinFromIsbnAsync("9780000000001", It.IsAny<CancellationToken>()))
            .ThrowsAsync(raised);
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var result = await CreateController(asinLookup.Object, memoryCache)
            .GetAsinFromIsbn("9780000000001", CancellationToken.None);

        // Not a 500, and not the 404 it used to be either. A 404 told the caller the ISBN has
        // no ASIN, which is a claim nothing had established.
        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status.StatusCode);

        // The body keeps the { success, error } shape the callers already read, and says
        // nothing about which host was asked or what it was asked for.
        var payload = Assert.IsType<string>(status.Value?.GetType().GetProperty("error")?.GetValue(status.Value));
        Assert.False((bool)status.Value!.GetType().GetProperty("success")!.GetValue(status.Value)!);
        Assert.DoesNotContain("http", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("audible", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Scenario", "AnAbsentAsinIsStillNotFound")]
    public async Task GetAsinFromIsbn_Returns404_WhenTheProviderAnsweredAndHadNothing()
    {
        var asinLookup = new Mock<IAsinLookupService>();
        asinLookup
            .Setup(service => service.GetAsinFromIsbnAsync("9780000000002", It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, null, "ASIN not found for ISBN"));
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var result = await CreateController(asinLookup.Object, memoryCache)
            .GetAsinFromIsbn("9780000000002", CancellationToken.None);

        // The half of the old behaviour that was right stays right: a provider that answered
        // and had nothing is a 404, and the new 503 must not swallow it.
        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(
            "ASIN not found for ISBN",
            notFound.Value?.GetType().GetProperty("error")?.GetValue(notFound.Value));
    }
}
