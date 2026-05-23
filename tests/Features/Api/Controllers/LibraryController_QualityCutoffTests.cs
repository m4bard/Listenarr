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
using System.Reflection;
using Listenarr.Api.Controllers;
using Listenarr.Application.Interfaces;
using Listenarr.Application.Interfaces.Repositories;
using Listenarr.Domain.Models;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Listenarr.Tests.Features.Api.Controllers
{
    [Trait("Area", "LibraryApi")]
    [Trait("Name", "LibraryController_QualityCutoffTests")]
    [Trait("Category", "LibraryController")]
    public class LibraryController_QualityCutoffTests : BaseTests
    {
        private LibraryController CreateController(
            IAudiobookFileRepository? audioFileRepository = null,
            IDownloadRepository? downloadRepository = null)
        {
            return new LibraryController(
                _audiobookRepository,
                new Mock<IImageCacheService>().Object,
                new Mock<ILogger<LibraryController>>().Object,
                _provider.GetRequiredService<IServiceScopeFactory>(),
                new Mock<IHistoryRepository>().Object,
                audioFileRepository ?? new Mock<IAudiobookFileRepository>().Object,
                new Mock<IQualityProfileRepository>().Object,
                downloadRepository ?? new Mock<IDownloadRepository>().Object,
                new Mock<IRootFolderRepository>().Object,
                new Mock<IFileNamingService>().Object,
                applicationPathService: LibraryControllerMockFactory.CreateApplicationPathService(FileService.GetTempPath()),
                libraryListService: LibraryControllerMockFactory.CreateLibraryListService());
        }

        [Fact]
        [Trait("Method", "IsQualityCutoffMetAsync")]
        [Trait("Scenario", "ImportPendingDownload_ReturnsTrue")]
        public async Task IsQualityCutoffMetAsync_ImportPendingDownload_ReturnsTrue()
        {
            // Given
            var downloadRepo = new Mock<IDownloadRepository>();
            downloadRepo.Setup(r => r.GetByAudiobookIdAsync(1, default)).ReturnsAsync(new List<Download>
            {
                new DownloadBuilder()
                    .WithId("dl-1")
                    .WithAudiobookId(1)
                    .WithStatus(DownloadStatus.ImportPending)
                    .WithTitle("Dune")
                    .Build()
            });

            var audioFileRepo = new Mock<IAudiobookFileRepository>();
            audioFileRepo.Setup(r => r.GetByAudiobookIdAsync(1, default)).ReturnsAsync(new List<AudiobookFile>());

            var audiobook = new AudiobookBuilder()
                .WithId(1)
                .WithTitle("Dune")
                .WithQualityProfile(new QualityProfile
                {
                    CutoffQuality = "MP3",
                    Qualities = new List<QualityDefinition>
                    {
                        new() { Quality = "MP3", Priority = 1 }
                    }
                })
                .Build();

            var controller = CreateController(audioFileRepository: audioFileRepo.Object, downloadRepository: downloadRepo.Object);

            // When
            var method = typeof(LibraryController).GetMethod(
                "IsQualityCutoffMetAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var task = (Task<bool>?)method!.Invoke(controller, new object[] { audiobook, Mock.Of<IQualityProfileService>(), downloadRepo.Object, audioFileRepo.Object });
            Assert.NotNull(task);
            var result = await task!;

            // Then
            Assert.True(result);
        }
    }
}
