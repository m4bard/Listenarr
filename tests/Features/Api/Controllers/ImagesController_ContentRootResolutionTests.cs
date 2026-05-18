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
using Listenarr.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using System.Reflection;
using Listenarr.Application.Interfaces.Repositories;
using Listenarr.Application.Common;
using Listenarr.Application.Interfaces;
using Listenarr.Application.Metadata;

namespace Listenarr.Tests.Features.Api.Controllers
{
    public class ImagesController_ContentRootResolutionTests
    {
        [Fact]
        public async Task GetImage_UsesApplicationPathServiceContentRoot()
        {
            var tempRoot = Path.Join(Path.GetTempPath(), "listenarr-images-controller-tests", Guid.NewGuid().ToString("N"));
            const string identifier = "ZZTEST1234";

            try
            {
                Directory.CreateDirectory(Path.Join(tempRoot, "config", "cache", "images", "authors"));

                var relativePath = $"config/cache/images/authors/{identifier}.jpg";
                var expectedPath = Path.Join(tempRoot, "config", "cache", "images", "authors", $"{identifier}.jpg");
                await File.WriteAllBytesAsync(expectedPath, new byte[] { 1, 2, 3, 4 });

                var imageCache = new Mock<IImageCacheService>();
                imageCache
                    .Setup(service => service.GetCachedImagePathAsync(identifier))
                    .ReturnsAsync(relativePath);

                var mockPathService = new Mock<IApplicationPathService>();
                mockPathService.SetupGet(p => p.ContentRootPath).Returns(tempRoot);

                using var httpClientForAudible = new System.Net.Http.HttpClient();
                var controller = new ImagesController(
                    imageCache.Object,
                    Mock.Of<IAudiobookMetadataService>(),
                    new Mock<AudibleService>(httpClientForAudible, Mock.Of<ILogger<AudibleService>>()) { CallBase = false }.Object,
                    Mock.Of<IAudnexusService>(),
                    Mock.Of<IAudiobookRepository>(),
                    Mock.Of<ILogger<ImagesController>>(),
                    mockPathService.Object);
                controller.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                };

                var effectiveRootField = typeof(ImagesController).GetField("_effectiveContentRootPath", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(effectiveRootField);
                Assert.Equal(tempRoot, effectiveRootField!.GetValue(controller));

                var result = await controller.GetImage(identifier);

                var fileResult = Assert.IsType<PhysicalFileResult>(result);
                var normalizedActualPath = fileResult.FileName.Replace('/', Path.DirectorySeparatorChar);
                Assert.Equal(expectedPath, normalizedActualPath);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }
    }
}
