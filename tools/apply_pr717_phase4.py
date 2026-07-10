from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, content: str) -> None:
    (ROOT / path).write_text(content, encoding="utf-8", newline="\n")


def replace_once(path: str, old: str, new: str) -> None:
    content = read(path)
    count = content.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected one match, found {count}")
    write(path, content.replace(old, new, 1))


repo_path = "listenarr.infrastructure/Persistence/Repositories/EfRootFolderRepository.cs"
replace_once(
    repo_path,
    """            var targetRoot = roots.SingleOrDefault(root => root.Id == targetRootId)
                ?? throw new KeyNotFoundException("Reassign root not found");

            var audiobooks = await ctx.Audiobooks
""",
    """            var targetRoot = roots.SingleOrDefault(root => root.Id == targetRootId)
                ?? throw new KeyNotFoundException("Reassign root not found");

            if (await ctx.RootFolderRelocations.AnyAsync(
                    relocation => relocation.ActiveRootFolderId == sourceRootId
                        || relocation.ActiveRootFolderId == targetRootId,
                    ct))
            {
                throw new InvalidOperationException(
                    "Root folder reassignment is blocked while a relocation is active.");
            }

            var activeMovePaths = await ctx.MoveJobs
                .AsNoTracking()
                .Where(job => job.Status == MoveJobStatus.Queued
                    || job.Status == MoveJobStatus.Running
                    || job.Status == MoveJobStatus.RetryScheduled)
                .Select(job => new { job.SourcePath, job.RequestedPath })
                .ToListAsync(ct);
            if (activeMovePaths.Any(job =>
                    MoveTouchesRoot(job.SourcePath, sourceRoot.Path, sourceSemantics)
                    || MoveTouchesRoot(job.RequestedPath, sourceRoot.Path, sourceSemantics)
                    || MoveTouchesRoot(job.SourcePath, targetRoot.Path, targetSemantics)
                    || MoveTouchesRoot(job.RequestedPath, targetRoot.Path, targetSemantics)))
            {
                throw new InvalidOperationException(
                    "Root folder reassignment is blocked while an active move touches either root.");
            }

            var audiobooks = await ctx.Audiobooks
""",
)
replace_once(
    repo_path,
    """                plannedRewrites.Add((audiobook, sourceBasePath, targetBasePath));
""",
    """                if (!FileSystemPathIdentity.IsSameOrInside(
                        targetBasePath,
                        targetRoot.Path,
                        targetSemantics))
                {
                    throw new InvalidOperationException(
                        "An audiobook target path escaped the reassignment root.");
                }

                plannedRewrites.Add((audiobook, sourceBasePath, targetBasePath));
""",
)
replace_once(
    repo_path,
    """        public async Task SaveChangesAsync(CancellationToken ct = default)
""",
    """        private static bool MoveTouchesRoot(
            string? path,
            string rootPath,
            FileSystemPathSemantics semantics)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                return FileSystemPathIdentity.IsSameOrInside(path, rootPath, semantics);
            }
            catch (ArgumentException)
            {
                // Fail closed for malformed active-job paths while deciding whether a
                // destructive root-folder reassignment can proceed.
                return true;
            }
        }

        public async Task SaveChangesAsync(CancellationToken ct = default)
""",
)

path = "tests/Features/Infrastructure/Persistence/RootFolderReassignmentTransactionTests.cs"
content = read(path)
marker = """    private sealed class TestDbContextFactory(DbContextOptions<ListenArrDbContext> options)
"""
if content.count(marker) != 1:
    raise RuntimeError("root reassignment test insertion marker mismatch")
