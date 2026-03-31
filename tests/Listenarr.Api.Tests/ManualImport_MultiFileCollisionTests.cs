using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Listenarr.Api.Controllers;
using Listenarr.Domain.Models;
using Listenarr.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Api.Tests
{
    public class ManualImport_MultiFileCollisionTests
    {
        private static void TryDeleteDirectory(string path)
        {
            try
            {
                Directory.Delete(path, true);
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }

        [Fact]
        public async Task InteractiveManualImport_MultipleFiles_ResolvesCollisionsWithinBatch()
        {
            // Setup DB-like audiobook object
            var basePath = Path.Join(Path.GetTempPath(), "listenarr-manual-batch", Guid.NewGuid().ToString());
            Directory.CreateDirectory(basePath);

            var book = new Audiobook { Id = 42, Title = "Batch Book", BasePath = basePath };

            // Create two source files
            var srcDir = Path.Join(Path.GetTempPath(), "listenarr-manual-src", Guid.NewGuid().ToString());
            Directory.CreateDirectory(srcDir);
            var src1 = Path.Join(srcDir, "one.mp3");
            var src2 = Path.Join(srcDir, "two.mp3");
            await File.WriteAllTextAsync(src1, "one");
            await File.WriteAllTextAsync(src2, "two");

            // Mocks
            var repoMock = new Mock<IAudiobookRepository>();
            repoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => id == book.Id ? book : null);

            var metadataMock = new Mock<IMetadataService>();
            metadataMock.Setup(m => m.ExtractFileMetadataAsync(It.IsAny<string>())).ReturnsAsync(new AudioMetadata { Title = "Chapter", Bitrate = 128000 });

            var fileNamingMock = new Mock<IFileNamingService>();
            // For manual import pattern {Title} we want the generated relative path to be the book title (no extra folders)
            fileNamingMock.Setup(f => f.ApplyNamingPattern(It.IsAny<string>(), It.IsAny<System.Collections.Generic.Dictionary<string, object>>(), It.IsAny<bool>()))
                .Returns((string pattern, System.Collections.Generic.Dictionary<string, object> vars, bool t) =>
                {
                    vars.TryGetValue("Title", out var titleObj);
                    return titleObj?.ToString() ?? "Batch Book";
                });

            var configMock = new Mock<IConfigurationService>();
            configMock.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(new ApplicationSettings { OutputPath = basePath });

            var scanMock = new Mock<IScanQueueService>();
            scanMock.Setup(s => s.EnqueueScanAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(Guid.NewGuid());

            var rootFolderMock = new Mock<IRootFolderService>();
            rootFolderMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new System.Collections.Generic.List<Listenarr.Domain.Models.RootFolder>());

            var controller = new ManualImportController(
                Mock.Of<Microsoft.Extensions.Logging.ILogger<ManualImportController>>(),
                repoMock.Object,
                metadataMock.Object,
                fileNamingMock.Object,
                configMock.Object,
                scanMock.Object,
                rootFolderMock.Object,
                Mock.Of<IFileMover>()
            );

            var request = new ManualImportRequest
            {
                Path = srcDir,
                Mode = "interactive",
                InputMode = "copy",
                Items = new System.Collections.Generic.List<ManualImportItem>
                {
                    new ManualImportItem { FullPath = src1, MatchedAudiobookId = book.Id },
                    new ManualImportItem { FullPath = src2, MatchedAudiobookId = book.Id }
                }
            };

            // Act
            await controller.Start(request);

            // Assert: both files should exist in the audiobook base path, second should have a suffix if name collided
            var diskFiles = Directory.GetFiles(basePath, "*", SearchOption.AllDirectories).Select(p => Path.GetFileName(p)).ToList();

            Assert.Contains(diskFiles, f => f.Equals("Batch Book.mp3", StringComparison.OrdinalIgnoreCase) || f.StartsWith("Batch Book"));
            // Expect at least two files (the second should be suffixed)
            Assert.True(diskFiles.Count >= 2, "Expected at least two files in destination (one suffixed for the collision)");

            // Cleanup
            TryDeleteDirectory(basePath);
            TryDeleteDirectory(srcDir);
        }

        [Fact]
        public async Task InteractiveManualImport_MultipartFiles_UsesStableNaturalOrderAndNumbering()
        {
            var basePath = Path.Join(Path.GetTempPath(), "listenarr-manual-ordered", Guid.NewGuid().ToString());
            Directory.CreateDirectory(basePath);

            var book = new Audiobook { Id = 84, Title = "Ordered Book", BasePath = basePath };

            var srcDir = Path.Join(Path.GetTempPath(), "listenarr-manual-ordered-src", Guid.NewGuid().ToString());
            Directory.CreateDirectory(srcDir);
            var part10 = Path.Join(srcDir, "Part 10.mp3");
            var part2 = Path.Join(srcDir, "Part 2.mp3");
            var part1 = Path.Join(srcDir, "Part 1.mp3");
            await File.WriteAllTextAsync(part10, "ten");
            await File.WriteAllTextAsync(part2, "two");
            await File.WriteAllTextAsync(part1, "one");

            var repoMock = new Mock<IAudiobookRepository>();
            repoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => id == book.Id ? book : null);

            var metadataMock = new Mock<IMetadataService>();
            metadataMock.Setup(m => m.ExtractFileMetadataAsync(It.IsAny<string>()))
                .ReturnsAsync(new AudioMetadata { Title = "Ordered Book", Format = "mp3", Bitrate = 128000 });

            var settings = new ApplicationSettings
            {
                OutputPath = basePath,
                FolderNamingPattern = "{Author}",
                FileNamingPattern = "{Title}",
                MultiFileNamingPattern = "{Title}-{DiskNumber:00}"
            };

            var configMock = new Mock<IConfigurationService>();
            configMock.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(settings);

            var scanMock = new Mock<IScanQueueService>();
            scanMock.Setup(s => s.EnqueueScanAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(Guid.NewGuid());

            var rootFolderMock = new Mock<IRootFolderService>();
            rootFolderMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new System.Collections.Generic.List<Listenarr.Domain.Models.RootFolder>());

            var controller = new ManualImportController(
                Mock.Of<Microsoft.Extensions.Logging.ILogger<ManualImportController>>(),
                repoMock.Object,
                metadataMock.Object,
                new FileNamingService(configMock.Object, NullLogger<FileNamingService>.Instance),
                configMock.Object,
                scanMock.Object,
                rootFolderMock.Object,
                Mock.Of<IFileMover>()
            );

            var request = new ManualImportRequest
            {
                Path = srcDir,
                Mode = "interactive",
                InputMode = "copy",
                Items = new System.Collections.Generic.List<ManualImportItem>
                {
                    new ManualImportItem { FullPath = part10, MatchedAudiobookId = book.Id },
                    new ManualImportItem { FullPath = part2, MatchedAudiobookId = book.Id },
                    new ManualImportItem { FullPath = part1, MatchedAudiobookId = book.Id }
                }
            };

            var action = await controller.Start(request);
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(action.Result);

            var diskFiles = Directory.GetFiles(basePath, "*", SearchOption.AllDirectories)
                .Select(Path.GetFileName)
                .ToList();

            Assert.Contains("Ordered Book-01.mp3", diskFiles);
            Assert.Contains("Ordered Book-02.mp3", diskFiles);
            Assert.Contains("Ordered Book-10.mp3", diskFiles);
            Assert.Equal("one", await File.ReadAllTextAsync(Path.Join(basePath, "Ordered Book-01.mp3")));
            Assert.Equal("two", await File.ReadAllTextAsync(Path.Join(basePath, "Ordered Book-02.mp3")));
            Assert.Equal("ten", await File.ReadAllTextAsync(Path.Join(basePath, "Ordered Book-10.mp3")));
        }

        [Fact]
        public async Task InteractiveManualImport_ForewordAndChapterOne_AvoidsDuplicateNumberedNames()
        {
            var basePath = Path.Join(Path.GetTempPath(), "listenarr-manual-foreword", Guid.NewGuid().ToString());
            Directory.CreateDirectory(basePath);

            var book = new Audiobook { Id = 126, Title = "Jack of Shadows", BasePath = basePath };

            var srcDir = Path.Join(Path.GetTempPath(), "listenarr-manual-foreword-src", Guid.NewGuid().ToString());
            Directory.CreateDirectory(srcDir);
            var foreword = Path.Join(srcDir, "(Foreword by Joe Haldeman).mp3");
            var chapter1 = Path.Join(srcDir, "Chapter 01.mp3");
            var chapter2 = Path.Join(srcDir, "Chapter 02.mp3");
            await File.WriteAllTextAsync(foreword, "foreword");
            await File.WriteAllTextAsync(chapter1, "chapter1");
            await File.WriteAllTextAsync(chapter2, "chapter2");

            var repoMock = new Mock<IAudiobookRepository>();
            repoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => id == book.Id ? book : null);
            repoMock.Setup(r => r.UpdateAsync(It.IsAny<Audiobook>())).ReturnsAsync(true);

            var metadataMock = new Mock<IMetadataService>();
            metadataMock.Setup(m => m.ExtractFileMetadataAsync(It.Is<string>(path => path.EndsWith("(Foreword by Joe Haldeman).mp3", StringComparison.OrdinalIgnoreCase))))
                .ReturnsAsync(new AudioMetadata { Title = "Jack of Shadows", Format = "mp3", TrackNumber = 1 });
            metadataMock.Setup(m => m.ExtractFileMetadataAsync(It.Is<string>(path => path.EndsWith("Chapter 01.mp3", StringComparison.OrdinalIgnoreCase))))
                .ReturnsAsync(new AudioMetadata { Title = "Jack of Shadows", Format = "mp3", TrackNumber = 1 });
            metadataMock.Setup(m => m.ExtractFileMetadataAsync(It.Is<string>(path => path.EndsWith("Chapter 02.mp3", StringComparison.OrdinalIgnoreCase))))
                .ReturnsAsync(new AudioMetadata { Title = "Jack of Shadows", Format = "mp3", TrackNumber = 2 });

            var settings = new ApplicationSettings
            {
                OutputPath = basePath,
                FolderNamingPattern = "",
                FileNamingPattern = "{Title}",
                MultiFileNamingPattern = "{Title}-{DiskNumber:00}"
            };

            var configMock = new Mock<IConfigurationService>();
            configMock.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(settings);

            var scanMock = new Mock<IScanQueueService>();
            scanMock.Setup(s => s.EnqueueScanAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(Guid.NewGuid());

            var rootFolderMock = new Mock<IRootFolderService>();
            rootFolderMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new System.Collections.Generic.List<Listenarr.Domain.Models.RootFolder>());

            var controller = new ManualImportController(
                Mock.Of<Microsoft.Extensions.Logging.ILogger<ManualImportController>>(),
                repoMock.Object,
                metadataMock.Object,
                new FileNamingService(configMock.Object, NullLogger<FileNamingService>.Instance),
                configMock.Object,
                scanMock.Object,
                rootFolderMock.Object,
                Mock.Of<IFileMover>()
            );

            var request = new ManualImportRequest
            {
                Path = srcDir,
                Mode = "interactive",
                InputMode = "copy",
                Items = new System.Collections.Generic.List<ManualImportItem>
                {
                    new ManualImportItem { FullPath = foreword, MatchedAudiobookId = book.Id },
                    new ManualImportItem { FullPath = chapter1, MatchedAudiobookId = book.Id },
                    new ManualImportItem { FullPath = chapter2, MatchedAudiobookId = book.Id }
                }
            };

            var action = await controller.Start(request);
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(action.Result);

            var diskFiles = Directory.GetFiles(basePath, "*", SearchOption.AllDirectories)
                .Select(Path.GetFileName)
                .ToList();

            Assert.Contains("Jack of Shadows-01.mp3", diskFiles);
            Assert.Contains("Jack of Shadows-02.mp3", diskFiles);
            Assert.Contains("Jack of Shadows-03.mp3", diskFiles);
            Assert.DoesNotContain("Jack of Shadows-01 (1).mp3", diskFiles, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task InteractiveManualImport_MultiFileBatch_EnqueuesSingleCommonDirectoryScan()
        {
            var outputRoot = Path.Join(Path.GetTempPath(), "listenarr-manual-scan-root", Guid.NewGuid().ToString());
            Directory.CreateDirectory(outputRoot);

            var book = new Audiobook { Id = 222, Title = "Jack of Shadows", Authors = new System.Collections.Generic.List<string> { "Roger Zelazny" }, BasePath = outputRoot };

            var srcDir = Path.Join(Path.GetTempPath(), "listenarr-manual-scan-src", Guid.NewGuid().ToString());
            Directory.CreateDirectory(srcDir);
            var disc1 = Path.Join(srcDir, "Disc 1.mp3");
            var disc2 = Path.Join(srcDir, "Disc 2.mp3");
            await File.WriteAllTextAsync(disc1, "disc1");
            await File.WriteAllTextAsync(disc2, "disc2");

            var repoMock = new Mock<IAudiobookRepository>();
            repoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => id == book.Id ? book : null);
            repoMock.Setup(r => r.UpdateAsync(It.IsAny<Audiobook>())).ReturnsAsync(true);

            var metadataMock = new Mock<IMetadataService>();
            metadataMock.Setup(m => m.ExtractFileMetadataAsync(It.Is<string>(path => path.EndsWith("Disc 1.mp3", StringComparison.OrdinalIgnoreCase))))
                .ReturnsAsync(new AudioMetadata { Title = "Jack of Shadows", Format = "mp3", DiscNumber = 1, TrackNumber = 1 });
            metadataMock.Setup(m => m.ExtractFileMetadataAsync(It.Is<string>(path => path.EndsWith("Disc 2.mp3", StringComparison.OrdinalIgnoreCase))))
                .ReturnsAsync(new AudioMetadata { Title = "Jack of Shadows", Format = "mp3", DiscNumber = 2, TrackNumber = 2 });

            var settings = new ApplicationSettings
            {
                OutputPath = outputRoot,
                FolderNamingPattern = "{Author}/{Title}",
                FileNamingPattern = "{Title}",
                MultiFileNamingPattern = "Disc {DiskNumber:00}/{Title}-{DiskNumber:00}"
            };

            var configMock = new Mock<IConfigurationService>();
            configMock.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(settings);

            var expectedScanPath = Path.Join(outputRoot, "Roger Zelazny", "Jack of Shadows");
            var scanMock = new Mock<IScanQueueService>();
            scanMock.Setup(s => s.EnqueueScanAsync(book.Id, expectedScanPath)).ReturnsAsync(Guid.NewGuid());

            var rootFolderMock = new Mock<IRootFolderService>();
            rootFolderMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new System.Collections.Generic.List<Listenarr.Domain.Models.RootFolder>());

            var controller = new ManualImportController(
                Mock.Of<Microsoft.Extensions.Logging.ILogger<ManualImportController>>(),
                repoMock.Object,
                metadataMock.Object,
                new FileNamingService(configMock.Object, NullLogger<FileNamingService>.Instance),
                configMock.Object,
                scanMock.Object,
                rootFolderMock.Object,
                Mock.Of<IFileMover>()
            );

            var request = new ManualImportRequest
            {
                Path = srcDir,
                Mode = "interactive",
                InputMode = "copy",
                Items = new System.Collections.Generic.List<ManualImportItem>
                {
                    new ManualImportItem { FullPath = disc1, MatchedAudiobookId = book.Id },
                    new ManualImportItem { FullPath = disc2, MatchedAudiobookId = book.Id }
                }
            };

            var action = await controller.Start(request);
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(action.Result);

            Assert.Equal(expectedScanPath, book.BasePath);
            scanMock.Verify(s => s.EnqueueScanAsync(book.Id, expectedScanPath), Times.Once);
            scanMock.Verify(s => s.EnqueueScanAsync(book.Id, It.IsAny<string>()), Times.Once);
            repoMock.Verify(r => r.UpdateAsync(It.Is<Audiobook>(a => a.Id == book.Id && a.BasePath == expectedScanPath)), Times.AtLeastOnce);
        }

        [Fact]
        public async Task InteractiveManualImport_MoveWithCompanionFiles_ImportsSidecarsAndDeletesSourceFolder()
        {
            var destinationRoot = Path.Join(Path.GetTempPath(), "listenarr-manual-companion-dest", Guid.NewGuid().ToString());
            Directory.CreateDirectory(destinationRoot);

            var book = new Audiobook { Id = 333, Title = "Companion Book", BasePath = destinationRoot };

            var sourceDir = Path.Join(Path.GetTempPath(), "listenarr-manual-companion-src", Guid.NewGuid().ToString());
            Directory.CreateDirectory(sourceDir);
            var audioFile = Path.Join(sourceDir, "Track 01.mp3");
            var coverFile = Path.Join(sourceDir, "cover.jpg");
            var notesFile = Path.Join(sourceDir, "notes.txt");
            await File.WriteAllTextAsync(audioFile, "audio");
            await File.WriteAllTextAsync(coverFile, "cover");
            await File.WriteAllTextAsync(notesFile, "notes");

            var repoMock = new Mock<IAudiobookRepository>();
            repoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => id == book.Id ? book : null);
            repoMock.Setup(r => r.UpdateAsync(It.IsAny<Audiobook>())).ReturnsAsync(true);

            var metadataMock = new Mock<IMetadataService>();
            metadataMock.Setup(m => m.ExtractFileMetadataAsync(audioFile))
                .ReturnsAsync(new AudioMetadata { Title = "Companion Book", Format = "mp3", Bitrate = 128000 });
            metadataMock.Setup(m => m.WriteAsinTagAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            var settings = new ApplicationSettings
            {
                OutputPath = destinationRoot,
                FolderNamingPattern = "",
                FileNamingPattern = "{Title}",
                ImportBlacklistExtensions = new System.Collections.Generic.List<string>()
            };

            var configMock = new Mock<IConfigurationService>();
            configMock.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(settings);

            var scanMock = new Mock<IScanQueueService>();
            scanMock.Setup(s => s.EnqueueScanAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(Guid.NewGuid());

            var rootFolderMock = new Mock<IRootFolderService>();
            rootFolderMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new System.Collections.Generic.List<Listenarr.Domain.Models.RootFolder>());

            var controller = new ManualImportController(
                Mock.Of<Microsoft.Extensions.Logging.ILogger<ManualImportController>>(),
                repoMock.Object,
                metadataMock.Object,
                new FileNamingService(configMock.Object, NullLogger<FileNamingService>.Instance),
                configMock.Object,
                scanMock.Object,
                rootFolderMock.Object,
                Mock.Of<IFileMover>()
            );

            var request = new ManualImportRequest
            {
                Path = sourceDir,
                Mode = "interactive",
                InputMode = "move",
                IncludeCompanionFiles = true,
                CleanupEmptySourceFolders = true,
                Items = new System.Collections.Generic.List<ManualImportItem>
                {
                    new ManualImportItem { FullPath = audioFile, MatchedAudiobookId = book.Id }
                }
            };

            var action = await controller.Start(request);
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(action.Result);

            Assert.True(File.Exists(Path.Join(destinationRoot, "Companion Book.mp3")));
            Assert.True(File.Exists(Path.Join(destinationRoot, "cover.jpg")));
            Assert.True(File.Exists(Path.Join(destinationRoot, "notes.txt")));
            Assert.False(Directory.Exists(sourceDir));

            TryDeleteDirectory(destinationRoot);
        }

        [Fact]
        public async Task InteractiveManualImport_CompanionPass_SkipsDifferentAudiobookAudioInSameFolder()
        {
            var destinationRoot = Path.Join(Path.GetTempPath(), "listenarr-manual-mixed-dest", Guid.NewGuid().ToString());
            Directory.CreateDirectory(destinationRoot);

            var book = new Audiobook { Id = 334, Title = "Companion Book", BasePath = destinationRoot };

            var sourceDir = Path.Join(Path.GetTempPath(), "listenarr-manual-mixed-src", Guid.NewGuid().ToString());
            Directory.CreateDirectory(sourceDir);
            var selectedAudio = Path.Join(sourceDir, "Companion Book.mp3");
            var foreignAudio = Path.Join(sourceDir, "Different Book.mp3");
            var coverFile = Path.Join(sourceDir, "cover.jpg");
            await File.WriteAllTextAsync(selectedAudio, "selected");
            await File.WriteAllTextAsync(foreignAudio, "foreign");
            await File.WriteAllTextAsync(coverFile, "cover");

            var repoMock = new Mock<IAudiobookRepository>();
            repoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => id == book.Id ? book : null);
            repoMock.Setup(r => r.UpdateAsync(It.IsAny<Audiobook>())).ReturnsAsync(true);

            var metadataMock = new Mock<IMetadataService>();
            metadataMock.Setup(m => m.ExtractFileMetadataAsync(selectedAudio))
                .ReturnsAsync(new AudioMetadata
                {
                    Title = "Companion Book",
                    Album = "Companion Book",
                    Artist = "Author A",
                    Format = "mp3"
                });
            metadataMock.Setup(m => m.ExtractFileMetadataAsync(foreignAudio))
                .ReturnsAsync(new AudioMetadata
                {
                    Title = "Different Book",
                    Album = "Different Book",
                    Artist = "Author A",
                    Format = "mp3"
                });
            metadataMock.Setup(m => m.WriteAsinTagAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            var settings = new ApplicationSettings
            {
                OutputPath = destinationRoot,
                FolderNamingPattern = "",
                FileNamingPattern = "{Title}",
                ImportBlacklistExtensions = new System.Collections.Generic.List<string>()
            };

            var configMock = new Mock<IConfigurationService>();
            configMock.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(settings);

            var scanMock = new Mock<IScanQueueService>();
            scanMock.Setup(s => s.EnqueueScanAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(Guid.NewGuid());

            var rootFolderMock = new Mock<IRootFolderService>();
            rootFolderMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new System.Collections.Generic.List<Listenarr.Domain.Models.RootFolder>());

            var controller = new ManualImportController(
                Mock.Of<Microsoft.Extensions.Logging.ILogger<ManualImportController>>(),
                repoMock.Object,
                metadataMock.Object,
                new FileNamingService(configMock.Object, NullLogger<FileNamingService>.Instance),
                configMock.Object,
                scanMock.Object,
                rootFolderMock.Object,
                Mock.Of<IFileMover>()
            );

            var request = new ManualImportRequest
            {
                Path = sourceDir,
                Mode = "interactive",
                InputMode = "copy",
                IncludeCompanionFiles = true,
                Items = new System.Collections.Generic.List<ManualImportItem>
                {
                    new ManualImportItem { FullPath = selectedAudio, MatchedAudiobookId = book.Id }
                }
            };

            var action = await controller.Start(request);
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(action.Result);

            Assert.True(File.Exists(Path.Join(destinationRoot, "Companion Book.mp3")));
            Assert.True(File.Exists(Path.Join(destinationRoot, "cover.jpg")));
            Assert.False(File.Exists(Path.Join(destinationRoot, "Different Book.mp3")));

            TryDeleteDirectory(destinationRoot);
            TryDeleteDirectory(sourceDir);
        }
    }
}

