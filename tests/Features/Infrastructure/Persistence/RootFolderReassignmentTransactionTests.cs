/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

[Trait("Area", "Persistence")]
public sealed class RootFolderReassignmentTransactionTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Join(
        Path.GetTempPath(),
        "listenarr-tests",
        $"root-reassignment-{Guid.NewGuid():N}.db");
    private IDbContextFactory<ListenArrDbContext> _factory = null!;
    private EfRootFolderRepository _repository = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        var options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite($"Data Source={_databasePath};Pooling=False;Foreign Keys=True")
            .Options;
        _factory = new TestDbContextFactory(options);
        _repository = new EfRootFolderRepository(
            _factory,
            NullLogger<EfRootFolderRepository>.Instance);
        await using var db = await _factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ReassignAudiobooksAndRemoveAsync_RewritesAllReferencesAndDeletesRoot()
    {
        var sourcePath = Path.Join(Path.GetTempPath(), $"root-reassign-source-{Guid.NewGuid():N}");
        var targetPath = Path.Join(Path.GetTempPath(), $"root-reassign-target-{Guid.NewGuid():N}");
        int sourceRootId;
        int targetRootId;
        int audiobookId;
        var sourceBasePath = Path.Join(sourcePath, "Author", "Title");
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var sourceRoot = new RootFolder { Name = "Source", Path = sourcePath };
            var targetRoot = new RootFolder { Name = "Target", Path = targetPath };
            var audiobook = new Audiobook
            {
                Title = "Book",
                BasePath = sourceBasePath,
                FilePath = Path.Join(sourceBasePath, "book.m4b"),
                ImageUrl = Path.Join(sourceBasePath, "cover.jpg"),
                Files =
                [
                    new AudiobookFile { Path = Path.Join(sourceBasePath, "book.m4b") },
                    new AudiobookFile { Path = Path.Join("disc-1", "chapter.mp3") }
                ]
            };
            db.RootFolders.AddRange(sourceRoot, targetRoot);
            db.Audiobooks.Add(audiobook);
            await db.SaveChangesAsync();
            sourceRootId = sourceRoot.Id;
            targetRootId = targetRoot.Id;
            audiobookId = audiobook.Id;
        }

        await _repository.ReassignAudiobooksAndRemoveAsync(
            sourceRootId,
            targetRootId,
            FileSystemPathSemantics.CurrentHostDefault,
            FileSystemPathSemantics.CurrentHostDefault);

        await using var verification = await _factory.CreateDbContextAsync();
        Assert.Null(await verification.RootFolders.FindAsync(sourceRootId));
        var updated = await verification.Audiobooks
            .Include(audiobook => audiobook.Files)
            .SingleAsync(audiobook => audiobook.Id == audiobookId);
        var expectedBasePath = Path.Join(targetPath, "Author", "Title");
        Assert.Equal(expectedBasePath, updated.BasePath);
        Assert.Equal(Path.Join(expectedBasePath, "book.m4b"), updated.FilePath);
        Assert.Equal(Path.Join(expectedBasePath, "cover.jpg"), updated.ImageUrl);
        Assert.Contains(updated.Files!, file => file.Path == Path.Join(expectedBasePath, "book.m4b"));
        Assert.Contains(updated.Files!, file => file.Path == Path.Join("disc-1", "chapter.mp3"));
    }

    [Fact]
    public async Task ReassignAudiobooksAndRemoveAsync_DeleteConflictRollsBackPathRewrites()
    {
        var sourcePath = Path.Join(Path.GetTempPath(), $"root-reassign-rollback-source-{Guid.NewGuid():N}");
        var targetPath = Path.Join(Path.GetTempPath(), $"root-reassign-rollback-target-{Guid.NewGuid():N}");
        int sourceRootId;
        int targetRootId;
        int audiobookId;
        var sourceBasePath = Path.Join(sourcePath, "Author", "Title");
        var sourceFilePath = Path.Join(sourceBasePath, "book.m4b");
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var sourceRoot = new RootFolder { Name = "Source", Path = sourcePath };
            var targetRoot = new RootFolder { Name = "Target", Path = targetPath };
            var audiobook = new Audiobook
            {
                Title = "Book",
                BasePath = sourceBasePath,
                FilePath = sourceFilePath,
                Files = [new AudiobookFile { Path = sourceFilePath }]
            };
            db.RootFolders.AddRange(sourceRoot, targetRoot);
            db.Audiobooks.Add(audiobook);
            await db.SaveChangesAsync();

            db.RootFolderRelocations.Add(new RootFolderRelocation
            {
                RootFolderId = sourceRoot.Id,
                SourcePath = sourcePath,
                TargetPath = targetPath,
                DesiredName = "Historical relocation",
                Status = RootFolderRelocationStatus.Completed,
                CompletedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            sourceRootId = sourceRoot.Id;
            targetRootId = targetRoot.Id;
            audiobookId = audiobook.Id;
        }

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            _repository.ReassignAudiobooksAndRemoveAsync(
                sourceRootId,
                targetRootId,
                FileSystemPathSemantics.CurrentHostDefault,
                FileSystemPathSemantics.CurrentHostDefault));

        await using var verification = await _factory.CreateDbContextAsync();
        Assert.NotNull(await verification.RootFolders.FindAsync(sourceRootId));
        var unchanged = await verification.Audiobooks
            .Include(audiobook => audiobook.Files)
            .SingleAsync(audiobook => audiobook.Id == audiobookId);
        Assert.Equal(sourceBasePath, unchanged.BasePath);
        Assert.Equal(sourceFilePath, unchanged.FilePath);
        Assert.Equal(sourceFilePath, Assert.Single(unchanged.Files!).Path);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ListenArrDbContext> options)
        : IDbContextFactory<ListenArrDbContext>
    {
        public ListenArrDbContext CreateDbContext() => new(options);

        public Task<ListenArrDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
