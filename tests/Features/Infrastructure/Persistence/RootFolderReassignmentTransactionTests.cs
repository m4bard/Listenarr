/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

[Trait("Area", "Persistence")]
public sealed class RootFolderReassignmentTransactionTests : BaseTests
{
    [Fact]
    public async Task ReassignAudiobooksAndRemoveAsync_RewritesAllReferencesAndDeletesRoot()
    {
        var sourcePath = FileService.GetTempDirectory("root-reassign-source");
        var targetPath = FileService.GetTempDirectory("root-reassign-target");
        var sourceRoot = new RootFolder { Name = "Source", Path = sourcePath };
        var targetRoot = new RootFolder { Name = "Target", Path = targetPath };
        await _rootFolderRepository.AddAsync(sourceRoot);
        await _rootFolderRepository.AddAsync(targetRoot);

        var sourceBasePath = Path.Join(sourcePath, "Author", "Title");
        var audiobook = await _audiobookRepository.AddAsync(new Audiobook
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
        });

        await _rootFolderRepository.ReassignAudiobooksAndRemoveAsync(
            sourceRoot.Id,
            targetRoot.Id,
            FileSystemPathSemantics.CurrentHostDefault,
            FileSystemPathSemantics.CurrentHostDefault);

        Assert.Null(await _rootFolderRepository.GetByIdAsync(sourceRoot.Id));
        var updated = await _audiobookRepository.GetByIdAsync(audiobook.Id);
        Assert.NotNull(updated);
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
        var sourcePath = FileService.GetTempDirectory("root-reassign-rollback-source");
        var targetPath = FileService.GetTempDirectory("root-reassign-rollback-target");
        var sourceRoot = new RootFolder { Name = "Source", Path = sourcePath };
        var targetRoot = new RootFolder { Name = "Target", Path = targetPath };
        await _rootFolderRepository.AddAsync(sourceRoot);
        await _rootFolderRepository.AddAsync(targetRoot);

        var sourceBasePath = Path.Join(sourcePath, "Author", "Title");
        var sourceFilePath = Path.Join(sourceBasePath, "book.m4b");
        var audiobook = await _audiobookRepository.AddAsync(new Audiobook
        {
            Title = "Book",
            BasePath = sourceBasePath,
            FilePath = sourceFilePath,
            Files = [new AudiobookFile { Path = sourceFilePath }]
        });

        var factory = _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
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
        }

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            _rootFolderRepository.ReassignAudiobooksAndRemoveAsync(
                sourceRoot.Id,
                targetRoot.Id,
                FileSystemPathSemantics.CurrentHostDefault,
                FileSystemPathSemantics.CurrentHostDefault));

        Assert.NotNull(await _rootFolderRepository.GetByIdAsync(sourceRoot.Id));
        var unchanged = await _audiobookRepository.GetByIdAsync(audiobook.Id);
        Assert.NotNull(unchanged);
        Assert.Equal(sourceBasePath, unchanged.BasePath);
        Assert.Equal(sourceFilePath, unchanged.FilePath);
        Assert.Equal(sourceFilePath, Assert.Single(unchanged.Files!).Path);
    }
}
