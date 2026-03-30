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
            catch
            {
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

        private (RenameService Service, ListenArrDbContext Db, string DbName) BuildService(ApplicationSettings settings)
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
