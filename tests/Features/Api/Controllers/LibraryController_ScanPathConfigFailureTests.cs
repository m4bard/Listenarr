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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using Listenarr.Api.Controllers;
using Listenarr.Domain.Models;
using Listenarr.Application.Interfaces.Repositories;
using Listenarr.Application.Interfaces;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Api.Controllers
{
    [Trait("Area", "LibraryApi")]
    [Trait("Name", "LibraryController_ScanPathConfigFailureTests")]
    [Trait("Category", "LibraryController")]
    public class LibraryController_ScanPathConfigFailureTests : BaseTests
    {
        private LibraryController CreateController(
            IAudiobookRepository? audiobookRepository = null,
            IRootFolderService? rootFolderService = null,
            IConfigurationService? configurationService = null)
        {
            var serviceDescriptors = new List<ServiceDescriptor>();

            if (audiobookRepository != null)
            {
                serviceDescriptors.Add(ServiceDescriptor.Singleton(audiobookRepository));
            }

            if (rootFolderService != null)
            {
                serviceDescriptors.Add(ServiceDescriptor.Singleton(rootFolderService));
            }

            if (configurationService != null)
            {
                serviceDescriptors.Add(ServiceDescriptor.Singleton(configurationService));
            }

            return GetRequiredServiceWithOverrides<LibraryController>(
                serviceDescriptors,
                typeof(IScanQueueService));
        }
        [Fact]
        [Trait("Method", "ScanAudiobookFiles")]
        [Trait("Scenario", "ConfigUnavailable_NoBasePath_Returns500")]
        public async Task ScanAudiobook_ConfigUnavailable_NoBasePath_Returns500()
        {
            // Given
            var mockConfig = new Mock<IConfigurationService>();
            mockConfig.Setup(c => c.GetApplicationSettingsAsync()).ThrowsAsync(new Exception("config failure"));
            var mockRootFolderSvc = new Mock<IRootFolderService>();
            mockRootFolderSvc.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<RootFolder>());

            var controller = CreateController(
                rootFolderService: mockRootFolderSvc.Object,
                configurationService: mockConfig.Object);

            var ab = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Test")
                .Build());

            var request = new LibraryController.ScanRequest { Path = Path.Join(Path.GetTempPath(), "somepath") };

            // When
            var result = await controller.ScanAudiobookFiles(ab.Id, request);

            // Then
            var obj = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, obj.StatusCode);
            Assert.Contains("Failed to determine a safe scan path", obj.Value?.ToString() ?? string.Empty);
        }
    }
}
