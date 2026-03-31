using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Listenarr.Api.Controllers;
using Listenarr.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class ImagesController_PlaceholderFallbackTests
    {
        [Fact]
        public async Task GetImage_ReturnsPlaceholder_WhenImageLookupFails()
        {
            // Arrange
            const string identifier = "B000APXZHK";

            var imageCache = new Mock<IImageCacheService>();
            imageCache.Setup(m => m.GetCachedImagePathAsync(identifier)).ReturnsAsync((string?)null);

            var metadataService = new Mock<IAudiobookMetadataService>();
            metadataService
                .Setup(m => m.GetAudibleMetadataAsync(identifier, It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync((AudibleBookResponse?)null);
            metadataService
                .Setup(m => m.GetMetadataAsync(identifier, It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync((object?)null);

            using var httpClientForAudible = new System.Net.Http.HttpClient();
            var audible = new Mock<AudibleService>(httpClientForAudible, Mock.Of<ILogger<AudibleService>>());
            audible
                .Setup(m => m.LookupAuthorAsync(identifier, It.IsAny<string>()))
                .ReturnsAsync((AuthorLookupItem?)null);

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
            repo.Setup(r => r.GetByAsinAsync(identifier)).ReturnsAsync((Listenarr.Domain.Models.Audiobook?)null);
            repo.Setup(r => r.GetAuthorAsinByNameAsync(identifier)).ReturnsAsync((string?)null);

            var tempRoot = Path.Join(Path.GetTempPath(), "listenarr_test_contentroot_missing_placeholder");
            Directory.CreateDirectory(tempRoot);

            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(e => e.ContentRootPath).Returns(tempRoot);

            var controller = new ImagesController(
                imageCache.Object,
                metadataService.Object,
                audible.Object,
                audnexus.Object,
                repo.Object,
                Mock.Of<ILogger<ImagesController>>(),
                environment.Object);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };

            // Act
            var result = await controller.GetImage(identifier);

            // Assert
            Assert.False(result is NotFoundObjectResult);
            Assert.True(
                result is PhysicalFileResult physical && physical.FileName.EndsWith("placeholder.svg", System.StringComparison.OrdinalIgnoreCase)
                || result is RedirectResult redirect && redirect.Url == "/placeholder.svg",
                $"Expected placeholder response, got {result.GetType().Name}");
        }
    }
}
