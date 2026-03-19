using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Listenarr.Api.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class ImageCacheServiceTests
    {
        [Fact]
        public async Task MoveToAuthorLibraryStorageAsync_UsesRepoRoot_WhenDevContentRootPointsToBinOutput()
        {
            var originalEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            var tempRoot = Path.Combine(Path.GetTempPath(), "listenarr-image-cache-tests", Guid.NewGuid().ToString("N"));

            try
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

                var repoApiRoot = Path.Combine(tempRoot, "listenarr.api");
                var binRoot = Path.Combine(repoApiRoot, "bin", "Debug", "net8.0");
                Directory.CreateDirectory(Path.Combine(repoApiRoot, "config"));
                Directory.CreateDirectory(Path.Combine(binRoot, "config"));
                File.WriteAllText(Path.Combine(repoApiRoot, "listenarr.api.csproj"), "<Project />");

                var httpClientFactory = new Mock<IHttpClientFactory>();
                httpClientFactory
                    .Setup(factory => factory.CreateClient(It.IsAny<string>()))
                    .Returns(new HttpClient());

                var service = new ImageCacheService(
                    Mock.Of<ILogger<ImageCacheService>>(),
                    httpClientFactory.Object,
                    binRoot);

                var repoTempImage = Path.Combine(repoApiRoot, "config", "cache", "images", "temp", "AUTHOR123.jpg");
                Directory.CreateDirectory(Path.GetDirectoryName(repoTempImage)!);
                await File.WriteAllBytesAsync(repoTempImage, new byte[] { 1, 2, 3, 4 });

                var relativePath = await service.MoveToAuthorLibraryStorageAsync("AUTHOR123");

                var expectedAuthorImage = Path.Combine(repoApiRoot, "config", "cache", "images", "authors", "AUTHOR123.jpg");
                var wrongAuthorImage = Path.Combine(binRoot, "config", "cache", "images", "authors", "AUTHOR123.jpg");

                Assert.Equal("config/cache/images/authors/AUTHOR123.jpg", relativePath);
                Assert.True(File.Exists(expectedAuthorImage));
                Assert.False(File.Exists(wrongAuthorImage));
            }
            finally
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", originalEnvironment);
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }
    }
}
