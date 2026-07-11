using System.Text.Json;
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private const int QuarantineOwnershipMarkerVersion = 1;
    private const string QuarantineOwnershipMarkerFileName = ".listenarr-quarantine-owner.json";

    private sealed record MoveQuarantineOwnershipMarker(
        int Version,
        Guid JobId,
        string Source,
        string Target);

    private sealed record ValidatedQuarantineOwnership(
        string DirectoryPath,
        string MarkerPath);

    private ValidatedQuarantineOwnership CreateOrValidateOwnedQuarantineDirectory(
        string quarantineRoot,
        string sourceParent,
        Guid jobId,
        string source,
        string target,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics)
    {
        if (Directory.Exists(quarantineRoot))
        {
            return ValidateOwnedQuarantineDirectory(
                quarantineRoot,
                sourceParent,
                jobId,
                source,
                target,
                sourceSemantics,
                targetSemantics);
        }

        if (File.Exists(quarantineRoot))
        {
            throw new MoveNeedsAttentionException(
                "The move quarantine path is occupied by a file and cannot be claimed safely.");
        }

        ValidateMoveRootPath(quarantineRoot, mustExist: false, "quarantine");
        Directory.CreateDirectory(quarantineRoot);
        var markerPath = Path.Join(quarantineRoot, QuarantineOwnershipMarkerFileName);
        try
        {
            var marker = new MoveQuarantineOwnershipMarker(
                QuarantineOwnershipMarkerVersion,
                jobId,
                Path.GetFullPath(source),
                Path.GetFullPath(target));
            var payload = JsonSerializer.SerializeToUtf8Bytes(marker);
            using (var stream = new FileStream(
                markerPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(markerPath, FileAttributes.Hidden);
            }

            return ValidateOwnedQuarantineDirectory(
                quarantineRoot,
                sourceParent,
                jobId,
                source,
                target,
                sourceSemantics,
                targetSemantics);
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            try
            {
                if (Directory.Exists(quarantineRoot)
                    && !Directory.EnumerateFileSystemEntries(quarantineRoot).Any())
                {
                    Directory.Delete(quarantineRoot, recursive: false);
                }
            }
            catch (Exception cleanupException) when (WorkerExceptionClassifier.IsNonFatal(cleanupException))
            {
                logger.LogWarning(
                    cleanupException,
                    "Failed to remove newly created empty quarantine directory for move job {JobId}",
                    jobId);
            }

            throw new MoveNeedsAttentionException(
                $"The move quarantine directory could not be claimed safely: {exception.Message}");
        }
    }

    private static ValidatedQuarantineOwnership ValidateOwnedQuarantineDirectory(
        string quarantineRoot,
        string sourceParent,
        Guid jobId,
        string source,
        string target,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics)
    {
        if (!FileSystemSafety.TryValidateMutationTarget(
                quarantineRoot,
                [sourceParent],
                out var safeQuarantineRoot,
                out var quarantineReason))
        {
            throw new MoveNeedsAttentionException(quarantineReason);
        }

        ValidateExistingMoveDirectory(safeQuarantineRoot, "quarantine directory");
        var markerPath = Path.Join(safeQuarantineRoot, QuarantineOwnershipMarkerFileName);
        if (!FileSystemSafety.TryValidateMutationTarget(
                markerPath,
                [safeQuarantineRoot],
                out markerPath,
                out var markerReason))
        {
            throw new MoveNeedsAttentionException(markerReason);
        }

        if (!File.Exists(markerPath)
            || (File.GetAttributes(markerPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new MoveNeedsAttentionException(
                "The move quarantine directory has no valid ownership marker.");
        }

        MoveQuarantineOwnershipMarker? marker;
        try
        {
            marker = JsonSerializer.Deserialize<MoveQuarantineOwnershipMarker>(
                File.ReadAllText(markerPath));
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            throw new MoveNeedsAttentionException(
                $"The move quarantine ownership marker could not be read safely: {exception.Message}");
        }

        if (marker == null
            || marker.Version != QuarantineOwnershipMarkerVersion
            || marker.JobId != jobId)
        {
            throw new MoveNeedsAttentionException(
                "The move quarantine directory is owned by another job or uses an unsupported marker version.");
        }

        try
        {
            if (!FileSystemPathIdentity.AreEquivalent(marker.Source, source, sourceSemantics)
                || !FileSystemPathIdentity.AreEquivalent(marker.Target, target, targetSemantics))
            {
                throw new MoveNeedsAttentionException(
                    "The move quarantine ownership marker does not match the persisted source and target.");
            }
        }
        catch (ArgumentException)
        {
            throw new MoveNeedsAttentionException(
                "The move quarantine ownership marker contains an invalid source or target identity.");
        }

        var ownership = new ValidatedQuarantineOwnership(safeQuarantineRoot, markerPath);
        ValidateOwnedQuarantineTree(ownership);
        return ownership;
    }

    private static void ValidateOwnedQuarantineTree(ValidatedQuarantineOwnership ownership)
    {
        ValidateExistingMoveDirectory(ownership.DirectoryPath, "quarantine directory");
        if (!FileSystemSafety.TryEnumerateTreeWithoutLinks(
                ownership.DirectoryPath,
                out _,
                out _,
                out var reason))
        {
            throw new MoveNeedsAttentionException(
                $"The move quarantine directory could not be traversed safely: {reason}");
        }
    }

    private static void ValidateQuarantineMutationPath(
        ValidatedQuarantineOwnership ownership,
        string path)
    {
        ValidateOwnedQuarantineTree(ownership);
        if (!FileSystemSafety.TryValidateMutationTarget(
                path,
                [ownership.DirectoryPath],
                out _,
                out var reason))
        {
            throw new MoveNeedsAttentionException(reason);
        }

        if ((File.Exists(path) || Directory.Exists(path))
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new MoveNeedsAttentionException(
                "A move quarantine entry is a symbolic link or reparse point.");
        }
    }

    private static void DeleteEmptyOwnedQuarantineDirectory(
        ValidatedQuarantineOwnership ownership,
        FileSystemPathSemantics sourceSemantics)
    {
        if (!FileSystemSafety.TryEnumerateTreeWithoutLinks(
                ownership.DirectoryPath,
                out var files,
                out var directories,
                out var reason))
        {
            throw new MoveNeedsAttentionException(
                $"The completed move quarantine could not be traversed safely: {reason}");
        }

        var unexpectedFile = files.FirstOrDefault(file =>
            !FileSystemPathIdentity.AreEquivalent(
                file,
                ownership.MarkerPath,
                sourceSemantics));
        if (unexpectedFile != null)
        {
            throw new MoveNeedsAttentionException(
                $"The completed move quarantine contains an unexpected file: {Path.GetFileName(unexpectedFile)}");
        }

        if (directories.Any(directory =>
                Directory.EnumerateFileSystemEntries(directory).Any()))
        {
            throw new MoveNeedsAttentionException(
                "The completed move quarantine contains an unexpected non-empty directory.");
        }

        // Ownership and link-free traversal are proven immediately above. Recursive
        // deletion removes the marker and empty directory tree as one filesystem action.
        Directory.Delete(ownership.DirectoryPath, recursive: true);
    }
}
