using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Listenarr.Api.Controllers;
using Listenarr.Api.Services;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class LibraryController_DeleteFilesystemTests
    {
        [Fact]
        public async Task DeleteAudiobook_DeleteFiles_RemovesAllFilesInFolderButPreservesDirectory()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "listenarr-delete-" + Guid.NewGuid().ToString("N"));
            var bookFolder = Path.Combine(tempRoot, "Jack of Shadows");
            var extrasFolder = Path.Combine(bookFolder, "Extras");
            var audioPath = Path.Combine(bookFolder, "Jack of Shadows.mp3");
            var sidecarPath = Path.Combine(bookFolder, "cover.jpg");
            var notePath = Path.Combine(extrasFolder, "notes.txt");

            Directory.CreateDirectory(extrasFolder);
            await File.WriteAllTextAsync(audioPath, "audio");
            await File.WriteAllTextAsync(sidecarPath, "cover");
            await File.WriteAllTextAsync(notePath, "notes");

            try
            {
                await using var dbContext = CreateDbContext();
                var audiobook = new Audiobook
                {
                    Id = 50,
                    Title = "Jack of Shadows",
                    BasePath = bookFolder,
                    FilePath = audioPath,
                    Files = new List<AudiobookFile>
                    {
                        new AudiobookFile
                        {
                            Id = 51,
                            AudiobookId = 50,
                            Path = audioPath
                        }
                    }
                };

                dbContext.Audiobooks.Add(audiobook);
                dbContext.AudiobookFiles.AddRange(audiobook.Files);
                await dbContext.SaveChangesAsync();

                var controller = CreateController(dbContext);

                var result = await controller.DeleteAudiobook(audiobook.Id, deleteFiles: true, deleteFolder: false);

                var ok = Assert.IsType<OkObjectResult>(result);
                Assert.Equal(200, ok.StatusCode ?? 200);
                Assert.False(File.Exists(audioPath));
                Assert.False(File.Exists(sidecarPath));
                Assert.False(File.Exists(notePath));
                Assert.True(Directory.Exists(bookFolder));
                Assert.False(Directory.Exists(extrasFolder));
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        [Fact]
        public async Task DeleteAudiobook_DeleteFilesAndFolder_RemovesTrackedFilesAndDirectory()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "listenarr-delete-" + Guid.NewGuid().ToString("N"));
            var bookFolder = Path.Combine(tempRoot, "Jack of Shadows");
            var audioPath = Path.Combine(bookFolder, "Jack of Shadows.mp3");
            var sidecarPath = Path.Combine(bookFolder, "cover.jpg");

            Directory.CreateDirectory(bookFolder);
            await File.WriteAllTextAsync(audioPath, "audio");
            await File.WriteAllTextAsync(sidecarPath, "cover");

            try
            {
                await using var dbContext = CreateDbContext();
                var audiobook = new Audiobook
                {
                    Id = 1,
                    Title = "Jack of Shadows",
                    BasePath = bookFolder,
                    FilePath = audioPath,
                    Files = new List<AudiobookFile>
                    {
                        new AudiobookFile
                        {
                            Id = 11,
                            AudiobookId = 1,
                            Path = audioPath
                        }
                    }
                };

                dbContext.Audiobooks.Add(audiobook);
                dbContext.AudiobookFiles.AddRange(audiobook.Files);
                await dbContext.SaveChangesAsync();

                var controller = CreateController(dbContext);

                var result = await controller.DeleteAudiobook(audiobook.Id, deleteFiles: true, deleteFolder: true);

                var ok = Assert.IsType<OkObjectResult>(result);
                Assert.Equal(200, ok.StatusCode ?? 200);
                Assert.False(File.Exists(audioPath));
                Assert.False(Directory.Exists(bookFolder));
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        [Fact]
        public async Task DeleteAudiobook_DeleteFolder_PreservesSharedDirectoryWhenAnotherAudiobookUsesIt()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "listenarr-delete-" + Guid.NewGuid().ToString("N"));
            var sharedFolder = Path.Combine(tempRoot, "Shared");
            var currentAudioPath = Path.Combine(sharedFolder, "current.mp3");
            var otherAudioPath = Path.Combine(sharedFolder, "other.mp3");

            Directory.CreateDirectory(sharedFolder);
            await File.WriteAllTextAsync(currentAudioPath, "audio");
            await File.WriteAllTextAsync(otherAudioPath, "audio");

            try
            {
                await using var dbContext = CreateDbContext();
                var current = new Audiobook
                {
                    Id = 1,
                    Title = "Current",
                    BasePath = sharedFolder,
                    FilePath = currentAudioPath,
                    Files = new List<AudiobookFile>
                    {
                        new AudiobookFile
                        {
                            Id = 21,
                            AudiobookId = 1,
                            Path = currentAudioPath
                        }
                    }
                };
                var other = new Audiobook
                {
                    Id = 2,
                    Title = "Other",
                    BasePath = sharedFolder,
                    FilePath = otherAudioPath,
                    Files = new List<AudiobookFile>
                    {
                        new AudiobookFile
                        {
                            Id = 22,
                            AudiobookId = 2,
                            Path = otherAudioPath
                        }
                    }
                };

                dbContext.Audiobooks.AddRange(current, other);
                dbContext.AudiobookFiles.AddRange(current.Files.Concat(other.Files));
                await dbContext.SaveChangesAsync();

                var controller = CreateController(dbContext);

                var result = await controller.DeleteAudiobook(current.Id, deleteFiles: true, deleteFolder: true);

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
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        [Fact]
        public async Task DeleteAudiobook_DeleteFolder_UsesTrackedFileCommonDirectoryWhenBasePathIsProtectedRoot()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "listenarr-delete-" + Guid.NewGuid().ToString("N"));
            var bookFolder = Path.Combine(tempRoot, "Roger Zelazny", "Jack of Shadows");
            var discFolder = Path.Combine(bookFolder, "Disc 01");
            var audioPath = Path.Combine(discFolder, "Jack of Shadows-01.mp3");

            Directory.CreateDirectory(discFolder);
            await File.WriteAllTextAsync(audioPath, "audio");

            try
            {
                await using var dbContext = CreateDbContext();
                dbContext.RootFolders.Add(new RootFolder
                {
                    Id = 500,
                    Name = "Library",
                    Path = tempRoot,
                    IsDefault = true
                });

                var audiobook = new Audiobook
                {
                    Id = 10,
                    Title = "Jack of Shadows",
                    BasePath = tempRoot,
                    FilePath = audioPath,
                    Files = new List<AudiobookFile>
                    {
                        new AudiobookFile
                        {
                            Id = 31,
                            AudiobookId = 10,
                            Path = audioPath
                        }
                    }
                };

                dbContext.Audiobooks.Add(audiobook);
                dbContext.AudiobookFiles.AddRange(audiobook.Files);
                await dbContext.SaveChangesAsync();

                var controller = CreateController(dbContext);

                var result = await controller.DeleteAudiobook(audiobook.Id, deleteFiles: true, deleteFolder: true);

                var ok = Assert.IsType<OkObjectResult>(result);
                var deletedFolderValue = ok.Value?.GetType().GetProperty("deletedFolder")?.GetValue(ok.Value);
                var deletedFolder = deletedFolderValue is bool flag ? flag : (bool?)null;

                Assert.False(File.Exists(audioPath));
                Assert.False(Directory.Exists(bookFolder));
                Assert.True(Directory.Exists(tempRoot));
                Assert.True(deletedFolder ?? false);
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        [Fact]
        public async Task DeleteAudiobook_DeleteFolder_RemovesEmptyAuthorFolder()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "listenarr-delete-" + Guid.NewGuid().ToString("N"));
            var authorFolder = Path.Combine(tempRoot, "Roger Zelazny");
            var bookFolder = Path.Combine(authorFolder, "Jack of Shadows");
            var audioPath = Path.Combine(bookFolder, "Jack of Shadows.mp3");

            Directory.CreateDirectory(bookFolder);
            await File.WriteAllTextAsync(audioPath, "audio");

            try
            {
                await using var dbContext = CreateDbContext();
                dbContext.RootFolders.Add(new RootFolder
                {
                    Id = 600,
                    Name = "Library",
                    Path = tempRoot,
                    IsDefault = true
                });

                var audiobook = new Audiobook
                {
                    Id = 12,
                    Title = "Jack of Shadows",
                    Authors = new List<string> { "Roger Zelazny" },
                    BasePath = bookFolder,
                    FilePath = audioPath,
                    Files = new List<AudiobookFile>
                    {
                        new AudiobookFile
                        {
                            Id = 41,
                            AudiobookId = 12,
                            Path = audioPath
                        }
                    }
                };

                dbContext.Audiobooks.Add(audiobook);
                dbContext.AudiobookFiles.AddRange(audiobook.Files);
                await dbContext.SaveChangesAsync();

                var controller = CreateController(dbContext);

                var result = await controller.DeleteAudiobook(audiobook.Id, deleteFiles: true, deleteFolder: true);

                var ok = Assert.IsType<OkObjectResult>(result);
                var deletedParentFolderValue = ok.Value?.GetType().GetProperty("deletedParentFolder")?.GetValue(ok.Value);
                var deletedParentFolder = deletedParentFolderValue is bool flag ? flag : (bool?)null;

                Assert.False(File.Exists(audioPath));
                Assert.False(Directory.Exists(bookFolder));
                Assert.False(Directory.Exists(authorFolder));
                Assert.True(Directory.Exists(tempRoot));
                Assert.True(deletedParentFolder ?? false);
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private static ListenArrDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ListenArrDbContext(options);
        }

        private static LibraryController CreateController(ListenArrDbContext dbContext)
        {
            var repo = new Mock<IAudiobookRepository>();
            repo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
                .ReturnsAsync((int id) => dbContext.Audiobooks.Include(a => a.Files).FirstOrDefault(a => a.Id == id));
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
                dbContext,
                scopeFactory,
                fileNaming.Object,
                scanQueueService: null,
                moveQueueService: null,
                notificationService: null,
                rootFolderService: null);
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
