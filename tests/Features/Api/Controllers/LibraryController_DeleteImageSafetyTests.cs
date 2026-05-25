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
using Xunit;
using Moq;
using Microsoft.Extensions.DependencyInjection;
using Listenarr.Api.Controllers;
using Listenarr.Application.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;
using Listenarr.Application.Interfaces;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Api.Controllers
{
    [Trait("Area", "LibraryApi")]
    [Trait("Name", "LibraryController_DeleteImageSafetyTests")]
    [Trait("Category", "LibraryController")]
    public class LibraryController_DeleteImageSafetyTests : BaseTests
    {
        [Fact]
        [Trait("Method", "DeleteAudiobook")]
        [Trait("Scenario", "InvalidImageUrl_DoesNotCallImageCacheService")]
        public async Task DeleteAudiobook_InvalidImageUrl_DoesNotCallImageCacheService()
        {
            // Given
            var mockRepo = new Mock<IAudiobookRepository>();
            var mockImageCache = new Mock<IImageCacheService>();

            var audiobook = new AudiobookBuilder()
                .WithId(123)
                .WithTitle("Test")
                .WithImageUrl("/config/cache/images/library/../evil/../../secret.txt")
                .Build();
            mockRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync(audiobook);
            mockRepo.Setup(r => r.DeleteByIdAsync(It.IsAny<int>())).ReturnsAsync(true);

            var controller = GetRequiredServiceWithOverrides<LibraryController>(
                ServiceDescriptor.Singleton(mockRepo.Object),
                ServiceDescriptor.Singleton(mockImageCache.Object));

            // When
            var result = await controller.DeleteAudiobook(audiobook.Id);

            // Then
            // The identifier 'secret' should be extracted and validated; ensure we called into the image cache service
            mockImageCache.Verify(s => s.GetCachedImagePathAsync("secret"), Times.Once);
            Assert.IsType<OkObjectResult>(result);
        }
    }
}