block = '''    [Fact]
    public async Task ReassignAudiobooksAndRemoveAsync_RootEqualReferencesRewriteAndRelativeReferencesRemain()
    {
        var sourcePath = Path.Join(Path.GetTempPath(), $"root-reassign-equal-source-{Guid.NewGuid():N}");
        var targetPath = Path.Join(Path.GetTempPath(), $"root-reassign-equal-target-{Guid.NewGuid():N}");
        int sourceRootId;
        int targetRootId;
        int audiobookId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var sourceRoot = new RootFolder { Name = "Source", Path = sourcePath };
            var targetRoot = new RootFolder { Name = "Target", Path = targetPath };
            var audiobook = new Audiobook
            {
                Title = "Book",
                BasePath = sourcePath,
                FilePath = sourcePath,
                ImageUrl = "https://example.com/cover.jpg",
                Files = [new AudiobookFile { Path = Path.Join("disc-1", "chapter.mp3") }]
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
        var updated = await verification.Audiobooks
            .Include(audiobook => audiobook.Files)
            .SingleAsync(audiobook => audiobook.Id == audiobookId);
        Assert.Equal(targetPath, updated.BasePath);
        Assert.Equal(targetPath, updated.FilePath);
        Assert.Equal("https://example.com/cover.jpg", updated.ImageUrl);
        Assert.Equal(Path.Join("disc-1", "chapter.mp3"), Assert.Single(updated.Files!).Path);
    }

    [Fact]
    public async Task ReassignAudiobooksAndRemoveAsync_ActiveMoveInsideTransactionBlocksAllChanges()
    {
        var sourcePath = Path.Join(Path.GetTempPath(), $"root-reassign-active-source-{Guid.NewGuid():N}");
        var targetPath = Path.Join(Path.GetTempPath(), $"root-reassign-active-target-{Guid.NewGuid():N}");
        var sourceBasePath = Path.Join(sourcePath, "Book");
        int sourceRootId;
        int targetRootId;
        int audiobookId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var sourceRoot = new RootFolder { Name = "Source", Path = sourcePath };
            var targetRoot = new RootFolder { Name = "Target", Path = targetPath };
            var audiobook = new Audiobook { Title = "Book", BasePath = sourceBasePath };
            db.RootFolders.AddRange(sourceRoot, targetRoot);
            db.Audiobooks.Add(audiobook);
            await db.SaveChangesAsync();
            db.MoveJobs.Add(new MoveJob
            {
                AudiobookId = audiobook.Id,
                SourcePath = sourceBasePath,
                RequestedPath = Path.Join(targetPath, "Book"),
                Status = MoveJobStatus.Running,
                ActiveDeduplicationKey = $"test:{Guid.NewGuid():N}"
            });
            await db.SaveChangesAsync();
            sourceRootId = sourceRoot.Id;
            targetRootId = targetRoot.Id;
            audiobookId = audiobook.Id;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _repository.ReassignAudiobooksAndRemoveAsync(
                sourceRootId,
                targetRootId,
                FileSystemPathSemantics.CurrentHostDefault,
                FileSystemPathSemantics.CurrentHostDefault));

        await using var verification = await _factory.CreateDbContextAsync();
        Assert.NotNull(await verification.RootFolders.FindAsync(sourceRootId));
        Assert.Equal(sourceBasePath, (await verification.Audiobooks.FindAsync(audiobookId))!.BasePath);
    }

    [Fact]
    public async Task ReassignAudiobooksAndRemoveAsync_ActiveRelocationInsideTransactionBlocksAllChanges()
    {
        var sourcePath = Path.Join(Path.GetTempPath(), $"root-reassign-relocation-source-{Guid.NewGuid():N}");
        var targetPath = Path.Join(Path.GetTempPath(), $"root-reassign-relocation-target-{Guid.NewGuid():N}");
        var sourceBasePath = Path.Join(sourcePath, "Book");
        int sourceRootId;
        int targetRootId;
        int audiobookId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var sourceRoot = new RootFolder { Name = "Source", Path = sourcePath };
            var targetRoot = new RootFolder { Name = "Target", Path = targetPath };
            var audiobook = new Audiobook { Title = "Book", BasePath = sourceBasePath };
            db.RootFolders.AddRange(sourceRoot, targetRoot);
            db.Audiobooks.Add(audiobook);
            await db.SaveChangesAsync();
            db.RootFolderRelocations.Add(new RootFolderRelocation
            {
                RootFolderId = sourceRoot.Id,
                ActiveRootFolderId = sourceRoot.Id,
                SourcePath = sourcePath,
                TargetPath = targetPath,
                DesiredName = "Source",
                Status = RootFolderRelocationStatus.Running
            });
            await db.SaveChangesAsync();
            sourceRootId = sourceRoot.Id;
            targetRootId = targetRoot.Id;
            audiobookId = audiobook.Id;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _repository.ReassignAudiobooksAndRemoveAsync(
                sourceRootId,
                targetRootId,
                FileSystemPathSemantics.CurrentHostDefault,
                FileSystemPathSemantics.CurrentHostDefault));

        await using var verification = await _factory.CreateDbContextAsync();
        Assert.NotNull(await verification.RootFolders.FindAsync(sourceRootId));
        Assert.Equal(sourceBasePath, (await verification.Audiobooks.FindAsync(audiobookId))!.BasePath);
    }

'''
write(path, content.replace(marker, block + marker, 1))
