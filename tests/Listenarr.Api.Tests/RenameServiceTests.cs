using Listenarr.Api.Models;
using Listenarr.Api.Services;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class RenameServiceTests : IDisposable
    {
        private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "ListenarrRenameTests", Guid.NewGuid().ToString("N"));
        private readonly List<ListenArrDbContext> _contexts = new();

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempRoot))
                {
                    Directory.Delete(_tempRoot, true);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Ignoring cleanup failure for '{_tempRoot}': {ex.Message}");
            }

            foreach (var context in _contexts)
            {
                context.Dispose();
            }
        }

        [Fact]
        public async Task PreviewRename_UsesExtendedMetadataVariables()
        {
            var settings = new ApplicationSettings
            {
                OutputPath = _tempRoot,
                FolderNamingPattern = "{Author}/{Title}/{Edition}/{Narrator}/{Publisher}/{Language}/{Asin}",
                FileNamingPattern = "{Title}"
            };

            var (service, db, _) = BuildService(settings);
            db.Audiobooks.Add(new Audiobook
            {
                Id = 1,
                Title = "Dune",
                Authors = new List<string> { "Frank Herbert" },
                Narrators = new List<string> { "Scott Brick" },
                Publisher = "Audible",
                Language = "English",
                Asin = "B000TEST",
                Edition = "Anniversary",
                BasePath = Path.Combine(_tempRoot, "Wrong", "Folder"),
                Files = new List<AudiobookFile>
                {
                    new() { Id = 11, AudiobookId = 1, Path = Path.Combine(_tempRoot, "Wrong", "Folder", "old-name.m4b"), Format = "m4b" }
                }
            });
            await db.SaveChangesAsync();

            var previews = await service.PreviewRenameAsync(new[] { 1 });

            var preview = Assert.Single(previews);
            Assert.True(preview.HasChanges);
            Assert.Contains("Anniversary", preview.NewFolderPath);
            Assert.Contains("Scott Brick", preview.NewFolderPath);
            Assert.Contains("Audible", preview.NewFolderPath);
            Assert.Contains("English", preview.NewFolderPath);
            Assert.Contains("B000TEST", preview.NewFolderPath);
        }

        [Fact]
        public async Task PreviewRename_PreservesCustomBasePath()
        {
            var outputPath = Path.Combine(_tempRoot, "library");
            var customBase = Path.Combine(_tempRoot, "custom-shelf", "Dune");
            Directory.CreateDirectory(customBase);

            var settings = new ApplicationSettings
            {
                OutputPath = outputPath,
                FolderNamingPattern = "{Author}/{Title}",
                FileNamingPattern = "{Title}"
            };

            var (service, db, _) = BuildService(settings);
            db.Audiobooks.Add(new Audiobook
            {
                Id = 2,
                Title = "Dune",
                Authors = new List<string> { "Frank Herbert" },
                BasePath = customBase,
                Files = new List<AudiobookFile>
                {
                    new() { Id = 21, AudiobookId = 2, Path = Path.Combine(customBase, "wrong-name.m4b"), Format = "m4b" }
                }
            });
            await db.SaveChangesAsync();

            var previews = await service.PreviewRenameAsync(new[] { 2 });

            var preview = Assert.Single(previews);
            Assert.False(preview.FolderChanged);
            Assert.Equal(NormalizePath(customBase), preview.NewFolderPath);
            Assert.All(preview.FileRenames, file => Assert.StartsWith(NormalizePath(customBase), file.NewPath!, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task ExecuteRename_RejectsPathsOutsideAllowedRoots()
        {
            var libraryRoot = Path.Combine(_tempRoot, "library");
            var bookFolder = Path.Combine(libraryRoot, "Book");
            Directory.CreateDirectory(bookFolder);
            var sourcePath = Path.Combine(bookFolder, "Book.m4b");
            await File.WriteAllTextAsync(sourcePath, "test");

            var settings = new ApplicationSettings
            {
                OutputPath = libraryRoot,
                FolderNamingPattern = "{Author}/{Title}",
                FileNamingPattern = "{Title}"
            };

            var (service, db, _) = BuildService(settings);
            db.Audiobooks.Add(new Audiobook
            {
                Id = 3,
                Title = "Book",
                Authors = new List<string> { "Author" },
                BasePath = bookFolder,
                Files = new List<AudiobookFile>
                {
                    new() { Id = 31, AudiobookId = 3, Path = sourcePath, Format = "m4b" }
                }
            });
            await db.SaveChangesAsync();

            var results = await service.ExecuteRenameAsync(new List<RenameOperation>
            {
                new()
                {
                    AudiobookId = 3,
                    FileRenames = new List<FileRenameOperation>
                    {
                        new()
                        {
                            FileId = 31,
                            CurrentPath = sourcePath,
                            NewPath = Path.Combine(_tempRoot, "outside", "Book.m4b")
                        }
                    }
                }
            });

            var result = Assert.Single(results);
            Assert.False(result.Success);
            var fileResult = Assert.Single(result.RenamedFiles);
            Assert.False(fileResult.Success);
            Assert.Contains("outside", fileResult.Error!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ExecuteRename_RejectsFileIdsThatDoNotBelongToAudiobook()
        {
            var libraryRoot = Path.Combine(_tempRoot, "library");
            var bookFolder = Path.Combine(libraryRoot, "Book");
            Directory.CreateDirectory(bookFolder);
            var rogueSourcePath = Path.Combine(bookFolder, "rogue-file.m4b");
            var rogueTargetPath = Path.Combine(bookFolder, "moved-rogue-file.m4b");
            await File.WriteAllTextAsync(rogueSourcePath, "rogue");

            var settings = new ApplicationSettings
            {
                OutputPath = libraryRoot,
                FolderNamingPattern = "{Author}/{Title}",
                FileNamingPattern = "{Title}"
            };

            var (service, db, _) = BuildService(settings);
            db.Audiobooks.Add(new Audiobook
            {
                Id = 5,
                Title = "Book",
                Authors = new List<string> { "Author" },
                BasePath = bookFolder,
                Files = new List<AudiobookFile>
                {
                    new() { Id = 51, AudiobookId = 5, Path = Path.Combine(bookFolder, "tracked-file.m4b"), Format = "m4b" }
                }
            });
            await db.SaveChangesAsync();

            var results = await service.ExecuteRenameAsync(new List<RenameOperation>
            {
                new()
                {
                    AudiobookId = 5,
                    FileRenames = new List<FileRenameOperation>
                    {
                        new()
                        {
                            FileId = 999,
                            CurrentPath = rogueSourcePath,
                            NewPath = rogueTargetPath
                        }
                    }
                }
            });

            var result = Assert.Single(results);
            Assert.False(result.Success);
            var fileResult = Assert.Single(result.RenamedFiles);
            Assert.False(fileResult.Success);
            Assert.Equal("File does not belong to this audiobook.", fileResult.Error);
            Assert.True(File.Exists(rogueSourcePath));
            Assert.False(File.Exists(rogueTargetPath));
        }

        [Fact]
        public async Task ExecuteRename_RejectsSourcePathsThatDoNotMatchTrackedFile()
        {
            var libraryRoot = Path.Combine(_tempRoot, "library");
            var bookFolder = Path.Combine(libraryRoot, "Book");
            Directory.CreateDirectory(bookFolder);
            var trackedSourcePath = Path.Combine(bookFolder, "tracked-file.m4b");
            var rogueSourcePath = Path.Combine(bookFolder, "rogue-file.m4b");
            var rogueTargetPath = Path.Combine(bookFolder, "moved-rogue-file.m4b");
            await File.WriteAllTextAsync(trackedSourcePath, "tracked");
            await File.WriteAllTextAsync(rogueSourcePath, "rogue");

            var settings = new ApplicationSettings
            {
                OutputPath = libraryRoot,
                FolderNamingPattern = "{Author}/{Title}",
                FileNamingPattern = "{Title}"
            };

            var (service, db, _) = BuildService(settings);
            db.Audiobooks.Add(new Audiobook
            {
                Id = 6,
                Title = "Book",
                Authors = new List<string> { "Author" },
                BasePath = bookFolder,
                FilePath = trackedSourcePath,
                Files = new List<AudiobookFile>
                {
                    new() { Id = 61, AudiobookId = 6, Path = trackedSourcePath, Format = "m4b" }
                }
            });
            await db.SaveChangesAsync();

            var results = await service.ExecuteRenameAsync(new List<RenameOperation>
            {
                new()
                {
                    AudiobookId = 6,
                    FileRenames = new List<FileRenameOperation>
                    {
                        new()
                        {
                            FileId = 61,
                            CurrentPath = rogueSourcePath,
                            NewPath = rogueTargetPath
                        }
                    }
                }
            });

            var result = Assert.Single(results);
            Assert.False(result.Success);
            var fileResult = Assert.Single(result.RenamedFiles);
            Assert.False(fileResult.Success);
            Assert.Equal("Source path does not match the tracked audiobook file.", fileResult.Error);
            Assert.True(File.Exists(trackedSourcePath));
            Assert.True(File.Exists(rogueSourcePath));
            Assert.False(File.Exists(rogueTargetPath));
        }

        [Fact]
        public async Task ExecuteRename_RecomputesBasePathAfterPartialFileFailures()
        {
            var libraryRoot = Path.Combine(_tempRoot, "library");
            var sourceFolder = Path.Combine(libraryRoot, "Old");
            var targetFolder = Path.Combine(libraryRoot, "Author", "Book");
            Directory.CreateDirectory(sourceFolder);

            var firstSourcePath = Path.Combine(sourceFolder, "Part 1.m4b");
            var secondSourcePath = Path.Combine(sourceFolder, "Part 2.m4b");
            var firstTargetPath = Path.Combine(targetFolder, "Part 1.m4b");
            var secondTargetPath = Path.Combine(targetFolder, "Part 2.m4b");
            await File.WriteAllTextAsync(firstSourcePath, "one");
            await File.WriteAllTextAsync(secondSourcePath, "two");

            var settings = new ApplicationSettings
            {
                OutputPath = libraryRoot,
                FolderNamingPattern = "{Author}/{Title}",
                FileNamingPattern = "{Title}"
            };

            var (service, db, dbName) = BuildService(settings, fileMover =>
            {
                fileMover.Setup(mover => mover.MoveFileAsync(It.IsAny<string>(), It.Is<string>(dest => dest.EndsWith("Part 2.m4b", StringComparison.OrdinalIgnoreCase))))
                    .ReturnsAsync(false);
            });

            db.Audiobooks.Add(new Audiobook
            {
                Id = 7,
                Title = "Book",
                Authors = new List<string> { "Author" },
                BasePath = sourceFolder,
                FilePath = firstSourcePath,
                Files = new List<AudiobookFile>
                {
                    new() { Id = 71, AudiobookId = 7, Path = firstSourcePath, Format = "m4b" },
                    new() { Id = 72, AudiobookId = 7, Path = secondSourcePath, Format = "m4b" }
                }
            });
            await db.SaveChangesAsync();

            var results = await service.ExecuteRenameAsync(new List<RenameOperation>
            {
                new()
                {
                    AudiobookId = 7,
                    NewFolderPath = targetFolder,
                    FileRenames = new List<FileRenameOperation>
                    {
                        new()
                        {
                            FileId = 71,
                            CurrentPath = firstSourcePath,
                            NewPath = firstTargetPath
                        },
                        new()
                        {
                            FileId = 72,
                            CurrentPath = secondSourcePath,
                            NewPath = secondTargetPath
                        }
                    }
                }
            });

            var result = Assert.Single(results);
            Assert.False(result.Success);
            Assert.Equal(2, result.RenamedFiles.Count);
            Assert.Contains(result.RenamedFiles, item => item.FileId == 71 && item.Success);
            Assert.Contains(result.RenamedFiles, item => item.FileId == 72 && !item.Success);

            await using var verifyDb = CreateContext(dbName);
            var saved = await verifyDb.Audiobooks.Include(a => a.Files).SingleAsync(a => a.Id == 7);

            Assert.Equal(NormalizePath(libraryRoot), NormalizePath(saved.BasePath));
            Assert.NotEqual(NormalizePath(targetFolder), NormalizePath(saved.BasePath));
            Assert.Contains(saved.Files!, file => file.Id == 71 && NormalizePath(file.Path) == NormalizePath(firstTargetPath));
            Assert.Contains(saved.Files!, file => file.Id == 72 && NormalizePath(file.Path) == NormalizePath(secondSourcePath));
            Assert.True(File.Exists(firstTargetPath));
            Assert.True(File.Exists(secondSourcePath));
            Assert.False(File.Exists(secondTargetPath));
        }

        [Fact]
        public async Task ExecuteRename_MovesFileAndUpdatesDatabasePaths()
        {
            var libraryRoot = Path.Combine(_tempRoot, "library");
            var sourceFolder = Path.Combine(libraryRoot, "Old");
            var targetFolder = Path.Combine(libraryRoot, "Author", "Book");
            Directory.CreateDirectory(sourceFolder);
            var sourcePath = Path.Combine(sourceFolder, "old-name.m4b");
            var targetPath = Path.Combine(targetFolder, "Book.m4b");
            await File.WriteAllTextAsync(sourcePath, "test");

            var settings = new ApplicationSettings
            {
                OutputPath = libraryRoot,
                FolderNamingPattern = "{Author}/{Title}",
                FileNamingPattern = "{Title}"
            };

            var (service, db, dbName) = BuildService(settings);
            db.Audiobooks.Add(new Audiobook
            {
                Id = 4,
                Title = "Book",
                Authors = new List<string> { "Author" },
                BasePath = sourceFolder,
                FilePath = sourcePath,
                Files = new List<AudiobookFile>
                {
                    new() { Id = 41, AudiobookId = 4, Path = sourcePath, Format = "m4b" }
                }
            });
            await db.SaveChangesAsync();

            var results = await service.ExecuteRenameAsync(new List<RenameOperation>
            {
                new()
                {
                    AudiobookId = 4,
                    NewFolderPath = targetFolder,
                    FileRenames = new List<FileRenameOperation>
                    {
                        new()
                        {
                            FileId = 41,
                            CurrentPath = sourcePath,
                            NewPath = targetPath
                        }
                    }
                }
            });

            var result = Assert.Single(results);
            Assert.True(result.Success);
            Assert.True(File.Exists(targetPath));
            Assert.False(File.Exists(sourcePath));

            await using var verifyDb = CreateContext(dbName);
            var saved = await verifyDb.Audiobooks.Include(a => a.Files).SingleAsync(a => a.Id == 4);
            Assert.Equal(NormalizePath(targetFolder), NormalizePath(saved.BasePath));
            Assert.Equal(NormalizePath(targetPath), NormalizePath(saved.FilePath));
            Assert.Equal(NormalizePath(targetPath), NormalizePath(saved.Files!.Single().Path));
        }

        private (RenameService Service, ListenArrDbContext Db, string DbName) BuildService(
            ApplicationSettings settings,
            Action<Mock<IFileMover>>? configureFileMover = null)
        {
            var dbName = Guid.NewGuid().ToString();
            var db = CreateContext(dbName);

            var config = new Mock<IConfigurationService>();
            config.Setup(service => service.GetApplicationSettingsAsync()).ReturnsAsync(settings);

            var dbFactory = new Mock<IDbContextFactory<ListenArrDbContext>>();
            dbFactory.Setup(factory => factory.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => CreateContext(dbName));

            var fileNaming = new FileNamingService(config.Object, NullLogger<FileNamingService>.Instance);
            var fileMover = new Mock<IFileMover>();
            fileMover.Setup(mover => mover.MoveFileAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns<string, string>((source, dest) =>
                {
                    var dir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrWhiteSpace(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    File.Move(source, dest, true);
                    return Task.FromResult(true);
                });
            fileMover.Setup(mover => mover.MoveDirectoryAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns<string, string>((source, dest) =>
                {
                    var parent = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrWhiteSpace(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    Directory.Move(source, dest);
                    return Task.FromResult(true);
                });
            configureFileMover?.Invoke(fileMover);

            var service = new RenameService(
                config.Object,
                fileNaming,
                fileMover.Object,
                dbFactory.Object,
                NullLogger<RenameService>.Instance);

            return (service, db, dbName);
        }

        private ListenArrDbContext CreateContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;
            var context = new ListenArrDbContext(options);
            _contexts.Add(context);
            return context;
        }

        private static string NormalizePath(string? path) => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : Path.GetFullPath(path);
    }
}
