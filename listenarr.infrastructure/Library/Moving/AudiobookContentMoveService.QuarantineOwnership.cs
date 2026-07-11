using Listenarr.Domain.Common;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private const string QuarantineOwnershipMarkerFileName = ".listenarr-quarantine-owner.json";

    private sealed record ValidatedQuarantineOwnership(
        string DirectoryPath,
        string MarkerPath,
        MoveOwnershipMarker Marker);

    private ValidatedQuarantineOwnership CreateOrValidateOwnedQuarantineDirectory(
        string quarantineRoot,
        string sourceParent,
        Guid jobId,
        string source,
        string target,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics)
    {
        var markerPath = Path.Join(
            quarantineRoot,
            QuarantineOwnershipMarkerFileName);
        if (TryCompleteOwnedDirectoryCleanup(
                quarantineRoot,
                markerPath,
                QuarantineDirectoryArtifactType,
                jobId,
                source,
                target,
                sourceSemantics,
                targetSemantics,
                sourceSemantics))
        {
            // A prior completed cleanup left durable tombstone evidence.
        }
        else if (Directory.Exists(quarantineRoot))
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
        ValidateExistingMoveDirectory(quarantineRoot, "quarantine directory");
        var marker = CreateOwnershipMarker(
            QuarantineDirectoryArtifactType,
            jobId,
            source,
            target,
            quarantineRoot);
        try
        {
            PublishOwnershipMarker(
                markerPath,
                marker,
                OwnershipMarkerKind.QuarantineDirectory);
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
            TryRemoveNewEmptyOwnershipDirectory(
                quarantineRoot,
                jobId,
                "quarantine");
            throw new MoveNeedsAttentionException(
                $"The move quarantine directory could not be claimed safely: {exception.Message}");
        }
    }

    private ValidatedQuarantineOwnership ValidateOwnedQuarantineDirectory(
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

        ValidateExistingMoveDirectory(
            safeQuarantineRoot,
            "quarantine directory");
        var markerPath = Path.Join(
            safeQuarantineRoot,
            QuarantineOwnershipMarkerFileName);
        var expectedMarker = CreateOwnershipMarker(
            QuarantineDirectoryArtifactType,
            jobId,
            source,
            target,
            quarantineRoot);
        var marker = RecoverOrReadOwnershipMarker(
            markerPath,
            expectedMarker,
            sourceSemantics,
            targetSemantics,
            sourceSemantics);

        var ownership = new ValidatedQuarantineOwnership(
            safeQuarantineRoot,
            markerPath,
            marker);
        ValidateOwnedQuarantineTree(ownership);
        return ownership;
    }

    private ValidatedQuarantineOwnership? TryValidateExistingQuarantineDirectory(
        string source,
        string target,
        Guid jobId,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics)
    {
        var sourceParent = Path.GetDirectoryName(Path.GetFullPath(source))
            ?? throw new MoveNeedsAttentionException("The source parent is unavailable.");
        var quarantineRoot = Path.Join(
            sourceParent,
            $".listenarr-quarantine-{jobId:N}");
        var markerPath = Path.Join(
            quarantineRoot,
            QuarantineOwnershipMarkerFileName);
        if (TryCompleteOwnedDirectoryCleanup(
                quarantineRoot,
                markerPath,
                QuarantineDirectoryArtifactType,
                jobId,
                source,
                target,
                sourceSemantics,
                targetSemantics,
                sourceSemantics))
        {
            return null;
        }

        if (!Directory.Exists(quarantineRoot))
        {
            if (File.Exists(quarantineRoot))
            {
                throw new MoveNeedsAttentionException(
                    "The move quarantine path is occupied by a file and cannot be validated safely.");
            }

            return null;
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

    private static void ValidateOwnedQuarantineTree(
        ValidatedQuarantineOwnership ownership)
    {
        ValidateExistingMoveDirectory(
            ownership.DirectoryPath,
            "quarantine directory");
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
                out path,
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

    private void DeleteEmptyOwnedQuarantineDirectory(
        ValidatedQuarantineOwnership ownership,
        Guid jobId,
        string source,
        string target,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics)
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

        DeleteOwnedDirectoryWithTombstone(
            ownership.DirectoryPath,
            ownership.MarkerPath,
            QuarantineDirectoryArtifactType,
            jobId,
            source,
            target,
            sourceSemantics,
            targetSemantics,
            sourceSemantics);
    }
}
