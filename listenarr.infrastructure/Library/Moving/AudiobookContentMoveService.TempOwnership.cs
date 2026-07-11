using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private const string TempOwnershipMarkerFileName = ".listenarr-temp-owner.json";

    private sealed record ValidatedTempOwnership(
        string DirectoryPath,
        string MarkerPath,
        MoveOwnershipMarker Marker);

    private ValidatedTempOwnership CreateOrValidateOwnedTempDirectory(
        string tempDirectory,
        string targetParent,
        AudiobookContentMoveRequest request,
        string source,
        string target)
    {
        var markerPath = Path.Join(tempDirectory, TempOwnershipMarkerFileName);
        if (TryCompleteOwnedDirectoryCleanup(
                tempDirectory,
                markerPath,
                TemporaryDirectoryArtifactType,
                request.JobId,
                source,
                target,
                request.SourceSemantics,
                request.TargetSemantics,
                request.TargetSemantics))
        {
            // A prior cleanup completed. A new temp directory may now be claimed.
        }
        else if (Directory.Exists(tempDirectory))
        {
            return ValidateOwnedTempDirectory(
                tempDirectory,
                targetParent,
                request,
                source,
                target);
        }

        if (File.Exists(tempDirectory))
        {
            throw new MoveNeedsAttentionException(
                "The move temporary path is occupied by a file and cannot be claimed safely.");
        }

        ValidateMoveRootPath(tempDirectory, mustExist: false, "temporary directory");
        Directory.CreateDirectory(tempDirectory);
        ValidateExistingMoveDirectory(tempDirectory, "temporary directory");
        var marker = CreateOwnershipMarker(
            TemporaryDirectoryArtifactType,
            request.JobId,
            source,
            target,
            tempDirectory);
        try
        {
            PublishOwnershipMarker(
                markerPath,
                marker,
                OwnershipMarkerKind.TemporaryDirectory);
            return ValidateOwnedTempDirectory(
                tempDirectory,
                targetParent,
                request,
                source,
                target);
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            TryRemoveNewEmptyOwnershipDirectory(tempDirectory, request.JobId, "temp");
            throw new MoveNeedsAttentionException(
                $"The move temporary directory could not be claimed safely: {exception.Message}");
        }
    }

    private ValidatedTempOwnership ValidateOwnedTempDirectory(
        string tempDirectory,
        string targetParent,
        AudiobookContentMoveRequest request,
        string source,
        string target)
    {
        if (!FileSystemSafety.TryValidateMutationTarget(
                tempDirectory,
                [targetParent],
                out var safeTempDirectory,
                out var tempReason))
        {
            throw new MoveNeedsAttentionException(tempReason);
        }

        ValidateExistingMoveDirectory(safeTempDirectory, "temporary directory");
        var markerPath = Path.Join(safeTempDirectory, TempOwnershipMarkerFileName);
        var expectedMarker = CreateOwnershipMarker(
            TemporaryDirectoryArtifactType,
            request.JobId,
            source,
            target,
            tempDirectory);
        var marker = RecoverOrReadOwnershipMarker(
            markerPath,
            expectedMarker,
            request.SourceSemantics,
            request.TargetSemantics,
            request.TargetSemantics);
        return new ValidatedTempOwnership(
            safeTempDirectory,
            markerPath,
            marker);
    }

    private void TryDeleteOwnedTempDirectory(
        string tempDirectory,
        string targetParent,
        AudiobookContentMoveRequest request,
        string source,
        string target)
    {
        var markerPath = Path.Join(tempDirectory, TempOwnershipMarkerFileName);
        try
        {
            if (TryCompleteOwnedDirectoryCleanup(
                    tempDirectory,
                    markerPath,
                    TemporaryDirectoryArtifactType,
                    request.JobId,
                    source,
                    target,
                    request.SourceSemantics,
                    request.TargetSemantics,
                    request.TargetSemantics))
            {
                return;
            }

            if (!Directory.Exists(tempDirectory))
            {
                return;
            }

            var ownership = ValidateOwnedTempDirectory(
                tempDirectory,
                targetParent,
                request,
                source,
                target);
            DeleteOwnedDirectoryWithTombstone(
                ownership.DirectoryPath,
                ownership.MarkerPath,
                TemporaryDirectoryArtifactType,
                request.JobId,
                source,
                target,
                request.SourceSemantics,
                request.TargetSemantics,
                request.TargetSemantics);
        }
        catch (MoveNeedsAttentionException exception)
        {
            logger.LogWarning(
                exception,
                "Preserved unowned or ambiguous move temp directory for job {JobId}",
                request.JobId);
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            logger.LogWarning(
                exception,
                "Failed to clean the validated move temp directory for job {JobId}",
                request.JobId);
        }
    }

    private ValidatedTempOwnership? TryValidatePublishedTempOwnership(
        string destinationRoot,
        AudiobookContentMoveRequest request,
        string source,
        string target)
    {
        var markerPath = Path.Join(destinationRoot, TempOwnershipMarkerFileName);
        if (!File.Exists(markerPath))
        {
            return null;
        }

        var destinationParent = Path.GetDirectoryName(destinationRoot)
            ?? throw new MoveNeedsAttentionException("The destination parent is unavailable.");
        var originalTempDirectory = Path.Join(
            destinationParent,
            Path.GetFileName(target) + ".tmp-" + request.JobId.ToString("N"));
        if (!FileSystemSafety.TryValidateMutationTarget(
                destinationRoot,
                [destinationParent],
                out var safeDestination,
                out var destinationReason))
        {
            throw new MoveNeedsAttentionException(destinationReason);
        }

        ValidateExistingMoveDirectory(safeDestination, "published temporary directory");
        var expectedMarker = CreateOwnershipMarker(
            TemporaryDirectoryArtifactType,
            request.JobId,
            source,
            target,
            originalTempDirectory);
        var marker = RecoverOrReadOwnershipMarker(
            markerPath,
            expectedMarker,
            request.SourceSemantics,
            request.TargetSemantics,
            request.TargetSemantics);
        return new ValidatedTempOwnership(
            safeDestination,
            markerPath,
            marker);
    }

    private static void TryDeletePublishedTempOwnershipMarker(
        ValidatedTempOwnership? ownership,
        AudiobookContentMoveRequest request)
    {
        if (ownership == null || !File.Exists(ownership.MarkerPath))
        {
            return;
        }

        ValidateExistingMoveDirectory(
            ownership.DirectoryPath,
            "published temporary directory");
        var marker = ReadOwnershipMarker(ownership.MarkerPath);
        ValidateOwnershipMarker(
            marker,
            ownership.Marker,
            request.SourceSemantics,
            request.TargetSemantics,
            request.TargetSemantics);
        ValidateExistingMoveDirectory(
            ownership.DirectoryPath,
            "published temporary directory");
        var currentMarker = ReadOwnershipMarker(ownership.MarkerPath);
        ValidateOwnershipMarker(
            currentMarker,
            ownership.Marker,
            request.SourceSemantics,
            request.TargetSemantics,
            request.TargetSemantics);
        File.Delete(ownership.MarkerPath);
    }

    private void TryRemoveNewEmptyOwnershipDirectory(
        string directory,
        Guid jobId,
        string artifactName)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                ValidateExistingMoveDirectory(
                    directory,
                    $"new {artifactName} directory");
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    ValidateExistingMoveDirectory(
                        directory,
                        $"new {artifactName} directory");
                    Directory.Delete(directory, recursive: false);
                }
            }
        }
        catch (Exception cleanupException) when (WorkerExceptionClassifier.IsNonFatal(cleanupException))
        {
            logger.LogWarning(
                cleanupException,
                "Failed to remove newly created empty {ArtifactName} directory for move job {JobId}",
                artifactName,
                jobId);
        }
    }
}
