using System.Text.Json;
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private const int TempOwnershipMarkerVersion = 1;
    private const string TempOwnershipMarkerFileName = ".listenarr-temp-owner.json";

    private sealed record MoveTempOwnershipMarker(
        int Version,
        Guid JobId,
        string Source,
        string Target);

    private sealed record ValidatedTempOwnership(
        string DirectoryPath,
        string MarkerPath);

    private ValidatedTempOwnership CreateOrValidateOwnedTempDirectory(
        string tempDirectory,
        string targetParent,
        AudiobookContentMoveRequest request,
        string source,
        string target)
    {
        if (Directory.Exists(tempDirectory))
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

        Directory.CreateDirectory(tempDirectory);
        var markerPath = Path.Join(tempDirectory, TempOwnershipMarkerFileName);
        try
        {
            var marker = new MoveTempOwnershipMarker(
                TempOwnershipMarkerVersion,
                request.JobId,
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

            return ValidateOwnedTempDirectory(
                tempDirectory,
                targetParent,
                request,
                source,
                target);
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            try
            {
                if (Directory.Exists(tempDirectory)
                    && !Directory.EnumerateFileSystemEntries(tempDirectory).Any())
                {
                    Directory.Delete(tempDirectory, recursive: false);
                }
            }
            catch (Exception cleanupException) when (WorkerExceptionClassifier.IsNonFatal(cleanupException))
            {
                logger.LogWarning(
                    cleanupException,
                    "Failed to remove newly created empty temp directory for move job {JobId}",
                    request.JobId);
            }

            throw new MoveNeedsAttentionException(
                $"The move temporary directory could not be claimed safely: {exception.Message}");
        }
    }

    private static ValidatedTempOwnership ValidateOwnedTempDirectory(
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

        if (!Directory.Exists(safeTempDirectory)
            || (File.GetAttributes(safeTempDirectory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new MoveNeedsAttentionException(
                "The move temporary directory is missing or is a symbolic link or reparse point.");
        }

        var markerPath = Path.Join(safeTempDirectory, TempOwnershipMarkerFileName);
        if (!FileSystemSafety.TryValidateMutationTarget(
                markerPath,
                [safeTempDirectory],
                out markerPath,
                out var markerReason))
        {
            throw new MoveNeedsAttentionException(markerReason);
        }

        if (!File.Exists(markerPath)
            || (File.GetAttributes(markerPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new MoveNeedsAttentionException(
                "The move temporary directory has no valid ownership marker.");
        }

        MoveTempOwnershipMarker? marker;
        try
        {
            marker = JsonSerializer.Deserialize<MoveTempOwnershipMarker>(File.ReadAllText(markerPath));
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            throw new MoveNeedsAttentionException(
                $"The move temporary ownership marker could not be read safely: {exception.Message}");
        }

        if (marker == null
            || marker.Version != TempOwnershipMarkerVersion
            || marker.JobId != request.JobId)
        {
            throw new MoveNeedsAttentionException(
                "The move temporary directory is owned by another job or uses an unsupported marker version.");
        }

        try
        {
            if (!FileSystemPathIdentity.AreEquivalent(
                    marker.Source,
                    source,
                    request.SourceSemantics)
                || !FileSystemPathIdentity.AreEquivalent(
                    marker.Target,
                    target,
                    request.TargetSemantics))
            {
                throw new MoveNeedsAttentionException(
                    "The move temporary ownership marker does not match the persisted source and target.");
            }
        }
        catch (ArgumentException)
        {
            throw new MoveNeedsAttentionException(
                "The move temporary ownership marker contains an invalid source or target identity.");
        }

        return new ValidatedTempOwnership(safeTempDirectory, markerPath);
    }

    private void TryDeleteOwnedTempDirectory(
        string tempDirectory,
        string targetParent,
        AudiobookContentMoveRequest request,
        string source,
        string target)
    {
        if (!Directory.Exists(tempDirectory))
        {
            return;
        }

        try
        {
            var ownership = ValidateOwnedTempDirectory(
                tempDirectory,
                targetParent,
                request,
                source,
                target);
            if (!FileSystemSafety.TryEnumerateTreeWithoutLinks(
                    ownership.DirectoryPath,
                    out _,
                    out _,
                    out var reason))
            {
                logger.LogWarning(
                    "Preserved move temp directory for job {JobId} because it could not be traversed safely: {Reason}",
                    request.JobId,
                    reason);
                return;
            }

            Directory.Delete(ownership.DirectoryPath, recursive: true);
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

    private static ValidatedTempOwnership? TryValidatePublishedTempOwnership(
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
        return ValidateOwnedTempDirectory(
            destinationRoot,
            destinationParent,
            request,
            source,
            target);
    }

    private static void TryDeletePublishedTempOwnershipMarker(
        ValidatedTempOwnership? ownership)
    {
        if (ownership == null || !File.Exists(ownership.MarkerPath))
        {
            return;
        }

        File.Delete(ownership.MarkerPath);
    }
}
