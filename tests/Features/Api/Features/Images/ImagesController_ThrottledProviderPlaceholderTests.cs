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
/// a provider would not answer. A client that raises rather than returning null must not turn
/// a rate-limited provider into a broken-looking library page.
/// </summary>
[Trait("Area", "Images")]
[Trait("Name", "ImagesController_ThrottledProviderPlaceholderTests")]
[Trait("Category", "Api")]
public sealed class ImagesController_ThrottledProviderPlaceholderTests : BaseTests, IDisposable
{
    private const string Identifier = "B000APXZHK";

    // A directory of its own per test class instance, removed on the way out. The fixed name
    // this used to build was shared by every run on the machine and was never cleaned up.
    private readonly string _contentRoot = Path.Join(
        Path.GetTempPath(),
        $"listenarr_test_contentroot_{Guid.NewGuid():N}");

    public ImagesController_ThrottledProviderPlaceholderTests()
    {
        Directory.CreateDirectory(_contentRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover directory is not worth failing a green test over.
        }
    }

    private static MetadataProviderThrottledException Pushback() =>
        new("the provider asked for less traffic", TimeSpan.FromSeconds(30));

    private ImagesController CreateController(
        IImageCacheService imageCache,
        IAudiobookMetadataService metadataService,
        IAudnexusService audnexus,
        AudibleService audible,
        IAudiobookRepository repository)
    {
        var pathService = new Mock<IApplicationPathService>();
        pathService.SetupGet(p => p.ContentRootPath).Returns(_contentRoot);

        return new ImagesController(
            imageCache,
            metadataService,
            audible,
            audnexus,
            repository,
            Mock.Of<ILogger<ImagesController>>(),
            pathService.Object,
            new LocalFileSystem())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static Mock<IAudiobookRepository> EmptyLibrary()
    {
        var repository = new Mock<IAudiobookRepository>();
        repository.Setup(r => r.GetByAsinAsync(Identifier)).ReturnsAsync((Audiobook?)null);
        repository.Setup(r => r.GetAuthorAsinByNameAsync(Identifier)).ReturnsAsync((string?)null);
        return repository;
    }

    [Fact]
    [Trait("Scenario", "ThrottledProviderStillServesThePlaceholder")]
    public async Task GetImage_ReturnsThePlaceholder_WhenTheFirstProviderAsksForLessTraffic()
    {
        var imageCache = new Mock<IImageCacheService>();
        imageCache.Setup(m => m.GetCachedImagePathAsync(Identifier)).ReturnsAsync((string?)null);

        // The first metadata call the walk makes sits outside any local try, so a raised fault
        // unwinds the whole walk to the catch at the bottom of TryResolveAsync rather than
        // carrying on to the next candidate. That is fine here and it is why this test exists:
        // whichever way it unwinds, the answer owed to the page is the placeholder and not an
        // error where the book's art should be.
        var metadataService = new Mock<IAudiobookMetadataService>();
        metadataService
            .Setup(m => m.GetAudibleMetadataAsync(Identifier, It.IsAny<string>(), It.IsAny<bool>()))
            .ThrowsAsync(Pushback());
        metadataService
            .Setup(m => m.GetMetadataAsync(Identifier, It.IsAny<string>(), It.IsAny<bool>()))
            .ThrowsAsync(Pushback());

        using var httpClientForAudible = new HttpClient();
        var audible = new Mock<AudibleService>(httpClientForAudible, Mock.Of<ILogger<AudibleService>>());
        audible
            .Setup(m => m.LookupAuthorAsync(Identifier, It.IsAny<string>()))
            .ThrowsAsync(Pushback());

        var audnexus = new Mock<IAudnexusService>();
        audnexus
            .Setup(m => m.GetBookMetadataAsync(Identifier, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync((AudnexusBookResponse?)null);
        audnexus
            .Setup(m => m.GetAuthorAsync(Identifier, It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync((AudnexusAuthorResponse?)null);
        audnexus
            .Setup(m => m.SearchAuthorsAsync(Identifier, It.IsAny<string>()))
            .ReturnsAsync(new List<AudnexusAuthorSearchResult>());

        var controller = CreateController(
            imageCache.Object,
            metadataService.Object,
            audnexus.Object,
            audible.Object,
            EmptyLibrary().Object);

        var result = await controller.GetImage(Identifier);

        Assert.True(
            result is PhysicalFileResult physical
                && physical.FileName.EndsWith("placeholder.svg", StringComparison.OrdinalIgnoreCase)
            || result is RedirectResult redirect && redirect.Url == "/placeholder.svg",
            $"Expected the placeholder, got {result.GetType().Name}");
    }

    [Fact]
    [Trait("Scenario", "OneThrottledProviderDoesNotCostAnothersCover")]
    public async Task GetImage_StillServesTheCover_WhenOneProviderIsThrottledAndAnotherHasOne()
    {
        const string coverUrl = "https://m.media-amazon.com/images/I/cover-from-audible.jpg";
        const string cachedRelativePath = "cache/images/B000APXZHK.jpg";

        var cachedFullPath = Path.Join(_contentRoot, "cache", "images", $"{Identifier}.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(cachedFullPath)!);
        await File.WriteAllTextAsync(cachedFullPath, "not really a jpeg");

        var imageCache = new Mock<IImageCacheService>();
        imageCache
            .SetupSequence(m => m.GetCachedImagePathAsync(Identifier))
            .ReturnsAsync((string?)null)
            .ReturnsAsync(cachedRelativePath);
        imageCache
            .Setup(m => m.DownloadAndCacheImageAsync(coverUrl, Identifier))
            .ReturnsAsync(cachedRelativePath);

        // Audible answers with a cover. Audnexus, asked next for a second candidate, pushes
        // back from inside the try that guards it.
        var metadataService = new Mock<IAudiobookMetadataService>();
        metadataService
            .Setup(m => m.GetAudibleMetadataAsync(Identifier, It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(new AudibleBookResponse { Asin = Identifier, ImageUrl = coverUrl });

        var audnexus = new Mock<IAudnexusService>();
        audnexus
            .Setup(m => m.GetBookMetadataAsync(Identifier, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ThrowsAsync(Pushback());

        using var httpClientForAudible = new HttpClient();
        var audible = new Mock<AudibleService>(httpClientForAudible, Mock.Of<ILogger<AudibleService>>());

        var controller = CreateController(
            imageCache.Object,
            metadataService.Object,
            audnexus.Object,
            audible.Object,
            EmptyLibrary().Object);

        var result = await controller.GetImage(Identifier);

        // The case the placeholder test cannot make: a throttle caught at a guarded site must
        // cost that one candidate and nothing else. Widening the recoverable-fault predicate to
        // cover pushback is what makes this true; without it the raise escapes that catch,
        // unwinds the walk, and a cover Audible had already handed over is never downloaded.
        var served = Assert.IsType<PhysicalFileResult>(result);
        Assert.Equal(cachedFullPath, served.FileName);
        imageCache.Verify(m => m.DownloadAndCacheImageAsync(coverUrl, Identifier), Times.Once);
    }
}
