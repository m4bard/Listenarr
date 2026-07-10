using System.Security.Cryptography;
using Listenarr.Domain.Common;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private async Task<IReadOnlyList<MoveJobEntry>> LoadOrCreateManifestAsync(
        Guid jobId,
        MoveLeaseToken leaseToken,
        string source,
        string target,
        bool targetInsideSource,
        FileSystemPathSemantics sourceSemantics,
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
            sourceSemantics,
            cancellationToken);
        await PersistManifestAsync(jobId, leaseToken, manifest, cancellationToken);
        return manifest;
    }

    private async Task<List<MoveJobEntry>> SnapshotSourceAsync(
        Guid jobId,
        string source,
        string target,
        bool targetInsideSource,
        FileSystemPathSemantics sourceSemantics,
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
                if (targetInsideSource && IsSameOrInside(entry, target, sourceSemantics))
                {
                    continue;
                }

                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new MoveNeedsAttentionException(
                        $"Move entry '{Path.GetRelativePath(source, entry)}' is a symlink or reparse point.");
                }

                if (!FileSystemPathIdentity.TryGetRelativePathWithinBase(source, entry, sourceSemantics, out var relativePath))
                {
                    throw new MoveNeedsAttentionException("A source entry escaped the source root.");
                }
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

    internal static void ValidateTargetManifest(
        string target,
        IReadOnlyCollection<MoveJobEntry> manifest,
        FileSystemPathSemantics targetSemantics)
    {
        var identities = new Dictionary<string, MoveJobEntry>(StringComparer.Ordinal);
        foreach (var entry in manifest)
        {
            if (Path.IsPathRooted(entry.RelativePath))
            {
                throw new MoveNeedsAttentionException("A manifest entry must be relative to the destination root.");
            }

            if (!FileSystemPathIdentity.TryResolveRelativePathWithinBase(
                target,
                entry.RelativePath,
                targetSemantics,
                out var destinationPath))
            {
                throw new MoveNeedsAttentionException("A manifest entry escaped the destination root.");
            }

            var key = FileSystemPathIdentity.CreateKey("move-target", destinationPath, targetSemantics);
            if (identities.TryGetValue(key, out var existing))
            {
                throw new MoveNeedsAttentionException(
                    $"Target filesystem cannot represent both '{existing.RelativePath}' and '{entry.RelativePath}'.");
            }

            identities.Add(key, entry);
        }
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
        MoveLeaseToken leaseToken,
        IReadOnlyList<MoveJobEntry> manifest,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics,
        string? sourceCleanupBoundary,
        CancellationToken cancellationToken)
    {
        var sourceExists = Directory.Exists(source);
        if (sourceExists && IsFilesystemRoot(source, sourceSemantics))
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
        foreach (var directoryEntry in manifest
            .Where(entry => entry.EntryType == MoveJobEntryType.Directory)
            .Where(entry => FileSystemPathIdentity.TryResolveRelativePathWithinBase(
                source,
                entry.RelativePath,
                sourceSemantics,
                out var sourceDirectory)
                && Directory.Exists(sourceDirectory)))
        {
            expectedAtSource.Add(directoryEntry);
        }
        foreach (var entry in manifest.Where(entry =>
            entry.EntryType == MoveJobEntryType.File
            && entry.CleanupState != MoveJobEntryCleanupState.Deleted))
        {
            ResolveCleanupPaths(source, quarantineRoot, entry.RelativePath, sourceSemantics, out var sourceFile, out var quarantineFile);
            if (File.Exists(sourceFile))
            {
                if (File.Exists(quarantineFile))
                {
                    throw new MoveNeedsAttentionException(
                        $"Both source and quarantine copies exist; cleanup is ambiguous: {entry.RelativePath}");
                }

                expectedAtSource.Add(entry);
                continue;
            }

            if (entry.CleanupState == MoveJobEntryCleanupState.Quarantined
                && !File.Exists(quarantineFile))
            {
                // Quarantined is persisted only after the bytes have been verified.
                // A missing quarantine file therefore means the delete completed and
                // the process stopped before the final state update.
                await UpdateCleanupStateAsync(
                    jobId,
                    leaseToken,
                    entry.RelativePath,
                    MoveJobEntryCleanupState.Deleted,
                    cancellationToken);
                entry.CleanupState = MoveJobEntryCleanupState.Deleted;
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
                sourceSemantics,
                cancellationToken)
            : [];
        if (!ManifestMatches(expectedAtSource, current, sourceSemantics))
        {
            throw new MoveNeedsAttentionException(
                "Source content changed after the move was planned; cleanup was blocked.");
        }

        await VerifyPublishedManifestAsync(target, manifest, targetSemantics, cancellationToken);
        Directory.CreateDirectory(quarantineRoot);
        foreach (var entry in manifest.Where(entry =>
            entry.EntryType == MoveJobEntryType.File
            && entry.CleanupState != MoveJobEntryCleanupState.Deleted))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveCleanupPaths(source, quarantineRoot, entry.RelativePath, sourceSemantics, out var sourceFile, out var quarantineFile);

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

            await VerifyPublishedManifestAsync(target, [entry], targetSemantics, cancellationToken);
            await UpdateCleanupStateAsync(
                jobId,
                leaseToken,
                entry.RelativePath,
                MoveJobEntryCleanupState.Quarantined,
                cancellationToken);
            File.Delete(quarantineFile);
            await UpdateCleanupStateAsync(
                jobId,
                leaseToken,
                entry.RelativePath,
                MoveJobEntryCleanupState.Deleted,
                cancellationToken);
        }

        foreach (var directoryEntry in manifest
            .Where(entry => entry.EntryType == MoveJobEntryType.Directory)
            .OrderByDescending(entry => entry.RelativePath.Length)
            .Select(entry => new
            {
                Directory = FileSystemPathIdentity.TryResolveRelativePathWithinBase(
                    source,
                    entry.RelativePath,
                    sourceSemantics,
                    out var directory)
                    ? directory
                    : null
            })
            .Where(entry => entry.Directory != null
                && Directory.Exists(entry.Directory)
                && !Directory.EnumerateFileSystemEntries(entry.Directory).Any()))
        {
            Directory.Delete(directoryEntry.Directory!, false);
        }

        var sourceDirectoryDeleted = false;
        if (deleteEmptySource
            && sourceExists
            && Directory.Exists(source)
            && !Directory.EnumerateFileSystemEntries(source).Any())
        {
            Directory.Delete(source, false);
            sourceDirectoryDeleted = true;
        }

        // Remove the temporary quarantine before pruning source ancestors. While the
        // quarantine exists, its parent appears nonempty and stops cleanup one level early.
        RemoveEmptyDirectoryTree(quarantineRoot, sourceParent, sourceSemantics);
        if (sourceDirectoryDeleted)
        {
            RemoveEmptySourceAncestors(source, sourceCleanupBoundary, sourceSemantics);
        }
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

    private static void RemoveEmptySourceAncestors(
        string source,
        string? boundary,
        FileSystemPathSemantics semantics)
    {
        if (string.IsNullOrWhiteSpace(boundary))
        {
            return;
        }

        var fullBoundary = Path.GetFullPath(boundary);
        var parent = Path.GetDirectoryName(Path.GetFullPath(source));
        if (parent == null || !FileSystemPathIdentity.IsSameOrInside(parent, fullBoundary, semantics))
        {
            return;
        }

        RemoveEmptyDirectoryTree(parent, fullBoundary, semantics);
    }
}
