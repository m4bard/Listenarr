using Listenarr.Domain.Common;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private async Task DeleteOwnedDirectoryWithTombstoneAsync(
        string directoryPath,
        string markerPath,
        string ownedArtifactType,
        Guid jobId,
        string source,
        string target,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics,
        FileSystemPathSemantics directorySemantics,
        Func<Task> authorizeMutation)
    {
        var fullDirectory = Path.GetFullPath(directoryPath);
        var tombstonePath = GetCleanupTombstonePath(
            fullDirectory,
            ownedArtifactType,
            jobId);
        var expectedTombstone = CreateOwnershipMarker(
            CleanupTombstoneArtifactType,
            jobId,
            source,
            target,
            fullDirectory,
            ownedArtifactType);

        await EnsureCleanupTombstoneAsync(
            tombstonePath,
            expectedTombstone,
            sourceSemantics,
            targetSemantics,
            directorySemantics,
            authorizeMutation);
        await CompleteOwnedDirectoryCleanupAsync(
            fullDirectory,
            markerPath,
            tombstonePath,
            expectedTombstone,
            sourceSemantics,
            targetSemantics,
            directorySemantics,
            authorizeMutation);
    }

    private async Task<bool> TryCompleteOwnedDirectoryCleanupAsync(
        string directoryPath,
        string markerPath,
        string ownedArtifactType,
        Guid jobId,
        string source,
        string target,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics,
        FileSystemPathSemantics directorySemantics,
        Func<Task> authorizeMutation)
    {
        var fullDirectory = Path.GetFullPath(directoryPath);
        var tombstonePath = GetCleanupTombstonePath(
            fullDirectory,
            ownedArtifactType,
            jobId);
        var tombstoneWritePrefix = Path.GetFileName(tombstonePath) + ".writing-";
        var parent = Path.GetDirectoryName(tombstonePath)
            ?? throw new MoveNeedsAttentionException("The cleanup tombstone parent is unavailable.");
        var hasTombstoneEvidence = File.Exists(tombstonePath)
            || Directory.EnumerateFiles(
                parent,
                tombstoneWritePrefix + "*",
                SearchOption.TopDirectoryOnly).Any();
        if (!hasTombstoneEvidence)
        {
            return false;
        }

        var expectedTombstone = CreateOwnershipMarker(
            CleanupTombstoneArtifactType,
            jobId,
            source,
            target,
            fullDirectory,
            ownedArtifactType);
        await authorizeMutation();
        await RecoverOrReadOwnershipMarkerAsync(
            tombstonePath,
            expectedTombstone,
            sourceSemantics,
            targetSemantics,
            directorySemantics,
            authorizeMutation);
        await CompleteOwnedDirectoryCleanupAsync(
            fullDirectory,
            markerPath,
            tombstonePath,
            expectedTombstone,
            sourceSemantics,
            targetSemantics,
            directorySemantics,
            authorizeMutation);
        return true;
    }

    private async Task EnsureCleanupTombstoneAsync(
        string tombstonePath,
        MoveOwnershipMarker expectedTombstone,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics,
        FileSystemPathSemantics directorySemantics,
        Func<Task> authorizeMutation)
    {
        var parent = Path.GetDirectoryName(tombstonePath)
            ?? throw new MoveNeedsAttentionException("The cleanup tombstone parent is unavailable.");
        var hasPublicationEvidence = File.Exists(tombstonePath)
            || Directory.EnumerateFiles(
                parent,
                Path.GetFileName(tombstonePath) + ".writing-*",
                SearchOption.TopDirectoryOnly).Any();
        if (!hasPublicationEvidence)
        {
            await authorizeMutation();
            await PublishOwnershipMarkerAsync(
                tombstonePath,
                expectedTombstone,
                OwnershipMarkerKind.CleanupTombstone,
                authorizeMutation);
        }

        await authorizeMutation();
        await RecoverOrReadOwnershipMarkerAsync(
            tombstonePath,
            expectedTombstone,
            sourceSemantics,
            targetSemantics,
            directorySemantics,
            authorizeMutation);
    }

    private async Task CompleteOwnedDirectoryCleanupAsync(
        string directoryPath,
        string markerPath,
        string tombstonePath,
        MoveOwnershipMarker expectedTombstone,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics,
        FileSystemPathSemantics directorySemantics,
        Func<Task> authorizeMutation)
    {
        var markerKind = string.Equals(
            expectedTombstone.OwnedArtifactType,
            TemporaryDirectoryArtifactType,
            StringComparison.Ordinal)
            ? OwnershipMarkerKind.TemporaryDirectory
            : OwnershipMarkerKind.QuarantineDirectory;
        var tombstoneParent = Path.GetDirectoryName(Path.GetFullPath(tombstonePath))
            ?? throw new MoveNeedsAttentionException("The cleanup tombstone parent is unavailable.");
        var expectedDirectoryMarker = CreateOwnershipMarker(
            expectedTombstone.OwnedArtifactType
                ?? throw new MoveNeedsAttentionException("The cleanup tombstone has no owned artifact type."),
            expectedTombstone.JobId,
            expectedTombstone.Source,
            expectedTombstone.Target,
            directoryPath);
        ValidateExistingMoveDirectory(tombstoneParent, "cleanup tombstone directory");
        var tombstone = ReadOwnershipMarker(tombstonePath);
        ValidateOwnershipMarker(
            tombstone,
            expectedTombstone,
            sourceSemantics,
            targetSemantics,
            directorySemantics);

        if (TryGetExistingPathAttributes(directoryPath, out var ownedPathAttributes)
            && (ownedPathAttributes & FileAttributes.Directory) == 0)
        {
            throw new MoveNeedsAttentionException(
                "The tombstoned owned directory path is occupied by a file and was preserved for operator review.");
        }

        if (Directory.Exists(directoryPath))
        {
            ValidateExistingMoveDirectory(directoryPath, "owned cleanup directory");
            if (!FileSystemSafety.TryEnumerateTreeWithoutLinks(
                    directoryPath,
                    out var files,
                    out var directories,
                    out var reason))
            {
                throw new MoveNeedsAttentionException(
                    $"The owned directory could not be cleaned safely: {reason}");
            }

            var hasDirectoryMarker = File.Exists(markerPath);
            MoveOwnershipMarker? directoryMarker = null;
            if (hasDirectoryMarker)
            {
                ValidateOwnedCleanupEntry(markerPath, directoryPath);
                directoryMarker = ReadOwnershipMarker(markerPath);
                ValidateOwnershipMarker(
                    directoryMarker,
                    expectedDirectoryMarker,
                    sourceSemantics,
                    targetSemantics,
                    directorySemantics);
            }

            var ownedFiles = files
                .Where(file => !FileSystemPathIdentity.AreEquivalent(
                    file,
                    markerPath,
                    directorySemantics))
                .ToList();
            if (markerKind == OwnershipMarkerKind.QuarantineDirectory
                && ownedFiles.Count > 0)
            {
                throw new MoveNeedsAttentionException(
                    "The quarantine cleanup directory contains unexpected content and was preserved.");
            }

            if (!hasDirectoryMarker
                && (ownedFiles.Count > 0 || directories.Count > 0))
            {
                throw new MoveNeedsAttentionException(
                    "The tombstoned cleanup directory was recreated or changed after its ownership marker was removed.");
            }

            foreach (var file in ownedFiles)
            {
                await authorizeMutation();
                ValidateOwnedCleanupEntry(file, directoryPath);
                File.Delete(file);
            }

            foreach (var directory in directories.OrderByDescending(path => path.Length))
            {
                ValidateOwnedCleanupEntry(directory, directoryPath);
                if (Directory.Exists(directory)
                    && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    await authorizeMutation();
                    ValidateOwnedCleanupEntry(directory, directoryPath);
                    Directory.Delete(directory, recursive: false);
                }
            }

            if (hasDirectoryMarker)
            {
                faultInjector?.OnOwnershipCleanup(
                    expectedTombstone.JobId,
                    markerKind,
                    OwnershipCleanupFaultPoint.BeforeOwnershipMarkerDelete);
                ValidateExistingMoveDirectory(directoryPath, "owned cleanup directory");
                ValidateOwnedCleanupEntry(markerPath, directoryPath);
                directoryMarker = ReadOwnershipMarker(markerPath);
                ValidateOwnershipMarker(
                    directoryMarker,
                    expectedDirectoryMarker,
                    sourceSemantics,
                    targetSemantics,
                    directorySemantics);
                await authorizeMutation();
                ValidateOwnedCleanupEntry(markerPath, directoryPath);
                File.Delete(markerPath);
            }

            ValidateExistingMoveDirectory(directoryPath, "owned cleanup directory");
            if (Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                throw new MoveNeedsAttentionException(
                    "The owned directory still contains unexpected content after cleanup.");
            }

            faultInjector?.OnOwnershipCleanup(
                expectedTombstone.JobId,
                markerKind,
                OwnershipCleanupFaultPoint.BeforeDirectoryDelete);
            ValidateExistingMoveDirectory(directoryPath, "owned cleanup directory");
            if (Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                throw new MoveNeedsAttentionException(
                    "The owned directory changed before final deletion.");
            }

            await authorizeMutation();
            ValidateExistingMoveDirectory(directoryPath, "owned cleanup directory");
            Directory.Delete(directoryPath, recursive: false);
        }

        ValidateExistingMoveDirectory(tombstoneParent, "cleanup tombstone directory");
        var validatedTombstone = ReadOwnershipMarker(tombstonePath);
        ValidateOwnershipMarker(
            validatedTombstone,
            expectedTombstone,
            sourceSemantics,
            targetSemantics,
            directorySemantics);
        faultInjector?.OnOwnershipCleanup(
            expectedTombstone.JobId,
            markerKind,
            OwnershipCleanupFaultPoint.BeforeTombstoneDelete);
        ValidateExistingMoveDirectory(tombstoneParent, "cleanup tombstone directory");
        validatedTombstone = ReadOwnershipMarker(tombstonePath);
        ValidateOwnershipMarker(
            validatedTombstone,
            expectedTombstone,
            sourceSemantics,
            targetSemantics,
            directorySemantics);
        await authorizeMutation();
        validatedTombstone = ReadOwnershipMarker(tombstonePath);
        ValidateOwnershipMarker(
            validatedTombstone,
            expectedTombstone,
            sourceSemantics,
            targetSemantics,
            directorySemantics);
        File.Delete(tombstonePath);
    }

    private static bool TryGetExistingPathAttributes(
        string path,
        out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            attributes = default;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }

    private static void ValidateOwnedCleanupEntry(
        string entryPath,
        string directoryPath)
    {
        if (!FileSystemSafety.TryValidateMutationTarget(
                entryPath,
                [directoryPath],
                out entryPath,
                out var reason))
        {
            throw new MoveNeedsAttentionException(reason);
        }

        if ((File.Exists(entryPath) || Directory.Exists(entryPath))
            && (File.GetAttributes(entryPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new MoveNeedsAttentionException(
                "An owned cleanup entry is a symbolic link or reparse point.");
        }
    }
}
