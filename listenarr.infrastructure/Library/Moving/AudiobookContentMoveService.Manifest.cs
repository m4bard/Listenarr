using System.Security.Cryptography;
using Listenarr.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private async Task<IReadOnlyList<MoveJobEntry>> LoadOrCreateManifestAsync(
        Guid jobId,
        string source,
        string target,
        bool targetInsideSource,
        FileSystemPathSemantics semantics,
        CancellationToken cancellationToken)
    {
        var persisted = await LoadManifestAsync(jobId, cancellationToken);
        if (persisted.Count > 0)
        {
            return persisted;
        }

        var manifest = await SnapshotSourceAsync(
            jobId,
            source,
            target,
            targetInsideSource,
            semantics,
            cancellationToken);
        await PersistManifestAsync(jobId, manifest, cancellationToken);
        return manifest;
    }

    private async Task<List<MoveJobEntry>> SnapshotSourceAsync(
        Guid jobId,
        string source,
        string target,
        bool targetInsideSource,
        FileSystemPathSemantics semantics,
        CancellationToken cancellationToken)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
        {
            throw new MoveNeedsAttentionException("Move sources cannot be symlinks or reparse points.");
        }

        var entries = new List<MoveJobEntry>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(source);
        while (pendingDirectories.Count > 0)
        {
            var directory = pendingDirectories.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (targetInsideSource && IsSameOrInside(entry, target, semantics))
                {
                    continue;
                }

                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new MoveNeedsAttentionException(
                        $"Move entry '{Path.GetRelativePath(source, entry)}' is a symlink or reparse point.");
                }

                var relativePath = Path.GetRelativePath(source, entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    entries.Add(new MoveJobEntry
                    {
                        MoveJobId = jobId,
                        RelativePath = relativePath,
                        EntryType = MoveJobEntryType.Directory,
                        LastWriteTimeUtc = Directory.GetLastWriteTimeUtc(entry)
                    });
                    pendingDirectories.Push(entry);
                    continue;
                }

                var fileInfo = new FileInfo(entry);
                entries.Add(new MoveJobEntry
                {
                    MoveJobId = jobId,
                    RelativePath = relativePath,
                    EntryType = MoveJobEntryType.File,
                    Length = fileInfo.Length,
                    LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
                    Sha256 = await ComputeSha256Async(entry, cancellationToken)
                });
            }
        }

        return entries;
    }

    private async Task PersistManifestAsync(
        Guid jobId,
        IReadOnlyCollection<MoveJobEntry> manifest,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.MoveJobs.AnyAsync(job => job.Id == jobId, cancellationToken))
        {
            return;
        }

        db.MoveJobEntries.AddRange(manifest);
        await db.SaveChangesAsync(cancellationToken);
    }

    private List<MoveJobEntry> LoadManifest(Guid jobId)
    {
        using var db = dbContextFactory.CreateDbContext();
        return db.MoveJobEntries
            .AsNoTracking()
            .Where(entry => entry.MoveJobId == jobId)
            .OrderBy(entry => entry.Id)
            .ToList();
    }

    private MoveJobPhase? LoadMoveJobPhase(Guid jobId)
    {
        using var db = dbContextFactory.CreateDbContext();
        return db.MoveJobs
            .AsNoTracking()
            .Where(job => job.Id == jobId)
            .Select(job => (MoveJobPhase?)job.Phase)
            .SingleOrDefault();
    }

    private async Task<List<MoveJobEntry>> LoadManifestAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.MoveJobEntries
            .AsNoTracking()
            .Where(entry => entry.MoveJobId == jobId)
            .OrderBy(entry => entry.Id)
            .ToListAsync(cancellationToken);
    }

    private static async Task VerifyPublishedManifestAsync(
        string destinationRoot,
        IReadOnlyCollection<MoveJobEntry> manifest,
        FileSystemPathSemantics semantics,
        CancellationToken cancellationToken)
    {
        foreach (var entry in manifest)
        {
            if (!FileSystemPathIdentity.TryResolveRelativePathWithinBase(
                destinationRoot,
                entry.RelativePath,
                semantics,
                out var destinationPath))
            {
                throw new MoveNeedsAttentionException("A manifest entry escaped the destination root.");
            }

            if (entry.EntryType == MoveJobEntryType.Directory)
            {
                if (!Directory.Exists(destinationPath))
                {
                    throw new MoveNeedsAttentionException($"Published directory is missing: {entry.RelativePath}");
                }

                continue;
            }

            if (!File.Exists(destinationPath)
                || new FileInfo(destinationPath).Length != entry.Length
                || !string.Equals(
                    await ComputeSha256Async(destinationPath, cancellationToken),
                    entry.Sha256,
                    StringComparison.Ordinal))
            {
                throw new MoveNeedsAttentionException($"Published file verification failed: {entry.RelativePath}");
            }
        }
    }

    private async Task DeleteOriginalSourceAsync(
        string source,
        string target,
        bool targetInsideSource,
        bool deleteEmptySource,
        Guid jobId,
        IReadOnlyList<MoveJobEntry> manifest,
        FileSystemPathSemantics semantics,
        CancellationToken cancellationToken)
    {
        var sourceExists = Directory.Exists(source);
        if (sourceExists && IsFilesystemRoot(source, semantics))
        {
            throw new IOException("Source path became invalid before cleanup.");
        }

        var sourceParent = Path.GetDirectoryName(source)
            ?? throw new IOException("Source parent path is unavailable.");
        var quarantineRoot = Path.Join(sourceParent, $".listenarr-quarantine-{jobId:N}");
        if (!FileSystemSafety.TryValidateMutationTarget(
            quarantineRoot,
            [sourceParent],
            out quarantineRoot,
            out var quarantineReason))
        {
            throw new MoveNeedsAttentionException(quarantineReason);
        }

        var expectedAtSource = new List<MoveJobEntry>();
        foreach (var directoryEntry in manifest.Where(entry => entry.EntryType == MoveJobEntryType.Directory))
        {
            if (FileSystemPathIdentity.TryResolveRelativePathWithinBase(
                source,
                directoryEntry.RelativePath,
                semantics,
                out var sourceDirectory)
                && Directory.Exists(sourceDirectory))
            {
                expectedAtSource.Add(directoryEntry);
            }
        }
        foreach (var entry in manifest.Where(entry =>
            entry.EntryType == MoveJobEntryType.File
            && entry.CleanupState != MoveJobEntryCleanupState.Deleted))
        {
            ResolveCleanupPaths(source, quarantineRoot, entry.RelativePath, semantics, out var sourceFile, out var quarantineFile);
            if (File.Exists(sourceFile))
            {
                expectedAtSource.Add(entry);
                continue;
            }

            if (!File.Exists(quarantineFile)
                || !string.Equals(
                    await ComputeSha256Async(quarantineFile, cancellationToken),
                    entry.Sha256,
                    StringComparison.Ordinal))
            {
                throw new MoveNeedsAttentionException(
                    $"Source file disappeared without a verified quarantine copy: {entry.RelativePath}");
            }
        }

        var current = sourceExists
            ? await SnapshotSourceAsync(
                jobId,
                source,
                target,
                targetInsideSource,
                semantics,
                cancellationToken)
            : [];
        if (!ManifestMatches(expectedAtSource, current, semantics))
        {
            throw new MoveNeedsAttentionException(
                "Source content changed after the move was planned; cleanup was blocked.");
        }

        await VerifyPublishedManifestAsync(target, manifest, semantics, cancellationToken);
        Directory.CreateDirectory(quarantineRoot);
        foreach (var entry in manifest.Where(entry =>
            entry.EntryType == MoveJobEntryType.File
            && entry.CleanupState != MoveJobEntryCleanupState.Deleted))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveCleanupPaths(source, quarantineRoot, entry.RelativePath, semantics, out var sourceFile, out var quarantineFile);

            var quarantineDirectory = Path.GetDirectoryName(quarantineFile);
            if (!string.IsNullOrEmpty(quarantineDirectory))
            {
                Directory.CreateDirectory(quarantineDirectory);
            }

            if (!File.Exists(quarantineFile))
            {
                if (!File.Exists(sourceFile))
                {
                    throw new MoveNeedsAttentionException($"Source file disappeared before cleanup: {entry.RelativePath}");
                }

                File.Move(sourceFile, quarantineFile, overwrite: false);
            }

            var quarantinedHash = await ComputeSha256Async(quarantineFile, cancellationToken);
            if (!string.Equals(quarantinedHash, entry.Sha256, StringComparison.Ordinal))
            {
                TryRestoreQuarantinedFile(quarantineFile, sourceFile);
                throw new MoveNeedsAttentionException(
                    $"Quarantined source bytes changed before cleanup: {entry.RelativePath}");
            }

            await UpdateCleanupStateAsync(
                jobId,
                entry.RelativePath,
                MoveJobEntryCleanupState.Quarantined,
                cancellationToken);
            File.Delete(quarantineFile);
            await UpdateCleanupStateAsync(
                jobId,
                entry.RelativePath,
                MoveJobEntryCleanupState.Deleted,
                cancellationToken);
        }

        foreach (var directoryEntry in manifest
            .Where(entry => entry.EntryType == MoveJobEntryType.Directory)
            .OrderByDescending(entry => entry.RelativePath.Length))
        {
            if (FileSystemPathIdentity.TryResolveRelativePathWithinBase(
                source,
                directoryEntry.RelativePath,
                semantics,
                out var directory)
                && Directory.Exists(directory)
                && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory, false);
            }
        }

        if (deleteEmptySource
            && sourceExists
            && Directory.Exists(source)
            && !Directory.EnumerateFileSystemEntries(source).Any())
        {
            Directory.Delete(source, false);
        }

        RemoveEmptyDirectoryTree(quarantineRoot, sourceParent, semantics);
    }

    private async Task UpdateCleanupStateAsync(
        Guid jobId,
        string relativePath,
        MoveJobEntryCleanupState cleanupState,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var persistedEntry = await db.MoveJobEntries.SingleOrDefaultAsync(
            entry => entry.MoveJobId == jobId && entry.RelativePath == relativePath,
            cancellationToken);
        if (persistedEntry == null)
        {
            return;
        }

        persistedEntry.CleanupState = cleanupState;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ResolveCleanupPaths(
        string source,
        string quarantineRoot,
        string relativePath,
        FileSystemPathSemantics semantics,
        out string sourceFile,
        out string quarantineFile)
    {
        if (!FileSystemPathIdentity.TryResolveRelativePathWithinBase(
            source,
            relativePath,
            semantics,
            out sourceFile)
            || !FileSystemPathIdentity.TryResolveRelativePathWithinBase(
                quarantineRoot,
                relativePath,
                semantics,
                out quarantineFile))
        {
            throw new MoveNeedsAttentionException("A manifest entry escaped its cleanup boundary.");
        }
    }

    private async Task UpdateCopyStateAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entries = await db.MoveJobEntries
            .Where(entry => entry.MoveJobId == jobId)
            .ToListAsync(cancellationToken);
        foreach (var entry in entries)
        {
            entry.CopyState = MoveJobEntryCopyState.Verified;
        }

        if (entries.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task UpdateJobPhaseAsync(
        Guid jobId,
        MoveJobPhase phase,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var job = await db.MoveJobs.SingleOrDefaultAsync(
            candidate => candidate.Id == jobId,
            cancellationToken);
        if (job == null)
        {
            return;
        }

        job.Phase = phase;
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool ManifestMatches(
        IReadOnlyCollection<MoveJobEntry> expected,
        IReadOnlyCollection<MoveJobEntry> current,
        FileSystemPathSemantics semantics)
    {
        if (expected.Count != current.Count)
        {
            return false;
        }

        var currentByPath = current.ToDictionary(
            entry => entry.RelativePath,
            semantics.Comparer);
        return expected.All(entry =>
            currentByPath.TryGetValue(entry.RelativePath, out var currentEntry)
            && currentEntry.EntryType == entry.EntryType
            && currentEntry.Length == entry.Length
            && string.Equals(currentEntry.Sha256, entry.Sha256, StringComparison.Ordinal));
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static void TryRestoreQuarantinedFile(string quarantineFile, string sourceFile)
    {
        try
        {
            if (File.Exists(quarantineFile) && !File.Exists(sourceFile))
            {
                var sourceDirectory = Path.GetDirectoryName(sourceFile);
                if (!string.IsNullOrEmpty(sourceDirectory)) Directory.CreateDirectory(sourceDirectory);
                File.Move(quarantineFile, sourceFile, overwrite: false);
            }
        }
        catch (IOException)
        {
            // The quarantine is retained for operator inspection when restoration is uncertain.
        }
    }

    private static void RemoveEmptyDirectoryTree(
        string directory,
        string boundary,
        FileSystemPathSemantics semantics)
    {
        var current = directory;
        while (Directory.Exists(current)
            && !FileSystemPathIdentity.AreEquivalent(
                current,
                boundary,
                semantics)
            && !Directory.EnumerateFileSystemEntries(current).Any())
        {
            Directory.Delete(current, false);
            current = Path.GetDirectoryName(current) ?? boundary;
        }
    }
}
