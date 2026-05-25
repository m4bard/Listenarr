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
using Listenarr.Application.Interfaces;
using Listenarr.Application.Interfaces.Repositories;
using Listenarr.Domain.Models;
using Listenarr.Domain.Models.Configurations;
using Listenarr.Infrastructure.Persistence;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Listenarr.Tests.Features.Api.Controllers
{
    [Trait("Area", "LibraryApi")]
    [Trait("Name", "LibraryController_DeleteFilesystemTests")]
    [Trait("Category", "LibraryController")]
    public class LibraryController_DeleteFilesystemTests : BaseTests
    {
        [Fact]
        [Trait("Method", "DeleteAudiobook")]
        [Trait("Scenario", "DeleteFiles_RemovesAllFilesInFolderButPreservesDirectory")]
        public async Task DeleteAudiobook_DeleteFiles_RemovesAllFilesInFolderButPreservesDirectory()
        {
            // Given
            var tempRoot = FileService.GetTempDirectory("listenarr-delete");
            var bookFolder = Path.Join(tempRoot, "Jack of Shadows");
            var extrasFolder = Path.Join(bookFolder, "Extras");
            var audioPath = Path.Join(bookFolder, "Jack of Shadows.mp3");
            var sidecarPath = Path.Join(bookFolder, "cover.jpg");
            var notePath = Path.Join(extrasFolder, "notes.txt");

            Directory.CreateDirectory(extrasFolder);
            await File.WriteAllTextAsync(audioPath, "audio");
            await File.WriteAllTextAsync(sidecarPath, "cover");
            await File.WriteAllTextAsync(notePath, "notes");

            await using var dbContext = CreateDbContext();
            var audiobook = new AudiobookBuilder()
                .WithId(50)
                .WithTitle("Jack of Shadows")
                .WithBasePath(bookFolder)
                .WithFilePath(audioPath)
                .Build();

            var audioFile = new AudiobookFileBuilder()
                .WithAudiobook(audiobook)
                .WithPath(audioPath)
                .Build();

            audiobook.Files = [audioFile];

            dbContext.Audiobooks.Add(audiobook);
            dbContext.AudiobookFiles.AddRange(audiobook.Files);
            await dbContext.SaveChangesAsync();

            var controller = CreateController(dbContext);

            // When
            var result = await controller.DeleteAudiobook(audiobook.Id, deleteFiles: true, deleteFolder: false);

            // Then
            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(200, ok.StatusCode ?? 200);
            Assert.False(File.Exists(audioPath));
            Assert.False(File.Exists(sidecarPath));
            Assert.False(File.Exists(notePath));
            Assert.True(Directory.Exists(bookFolder));
            Assert.False(Directory.Exists(extrasFolder));
        }

        [Fact]
        [Trait("Method", "DeleteAudiobook")]
        [Trait("Scenario", "DeleteFilesAndFolder_RemovesTrackedFilesAndDirectory")]
        public async Task DeleteAudiobook_DeleteFilesAndFolder_RemovesTrackedFilesAndDirectory()
        {
            // Given
            var tempRoot = FileService.GetTempDirectory("listenarr-delete");
            var bookFolder = Path.Join(tempRoot, "Jack of Shadows");
            var audioPath = Path.Join(bookFolder, "Jack of Shadows.mp3");
            var sidecarPath = Path.Join(bookFolder, "cover.jpg");

            Directory.CreateDirectory(bookFolder);
            await File.WriteAllTextAsync(audioPath, "audio");
            await File.WriteAllTextAsync(sidecarPath, "cover");

            await using var dbContext = CreateDbContext();
            var audiobook = new AudiobookBuilder()
                .WithId(1)
                .WithTitle("Jack of Shadows")
                .WithBasePath(bookFolder)
                .WithFilePath(audioPath)
                .Build();

            var audioFile = new AudiobookFileBuilder()
                .WithAudiobook(audiobook)
                .WithPath(audioPath)
                .Build();

            audiobook.Files = [audioFile];

            dbContext.Audiobooks.Add(audiobook);
            dbContext.AudiobookFiles.AddRange(audiobook.Files);
            await dbContext.SaveChangesAsync();

            var controller = CreateController(dbContext);

            // When
            var result = await controller.DeleteAudiobook(audiobook.Id, deleteFiles: true, deleteFolder: true);

            // Then
            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(200, ok.StatusCode ?? 200);
            Assert.False(File.Exists(audioPath));
            Assert.False(Directory.Exists(bookFolder));
        }

        [Fact]
        [Trait("Method", "DeleteAudiobook")]
        [Trait("Scenario", "DeleteFolder_PreservesSharedDirectoryWhenAnotherAudiobookUsesIt")]
        public async Task DeleteAudiobook_DeleteFolder_PreservesSharedDirectoryWhenAnotherAudiobookUsesIt()
        {
            // Given
            var tempRoot = FileService.GetTempDirectory("listenarr-delete");
            var sharedFolder = Path.Join(tempRoot, "Shared");
            var currentAudioPath = Path.Join(sharedFolder, "current.mp3");
            var otherAudioPath = Path.Join(sharedFolder, "other.mp3");

            Directory.CreateDirectory(sharedFolder);
            await File.WriteAllTextAsync(currentAudioPath, "audio");
            await File.WriteAllTextAsync(otherAudioPath, "audio");

            await using var dbContext = CreateDbContext();
            var current = new AudiobookBuilder()
                .WithId(1)
                .WithTitle("Current")
                .WithBasePath(sharedFolder)
                .WithFilePath(currentAudioPath)
                .Build();
            var other = new AudiobookBuilder()
                .WithId(2)
                .WithTitle("Other")
                .WithBasePath(sharedFolder)
                .WithFilePath(otherAudioPath)
                .Build();

            var currentFile = new AudiobookFileBuilder()
                .WithAudiobook(current)
                .WithPath(currentAudioPath)
                .Build();
            var otherFile = new AudiobookFileBuilder()
                .WithAudiobook(other)
                .WithPath(otherAudioPath)
                .Build();

            current.Files = [currentFile];
            other.Files = [otherFile];

            dbContext.Audiobooks.AddRange(current, other);
            dbContext.AudiobookFiles.AddRange(current.Files.Concat(other.Files));
            await dbContext.SaveChangesAsync();

            var controller = CreateController(dbContext);

            // When
            var result = await controller.DeleteAudiobook(current.Id, deleteFiles: true, deleteFolder: true);

            // Then
            var ok = Assert.IsType<OkObjectResult>(result);
            var deletedFolderValue = ok.Value?.GetType().GetProperty("deletedFolder")?.GetValue(ok.Value);
            var deletedFolder = deletedFolderValue is bool flag ? flag : (bool?)null;
            var warnings = ok.Value?.GetType().GetProperty("warnings")?.GetValue(ok.Value) as IEnumerable<string>;

            Assert.False(File.Exists(currentAudioPath));
            Assert.True(File.Exists(otherAudioPath));
            Assert.True(Directory.Exists(sharedFolder));
            Assert.False(deletedFolder ?? true);
            Assert.NotNull(warnings);
            Assert.NotEmpty(warnings!);
        }

        [Fact]
        [Trait("Method", "DeleteAudiobook")]
        [Trait("Scenario", "DeleteFolder_UsesTrackedFileCommonDirectoryWhenBasePathIsProtectedRoot")]
        public async Task DeleteAudiobook_DeleteFolder_UsesTrackedFileCommonDirectoryWhenBasePathIsProtectedRoot()
        {
            // Given
            var tempRoot = FileService.GetTempDirectory("listenarr-delete");
            var bookFolder = Path.Join(tempRoot, "Roger Zelazny", "Jack of Shadows");
            var discFolder = Path.Join(bookFolder, "Disc 01");
            var audioPath = Path.Join(discFolder, "Jack of Shadows-01.mp3");

            Directory.CreateDirectory(discFolder);
            await File.WriteAllTextAsync(audioPath, "audio");

            await using var dbContext = CreateDbContext();
            dbContext.RootFolders.Add(new RootFolder
            {
                Id = 500,
                Name = "Library",
                Path = tempRoot,
                IsDefault = true
            });

            var audiobook = new AudiobookBuilder()
                .WithId(10)
                .WithTitle("Jack of Shadows")
                .WithBasePath(tempRoot)
                .WithFilePath(audioPath)
                .Build();

            var audioFile = new AudiobookFileBuilder()
                .WithAudiobook(audiobook)
                .WithPath(audioPath)
                .Build();

            audiobook.Files = [audioFile];

            dbContext.Audiobooks.Add(audiobook);
            dbContext.AudiobookFiles.AddRange(audiobook.Files);
            await dbContext.SaveChangesAsync();

            var controller = CreateController(dbContext);

            // When
            var result = await controller.DeleteAudiobook(audiobook.Id, deleteFiles: true, deleteFolder: true);

            // Then
            var ok = Assert.IsType<OkObjectResult>(result);
            var deletedFolderValue = ok.Value?.GetType().GetProperty("deletedFolder")?.GetValue(ok.Value);
            var deletedFolder = deletedFolderValue is bool flag ? flag : (bool?)null;

            Assert.False(File.Exists(audioPath));
            Assert.False(Directory.Exists(bookFolder));
            Assert.True(Directory.Exists(tempRoot));
            Assert.True(deletedFolder ?? false);
        }

        [Fact]
        [Trait("Method", "DeleteAudiobook")]
        [Trait("Scenario", "DeleteFolder_RemovesEmptyAuthorFolder")]
        public async Task DeleteAudiobook_DeleteFolder_RemovesEmptyAuthorFolder()
        {
            // Given
            var tempRoot = FileService.GetTempDirectory("listenarr-delete");
            var authorFolder = Path.Join(tempRoot, "Roger Zelazny");
            var bookFolder = Path.Join(authorFolder, "Jack of Shadows");
            var audioPath = Path.Join(bookFolder, "Jack of Shadows.mp3");

            Directory.CreateDirectory(bookFolder);
            await File.WriteAllTextAsync(audioPath, "audio");

            await using var dbContext = CreateDbContext();
            dbContext.RootFolders.Add(new RootFolder
            {
                Id = 600,
                Name = "Library",
                Path = tempRoot,
                IsDefault = true
            });

            var audiobook = new AudiobookBuilder()
                .WithId(12)
                .WithTitle("Jack of Shadows")
                .WithAuthor("Roger Zelazny")
                .WithBasePath(bookFolder)
                .WithFilePath(audioPath)
                .Build();

            var audioFile = new AudiobookFileBuilder()
                .WithAudiobook(audiobook)
                .WithPath(audioPath)
                .Build();

            audiobook.Files = [audioFile];

            dbContext.Audiobooks.Add(audiobook);
            dbContext.AudiobookFiles.AddRange(audiobook.Files);
            await dbContext.SaveChangesAsync();

            var controller = CreateController(dbContext);

            // When
            var result = await controller.DeleteAudiobook(audiobook.Id, deleteFiles: true, deleteFolder: true);

            // Then
            var ok = Assert.IsType<OkObjectResult>(result);
            var deletedParentFolderValue = ok.Value?.GetType().GetProperty("deletedParentFolder")?.GetValue(ok.Value);
            var deletedParentFolder = deletedParentFolderValue is bool flag ? flag : (bool?)null;

            Assert.False(File.Exists(audioPath));
            Assert.False(Directory.Exists(bookFolder));
            Assert.False(Directory.Exists(authorFolder));
            Assert.True(Directory.Exists(tempRoot));
            Assert.True(deletedParentFolder ?? false);
        }

        private static ListenArrDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ListenArrDbContext(options);
        }

        private LibraryController CreateController(ListenArrDbContext dbContext)
        {
            var repo = new Mock<IAudiobookRepository>();
            repo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
                .ReturnsAsync((int id) => dbContext.Audiobooks.Include(a => a.Files).FirstOrDefault(a => a.Id == id));
            repo.Setup(r => r.GetAllAsync())
                .ReturnsAsync(() => dbContext.Audiobooks.ToList());
            repo.Setup(r => r.DeleteByIdAsync(It.IsAny<int>()))
                .ReturnsAsync((int id) =>
                {
                    var audiobook = dbContext.Audiobooks.FirstOrDefault(a => a.Id == id);
                    if (audiobook == null)
                    {
                        return false;
                    }

                    dbContext.Audiobooks.Remove(audiobook);
                    dbContext.SaveChanges();
                    return true;
                });

            var imageCache = new Mock<IImageCacheService>();
            var logger = new Mock<ILogger<LibraryController>>();
            var fileNaming = new Mock<IFileNamingService>();

            var services = new ServiceCollection();
            var config = new Mock<IConfigurationService>();
            config.Setup(c => c.GetApplicationSettingsAsync())
                .ReturnsAsync(new ApplicationSettings { OutputPath = Path.GetTempPath() });
            services.AddSingleton<IConfigurationService>(config.Object);

            var provider = services.BuildServiceProvider();
            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            return new LibraryController(
                repo.Object,
                imageCache.Object,
                logger.Object,
                scopeFactory,
                new Mock<IHistoryRepository>().Object,
                new EfAudiobookFileRepository(dbContext),
                new Mock<IQualityProfileRepository>().Object,
                new Mock<IDownloadRepository>().Object,
                CreateRootFolderRepo(dbContext),
                fileNaming.Object,
                applicationPathService: _provider.GetRequiredService<IApplicationPathService>(),
                libraryListService: _provider.GetRequiredService<ILibraryListService>());
        }

        private static IRootFolderRepository CreateRootFolderRepo(ListenArrDbContext dbContext)
        {
            var mock = new Mock<IRootFolderRepository>();
            mock.Setup(r => r.GetAllAsync())
                .ReturnsAsync(() => dbContext.RootFolders.ToList());
            return mock.Object;
        }

    }
}
