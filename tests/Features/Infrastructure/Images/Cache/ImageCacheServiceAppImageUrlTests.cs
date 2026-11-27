/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Images.Cache
{
    /// <summary>
    /// Search results hand the UI Listenarr's own image path ("/api/v1/images/{asin}"), and adding a
    /// book posts that path back as its image URL. On Linux Uri.TryCreate reads "/api/..." as an
    /// absolute file:// URI, so the validator logged "Unsupported URL scheme 'file'" (issue 1238).
    /// </summary>
    [Trait("Name", "ImageCacheServiceAppImageUrlTests")]
    [Trait("Category", "Cache")]
    public class ImageCacheServiceAppImageUrlTests : BaseTests
    {
        private const string Asin = "B008DFUGCQ";
        private const string AppImageUrl = "/api/v1/images/" + Asin;

        [LinuxFact]
        public void Uri_TryCreate_reads_the_app_image_path_as_a_file_uri_on_linux()
        {
            Assert.True(Uri.TryCreate(AppImageUrl, UriKind.Absolute, out var uri));
            Assert.Equal("file", uri.Scheme);
            Assert.False(ImageDownloadValidator.TryValidateExternalImageUrl(AppImageUrl, out var reason));
            Assert.Equal("Unsupported URL scheme 'file'", reason);
        }

        [Theory]
        [InlineData(AppImageUrl)]
        [InlineData("/api/v1.1/images/" + Asin)]
        [InlineData(AppImageUrl + "?url=https%3A%2F%2Fexample.com%2Fcover.jpg")]
        public async Task DownloadAndCacheImageAsync_returns_the_cached_image_for_the_app_own_image_path(string imageUrl)
        {
            var (service, _, tempCachePath) = CreateService();
            await File.WriteAllBytesAsync(Path.Join(tempCachePath, Asin + ".jpg"), FakeJpeg());

            var cached = await service.DownloadAndCacheImageAsync(imageUrl, Asin);

            Assert.Equal($"config/cache/images/temp/{Asin}.jpg", cached);
        }

        [Fact]
        public async Task DownloadAndCacheImageAsync_does_not_warn_for_the_app_own_image_path()
        {
            var (service, logs, tempCachePath) = CreateService();
            await File.WriteAllBytesAsync(Path.Join(tempCachePath, Asin + ".jpg"), FakeJpeg());

            await service.DownloadAndCacheImageAsync(AppImageUrl, Asin);

            Assert.DoesNotContain(logs, line => line.Contains("Blocked image download URL", StringComparison.Ordinal));
        }

        [Fact]
        public async Task DownloadAndCacheImageAsync_returns_null_without_warning_when_the_app_own_image_path_is_not_cached()
        {
            var (service, logs, _) = CreateService();

            var cached = await service.DownloadAndCacheImageAsync(AppImageUrl, Asin);

            Assert.Null(cached);
            Assert.DoesNotContain(logs, line => line.Contains("Blocked image download URL", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("file:///etc/passwd")]
        [InlineData("/etc/passwd")]
        [InlineData("/api/v1/other/" + Asin)]
        [InlineData("/api/v1/images/" + Asin + "/../../../etc/passwd")]
        [InlineData("//api/v1/images/" + Asin)]
        public async Task DownloadAndCacheImageAsync_still_refuses_file_urls_and_lookalike_paths(string imageUrl)
        {
            var (service, logs, tempCachePath) = CreateService();
            await File.WriteAllBytesAsync(Path.Join(tempCachePath, Asin + ".jpg"), FakeJpeg());

            var cached = await service.DownloadAndCacheImageAsync(imageUrl, Asin);

            Assert.Null(cached);
            Assert.Contains(logs, line => line.Contains("Blocked image download URL", StringComparison.Ordinal));
        }

        private static byte[] FakeJpeg()
        {
            var bytes = new byte[2048];
            bytes[0] = 0xFF;
            bytes[1] = 0xD8;
            bytes[2] = 0xFF;
            bytes[^2] = 0xFF;
            bytes[^1] = 0xD9;
            return bytes;
        }

        private (ImageCacheService Service, List<string> Logs, string TempCachePath) CreateService()
        {
            var repoApiRoot = Path.Join(FileService.GetTempPath(), "listenarr.api");
            var images = Path.Join(repoApiRoot, "config", "cache", "images");
            var tempCachePath = Path.Join(images, "temp");

            var paths = new Mock<IApplicationPathService>();
            paths.SetupGet(p => p.ContentRootPath).Returns(repoApiRoot);
            paths.Setup(p => p.ResolveFromConfig("cache", "images", "temp")).Returns(tempCachePath);
            paths.Setup(p => p.ResolveFromConfig("cache", "images", "library")).Returns(Path.Join(images, "library"));
            paths.Setup(p => p.ResolveFromConfig("cache", "images", "authors")).Returns(Path.Join(images, "authors"));
            paths.Setup(p => p.ResolveFromConfig("cache", "images", "series")).Returns(Path.Join(images, "series"));

            var logs = new List<string>();
            var service = new ImageCacheService(new RecordingLogger(logs), new HttpClient(), paths.Object);
            return (service, logs, tempCachePath);
        }

        private sealed class RecordingLogger(List<string> lines) : ILogger<ImageCacheService>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                ArgumentNullException.ThrowIfNull(formatter);
                lines.Add($"{logLevel}: {formatter(state, exception)}");
            }
        }
    }
}
