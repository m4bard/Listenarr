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
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Api.Features.Library
{
    [Trait("Area", "LibraryApi")]
    [Trait("Name", "LibraryController_MoveTests")]
    [Trait("Category", "LibraryController")]
    public class LibraryController_MoveTests : BaseTests
    {
        [Fact]
        [Trait("Method", "EnqueueMove")]
        [Trait("Scenario", "ReturnsBadRequest_WhenSourceDoesNotExist")]
        public async Task MoveAudiobook_ReturnsBadRequest_WhenSourceDoesNotExist()
        {
            // Given
            var controller = _provider.GetRequiredService<LibraryController>();

            var outputPath = FileService.GetTempDirectory("listenarr-move-output");
            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithOutputPath(outputPath)
                .Build());

            var ab = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Test")
                .WithBasePath(Path.Join(FileService.GetTempPath(), "nonexistent"))
                .Build());

            var request = new LibraryController.MoveRequest { DestinationPath = Path.Join(outputPath, "target") };

            // When
            var result = await controller.EnqueueMove(ab.Id, request);

            // Then: expect 400 Bad Request with 'Source path' message
            var badObj = Assert.IsAssignableFrom<ObjectResult>(result);
            Assert.Equal(400, badObj.StatusCode);
            Assert.Contains("Source path", badObj.Value?.ToString() ?? string.Empty);
        }

        [Fact]
        [Trait("Method", "EnqueueMove")]
        [Trait("Scenario", "EnqueuesJob_WhenSourceExists")]
        public async Task MoveAudiobook_EnqueuesJob_WhenSourceExists()
        {
            // Given
            var mockMoveQueue = new Mock<IMoveQueueService>();
            var expectedId = Guid.NewGuid();
            mockMoveQueue.Setup(m => m.EnqueueMoveAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(expectedId);

            Init(services => services.WithSingleton(mockMoveQueue.Object));
            var controller = _provider.GetRequiredService<LibraryController>();

            var outputPath = FileService.GetTempDirectory("listenarr-move-output");
            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithOutputPath(outputPath)
                .Build());

            var ab = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Test")
                .WithBasePath(FileService.GetTempDirectory("listenarr-move-src"))
                .Build());

            var target = Path.Join(outputPath, "listenarr-move-dst");
            var request = new LibraryController.MoveRequest { DestinationPath = target };

            // When
            var result = await controller.EnqueueMove(ab.Id, request);

            // Then: expect 202 Accepted
            var acceptedObj = Assert.IsAssignableFrom<ObjectResult>(result);
            Assert.Equal(202, acceptedObj.StatusCode);
            Assert.NotNull(acceptedObj.Value);
        }

        [Fact]
        [Trait("Method", "EnqueueMove")]
        [Trait("Scenario", "UpdatesBasePath_WhenMoveFilesFalse")]
        public async Task MoveAudiobook_UpdatesBasePath_WhenMoveFilesFalse()
        {
            // Given
            var mockMoveQueue = new Mock<IMoveQueueService>();

            Init(services => services.WithSingleton(mockMoveQueue.Object));
            var controller = _provider.GetRequiredService<LibraryController>();

            var outputPath = FileService.GetTempDirectory("listenarr-move-output");
            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithOutputPath(outputPath)
                .Build());

            var ab = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Test")
                .WithBasePath(Path.Join(FileService.GetTempPath(), "listenarr-move-src"))
                .Build());

            var target = Path.Join(outputPath, "listenarr-move-dst");
            var request = new LibraryController.MoveRequest { DestinationPath = target, MoveFiles = false };

            // When
            var result = await controller.EnqueueMove(ab.Id, request);

            // Then: expect 200 OK
            var okObj = Assert.IsAssignableFrom<ObjectResult>(result);
            Assert.Equal(200, okObj.StatusCode);
            Assert.NotNull(okObj.Value);

            // Ensure DB was updated
            var updated = await _audiobookRepository.GetByIdAsync(ab.Id);
            Assert.Equal(FileUtils.NormalizeStoredPath(target), updated!.BasePath);

            // Ensure move queue was NOT enqueued
            mockMoveQueue.Verify(m => m.EnqueueMoveAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        [Trait("Method", "EnqueueMove")]
        [Trait("Scenario", "PreservesDestinationPathWhitespace_WhenMoveFilesFalse")]
        public async Task MoveAudiobook_PreservesDestinationPathWhitespace_WhenMoveFilesFalse()
        {
            // Given
            var outputPath = FileService.GetTempPath();
            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithOutputPath(outputPath)
                .Build());

            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Test")
                .WithBasePath(FileService.GetTempDirectory("listenarr-move-src"))
                .Build());

            var controller = _provider.GetRequiredService<LibraryController>();

            var relativeTarget = "  listenarr-move-dst-" + Guid.NewGuid().ToString("N");
            var request = new LibraryController.MoveRequest { DestinationPath = relativeTarget, MoveFiles = false };

            // When
            var result = await controller.EnqueueMove(audiobook.Id, request);

            // Then
            var okObj = Assert.IsAssignableFrom<ObjectResult>(result);
            Assert.Equal(200, okObj.StatusCode);

            var updated = await _audiobookRepository.GetByIdAsync(audiobook.Id);
            Assert.NotNull(updated);
            Assert.Equal(FileUtils.NormalizeStoredPath(Path.Join(outputPath, relativeTarget)), updated.BasePath);
            Assert.StartsWith("  listenarr-move-dst-", Path.GetFileName(updated.BasePath), StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Method", "EnqueueMove")]
        [Trait("Scenario", "RejectsInvalidDestinationPath")]
        public async Task MoveAudiobook_RejectsInvalidDestinationPath()
        {
            var outputPath = FileService.GetTempDirectory("listenarr-move-output");
            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithOutputPath(outputPath)
                .Build());

            var sourcePath = FileService.GetTempDirectory("listenarr-move-src");
            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Test")
                .WithBasePath(sourcePath)
                .Build());

            var controller = _provider.GetRequiredService<LibraryController>();
            var request = new LibraryController.MoveRequest
            {
                DestinationPath = Path.Join(FileService.GetTempPath(), "bad\0target"),
                MoveFiles = false
            };

            var result = await controller.EnqueueMove(audiobook.Id, request);

            var badObj = Assert.IsAssignableFrom<ObjectResult>(result);
            Assert.Equal(400, badObj.StatusCode);
            Assert.Contains("DestinationPath", badObj.Value?.ToString() ?? string.Empty);
        }

        [Fact]
        [Trait("Method", "EnqueueMove")]
        [Trait("Scenario", "AllowsAbsoluteDestinationInsideConfiguredRootFolder")]
        public async Task MoveAudiobook_AllowsAbsoluteDestinationInsideConfiguredRootFolder()
        {
            var rootPath = FileService.GetTempDirectory("listenarr-move-root");
            await _rootFolderRepository.AddAsync(new RootFolderBuilder()
                .WithName("Move Root")
                .WithPath(rootPath)
                .WithIsDefault()
                .Build());

            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Test")
                .WithBasePath(FileService.GetTempDirectory("listenarr-move-src"))
                .Build());

            var controller = _provider.GetRequiredService<LibraryController>();
            var target = Path.Join(rootPath, "Author", "Title");
            var request = new LibraryController.MoveRequest { DestinationPath = target, MoveFiles = false };

            var result = await controller.EnqueueMove(audiobook.Id, request);

            var okObj = Assert.IsAssignableFrom<ObjectResult>(result);
            Assert.Equal(200, okObj.StatusCode);

            var updated = await _audiobookRepository.GetByIdAsync(audiobook.Id);
            Assert.NotNull(updated);
            Assert.Equal(FileUtils.NormalizeStoredPath(target), updated.BasePath);
        }

        [Fact]
        [Trait("Method", "EnqueueMove")]
        [Trait("Scenario", "AllowsAbsoluteDestinationInsideConfiguredOutputPath")]
        public async Task MoveAudiobook_AllowsAbsoluteDestinationInsideConfiguredOutputPath()
        {
            var outputPath = FileService.GetTempDirectory("listenarr-move-output");
            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithOutputPath(outputPath)
                .Build());

            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Test")
                .WithBasePath(FileService.GetTempDirectory("listenarr-move-src"))
                .Build());

            var controller = _provider.GetRequiredService<LibraryController>();
            var target = Path.Join(outputPath, "Author", "Title");
            var request = new LibraryController.MoveRequest { DestinationPath = target, MoveFiles = false };

            var result = await controller.EnqueueMove(audiobook.Id, request);

            var okObj = Assert.IsAssignableFrom<ObjectResult>(result);
            Assert.Equal(200, okObj.StatusCode);

            var updated = await _audiobookRepository.GetByIdAsync(audiobook.Id);
            Assert.NotNull(updated);
            Assert.Equal(FileUtils.NormalizeStoredPath(target), updated.BasePath);
        }

        [Fact]
        [Trait("Method", "EnqueueMove")]
        [Trait("Scenario", "RejectsAbsoluteDestinationOutsideConfiguredRoots")]
        public async Task MoveAudiobook_RejectsAbsoluteDestinationOutsideConfiguredRoots()
        {
            var outputPath = FileService.GetTempDirectory("listenarr-move-output");
            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithOutputPath(outputPath)
                .Build());

            var originalBasePath = FileService.GetTempDirectory("listenarr-move-src");
            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Test")
                .WithBasePath(originalBasePath)
                .Build());

            var controller = _provider.GetRequiredService<LibraryController>();
            var outsidePath = Path.Join(FileService.GetTempDirectory("listenarr-move-outside"), "Author", "Title");
            var request = new LibraryController.MoveRequest { DestinationPath = outsidePath, MoveFiles = false };

            var result = await controller.EnqueueMove(audiobook.Id, request);

            var badObj = Assert.IsAssignableFrom<ObjectResult>(result);
            Assert.Equal(400, badObj.StatusCode);
            Assert.Contains("configured root folder or output path", badObj.Value?.ToString() ?? string.Empty);

            var unchanged = await _audiobookRepository.GetByIdAsync(audiobook.Id);
            Assert.NotNull(unchanged);
            Assert.Equal(originalBasePath, unchanged.BasePath);
        }

        [Fact]
        [Trait("Method", "EnqueueMove")]
        [Trait("Scenario", "UsesDefaultRootFolderForRelativeDestination_WhenOutputPathEmpty")]
        public async Task MoveAudiobook_UsesDefaultRootFolderForRelativeDestination_WhenOutputPathEmpty()
        {
            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithOutputPath(string.Empty)
                .Build());
            var rootPath = FileService.GetTempDirectory("listenarr-move-root");
            await _rootFolderRepository.AddAsync(new RootFolderBuilder()
                .WithName("Default Move Root")
                .WithPath(rootPath)
                .WithIsDefault()
                .Build());

            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Test")
                .WithBasePath(FileService.GetTempDirectory("listenarr-move-src"))
                .Build());

            var controller = _provider.GetRequiredService<LibraryController>();
            var relativeTarget = Path.Join("Author", "Title");
            var request = new LibraryController.MoveRequest { DestinationPath = relativeTarget, MoveFiles = false };

            var result = await controller.EnqueueMove(audiobook.Id, request);

            var okObj = Assert.IsAssignableFrom<ObjectResult>(result);
            Assert.Equal(200, okObj.StatusCode);

            var updated = await _audiobookRepository.GetByIdAsync(audiobook.Id);
            Assert.NotNull(updated);
            Assert.Equal(FileUtils.NormalizeStoredPath(Path.Join(rootPath, relativeTarget)), updated.BasePath);
        }

        [Fact]
        [Trait("Method", "EnqueueMove")]
        [Trait("Scenario", "RejectsRelativeDestinationOutsideOutputPath")]
        public async Task MoveAudiobook_RejectsRelativeDestinationOutsideOutputPath()
        {
            var outputPath = FileService.GetTempDirectory("listenarr-move-output");
            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithOutputPath(outputPath)
                .Build());

            var sourcePath = FileService.GetTempDirectory("listenarr-move-src");
            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Test")
                .WithBasePath(sourcePath)
                .Build());

            var controller = _provider.GetRequiredService<LibraryController>();
            var request = new LibraryController.MoveRequest
            {
                DestinationPath = Path.Join("..", "escape"),
                MoveFiles = false
            };

            var result = await controller.EnqueueMove(audiobook.Id, request);

            var badObj = Assert.IsAssignableFrom<ObjectResult>(result);
            Assert.Equal(400, badObj.StatusCode);
            Assert.Contains("DestinationPath", badObj.Value?.ToString() ?? string.Empty);
        }
    }
}
