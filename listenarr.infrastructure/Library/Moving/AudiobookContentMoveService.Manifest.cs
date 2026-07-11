using System.Security.Cryptography;
using Listenarr.Domain.Common;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private async Task<IReadOnlyList<MoveJobEntry>> LoadOrCreateManifestAsync(
        Guid jobId,
        MoveLeaseToken leaseToken,
        IReadOnlyList<ValidatedSourceEntry> validatedSourceEntries,
        CancellationToken cancellationToken)
    {
        var persisted = await LoadManifestAsync(jobId, cancellationToken);
        if (persisted.Count > 0)
        {
            return persisted;
        }

        var manifest = await BuildManifestAsync(
            jobId,
            validatedSourceEntries,
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
        var validatedEntries = ValidateSourceTreeForMove(
            source,
            target,
            targetInsideSource,
            sourceSemantics,
            cancellationToken);
        return await BuildManifestAsync(jobId, validatedEntries, cancellationToken);
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

        if (deleteEmptySource
            && sourceExists
            && Directory.Exists(source)
            && !IsSourceCleanupBoundary(source, sourceCleanupBoundary, sourceSemantics)
            && !Directory.EnumerateFileSystemEntries(source).Any())
        {
            Directory.Delete(source, false);
        }

        // Quarantined files preserve their relative directory structure. Remove those
        // now-empty, manifest-owned directories from deepest to shallowest before
        // removing the quarantine root itself.
        foreach (var directoryEntry in manifest
            .Where(entry => entry.EntryType == MoveJobEntryType.Directory)
            .OrderByDescending(entry => entry.RelativePath.Length))
        {
            if (FileSystemPathIdentity.TryResolveRelativePathWithinBase(
                    quarantineRoot,
                    directoryEntry.RelativePath,
                    sourceSemantics,
                    out var quarantineDirectory)
                && Directory.Exists(quarantineDirectory)
                && !Directory.EnumerateFileSystemEntries(quarantineDirectory).Any())
            {
                Directory.Delete(quarantineDirectory, false);
            }
        }

        RemoveEmptyDirectoryTree(quarantineRoot, sourceParent, sourceSemantics);
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

}
