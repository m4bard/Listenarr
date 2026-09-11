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

namespace Listenarr.Tests.Features.Api.Features.Images;

/// <summary>
/// Cover art is decoration, and the image endpoint has always degraded to the placeholder when
/// a provider would not answer. Teaching the Audible client to raise on a 429 must not turn a
/// rate-limited provider into a broken-looking library page.
/// </summary>
[Trait("Area", "Images")]
[Trait("Name", "ImagesController_ThrottledProviderPlaceholderTests")]
[Trait("Category", "Api")]
public sealed class ImagesController_ThrottledProviderPlaceholderTests : BaseTests
{
    [Fact]
    [Trait("Scenario", "ThrottledProviderStillServesThePlaceholder")]
    public async Task GetImage_ReturnsThePlaceholder_WhenTheProviderAsksForLessTraffic()
    {
        const string identifier = "B000APXZHK";

        var imageCache = new Mock<IImageCacheService>();
        imageCache.Setup(m => m.GetCachedImagePathAsync(identifier)).ReturnsAsync((string?)null);

        // The shape the client now raises. Before the fix it was not in the recoverable list,
        // so it escaped every catch in the candidate walk and left the request unhandled.
        var metadataService = new Mock<IAudiobookMetadataService>();
        metadataService
            .Setup(m => m.GetAudibleMetadataAsync(identifier, It.IsAny<string>(), It.IsAny<bool>()))
            .ThrowsAsync(new MetadataProviderThrottledException("slow down", TimeSpan.FromSeconds(30)));
        metadataService
            .Setup(m => m.GetMetadataAsync(identifier, It.IsAny<string>(), It.IsAny<bool>()))
            .ThrowsAsync(new MetadataProviderThrottledException("slow down", TimeSpan.FromSeconds(30)));

        using var httpClientForAudible = new HttpClient();
        var audible = new Mock<AudibleService>(httpClientForAudible, Mock.Of<ILogger<AudibleService>>());
        audible
            .Setup(m => m.LookupAuthorAsync(identifier, It.IsAny<string>()))
            .ThrowsAsync(new MetadataProviderThrottledException("slow down", TimeSpan.FromSeconds(30)));

        var audnexus = new Mock<IAudnexusService>();
        audnexus
            .Setup(m => m.GetBookMetadataAsync(identifier, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync((AudnexusBookResponse?)null);
        audnexus
            .Setup(m => m.GetAuthorAsync(identifier, It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync((AudnexusAuthorResponse?)null);
        audnexus
            .Setup(m => m.SearchAuthorsAsync(identifier, It.IsAny<string>()))
            .ReturnsAsync(new List<AudnexusAuthorSearchResult>());

        var repo = new Mock<IAudiobookRepository>();
        repo.Setup(r => r.GetByAsinAsync(identifier)).ReturnsAsync((Audiobook?)null);
        repo.Setup(r => r.GetAuthorAsinByNameAsync(identifier)).ReturnsAsync((string?)null);

        var tempRoot = Path.Join(Path.GetTempPath(), "listenarr_test_contentroot_throttled_placeholder");
        Directory.CreateDirectory(tempRoot);

        var pathService = new Mock<IApplicationPathService>();
        pathService.SetupGet(p => p.ContentRootPath).Returns(tempRoot);

        var controller = new ImagesController(
            imageCache.Object,
            metadataService.Object,
            audible.Object,
            audnexus.Object,
            repo.Object,
            Mock.Of<ILogger<ImagesController>>(),
            pathService.Object,
            new LocalFileSystem())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.GetImage(identifier);

        Assert.True(
            result is PhysicalFileResult physical
                && physical.FileName.EndsWith("placeholder.svg", StringComparison.OrdinalIgnoreCase)
            || result is RedirectResult redirect && redirect.Url == "/placeholder.svg",
            $"Expected the placeholder, got {result.GetType().Name}");
    }
}
