using System.Text.Json;
using Listenarr.Domain.Common;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private const int OwnershipMarkerVersion = 1;
    private const string TemporaryDirectoryArtifactType = "temporary-directory";
    private const string QuarantineDirectoryArtifactType = "quarantine-directory";
    private const string CleanupTombstoneArtifactType = "cleanup-tombstone";

    private sealed record MoveOwnershipMarker(
        int Version,
        string ArtifactType,
        Guid JobId,
        string Source,
        string Target,
        string DirectoryPath,
        string? OwnedArtifactType = null);

    private MoveOwnershipMarker CreateOwnershipMarker(
        string artifactType,
        Guid jobId,
        string source,
        string target,
        string directoryPath,
        string? ownedArtifactType = null) =>
        new(
            OwnershipMarkerVersion,
            artifactType,
            jobId,
            Path.GetFullPath(source),
            Path.GetFullPath(target),
            Path.GetFullPath(directoryPath),
            ownedArtifactType);

    private void PublishOwnershipMarker(
        string markerPath,
        MoveOwnershipMarker marker,
        OwnershipMarkerKind markerKind)
    {
        var markerDirectory = Path.GetDirectoryName(Path.GetFullPath(markerPath))
            ?? throw new MoveNeedsAttentionException("The ownership marker directory is unavailable.");
        ValidateExistingMoveDirectory(markerDirectory, "ownership-marker directory");
        if (!FileSystemSafety.TryValidateMutationTarget(
                markerPath,
                [markerDirectory],
                out markerPath,
                out var markerReason))
        {
            throw new MoveNeedsAttentionException(markerReason);
        }

        if (File.Exists(markerPath))
        {
            throw new MoveNeedsAttentionException(
                "The ownership marker already exists and cannot be overwritten safely.");
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(marker);
        var writePath = markerPath + $".writing-{Guid.NewGuid():N}";
        faultInjector?.OnOwnershipMarkerWrite(
            marker.JobId,
            markerKind,
            OwnershipMarkerWriteFaultPoint.BeforeTemporaryFileCreation);

        try
        {
            ValidateNewOwnershipMarkerWritePath(writePath, markerDirectory);
            using (var stream = new FileStream(
                writePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                var split = Math.Max(1, payload.Length / 2);
                stream.Write(payload.AsSpan(0, split));
                faultInjector?.OnOwnershipMarkerWrite(
                    marker.JobId,
                    markerKind,
                    OwnershipMarkerWriteFaultPoint.DuringJsonWrite);
                stream.Write(payload.AsSpan(split));
                faultInjector?.OnOwnershipMarkerWrite(
                    marker.JobId,
                    markerKind,
                    OwnershipMarkerWriteFaultPoint.DuringFlush);
                stream.Flush(flushToDisk: true);
            }

            faultInjector?.OnOwnershipMarkerWrite(
                marker.JobId,
                markerKind,
                OwnershipMarkerWriteFaultPoint.AfterTemporaryFileWritten);
            faultInjector?.OnOwnershipMarkerWrite(
                marker.JobId,
                markerKind,
                OwnershipMarkerWriteFaultPoint.BeforePublication);

            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(
                    writePath,
                    File.GetAttributes(writePath) | FileAttributes.Hidden);
            }

            ValidateOwnershipMarkerPublicationPaths(
                markerDirectory,
                writePath,
                markerPath);
            File.Move(writePath, markerPath, overwrite: false);
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            Exception? cleanupException = null;
            try
            {
                faultInjector?.OnOwnershipMarkerWrite(
                    marker.JobId,
                    markerKind,
                    OwnershipMarkerWriteFaultPoint.BeforeTemporaryFileDeletion);
                if (File.Exists(writePath))
                {
                    ValidateOwnershipMarkerWritePath(writePath, markerDirectory);
                    File.Delete(writePath);
                }
            }
            catch (Exception temporaryCleanupException) when (WorkerExceptionClassifier.IsNonFatal(temporaryCleanupException))
            {
                cleanupException = temporaryCleanupException;
            }

            if (cleanupException != null)
            {
                throw new MoveNeedsAttentionException(
                    $"Ownership marker publication failed and its temporary file could not be removed. "
                    + $"Publication error: {exception.Message}. "
                    + $"Temporary cleanup error: {cleanupException.Message}.");
            }

            throw;
        }
    }

    private MoveOwnershipMarker RecoverOrReadOwnershipMarker(
        string markerPath,
        MoveOwnershipMarker expected,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics,
        FileSystemPathSemantics directorySemantics)
    {
        var markerDirectory = Path.GetDirectoryName(Path.GetFullPath(markerPath))
            ?? throw new MoveNeedsAttentionException("The ownership marker directory is unavailable.");
        ValidateExistingMoveDirectory(markerDirectory, "ownership-marker directory");

        if (File.Exists(markerPath))
        {
            var marker = ReadOwnershipMarker(markerPath);
            ValidateOwnershipMarker(
                marker,
                expected,
                sourceSemantics,
                targetSemantics,
                directorySemantics);
            DeleteValidatedOwnershipWriteFiles(
                markerPath,
                expected,
                sourceSemantics,
                targetSemantics,
                directorySemantics);
            return marker;
        }

        var writeFiles = Directory.EnumerateFiles(
                markerDirectory,
                Path.GetFileName(markerPath) + ".writing-*",
                SearchOption.TopDirectoryOnly)
            .ToList();
        if (writeFiles.Count != 1)
        {
            throw new MoveNeedsAttentionException(
                writeFiles.Count == 0
                    ? "The owned directory has no valid ownership marker."
                    : "The owned directory has multiple incomplete ownership marker publications.");
        }

        var writePath = writeFiles[0];
        ValidateOwnershipMarkerWritePath(writePath, markerDirectory);
        var recovered = ReadOwnershipMarker(writePath);
        ValidateOwnershipMarker(
            recovered,
            expected,
            sourceSemantics,
            targetSemantics,
            directorySemantics);
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(
                writePath,
                File.GetAttributes(writePath) | FileAttributes.Hidden);
        }

        ValidateOwnershipMarkerPublicationPaths(
            markerDirectory,
            writePath,
            markerPath);
        File.Move(writePath, markerPath, overwrite: false);
        return recovered;
    }

    private static MoveOwnershipMarker ReadOwnershipMarker(string markerPath)
    {
        if (!FileSystemSafety.TryValidateMutationTarget(
                markerPath,
                [Path.GetDirectoryName(markerPath)],
                out markerPath,
                out var markerReason))
        {
            throw new MoveNeedsAttentionException(markerReason);
        }

        if (!File.Exists(markerPath)
            || (File.GetAttributes(markerPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new MoveNeedsAttentionException("The ownership marker is missing or linked.");
        }

        try
        {
            return JsonSerializer.Deserialize<MoveOwnershipMarker>(File.ReadAllText(markerPath))
                ?? throw new MoveNeedsAttentionException("The ownership marker is empty.");
        }
        catch (MoveNeedsAttentionException)
        {
            throw;
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            throw new MoveNeedsAttentionException(
                $"The ownership marker could not be read safely: {exception.Message}");
        }
    }

    private static void ValidateOwnershipMarker(
        MoveOwnershipMarker marker,
        MoveOwnershipMarker expected,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics,
        FileSystemPathSemantics directorySemantics)
    {
        if (marker.Version != OwnershipMarkerVersion
            || marker.JobId != expected.JobId
            || !string.Equals(marker.ArtifactType, expected.ArtifactType, StringComparison.Ordinal)
            || !string.Equals(marker.OwnedArtifactType, expected.OwnedArtifactType, StringComparison.Ordinal))
        {
            throw new MoveNeedsAttentionException(
                "The owned directory is owned by another job, artifact type, or unsupported marker version.");
        }

        try
        {
            if (!FileSystemPathIdentity.AreEquivalent(marker.Source, expected.Source, sourceSemantics)
                || !FileSystemPathIdentity.AreEquivalent(marker.Target, expected.Target, targetSemantics)
                || !FileSystemPathIdentity.AreEquivalent(
                    marker.DirectoryPath,
                    expected.DirectoryPath,
                    directorySemantics))
            {
                throw new MoveNeedsAttentionException(
                    "The ownership marker does not match the persisted source, target, or owned directory.");
            }
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or NotSupportedException or PathTooLongException)
        {
            throw new MoveNeedsAttentionException(
                "The ownership marker contains an invalid filesystem identity.");
        }
    }

    private static void DeleteValidatedOwnershipWriteFiles(
        string markerPath,
        MoveOwnershipMarker expected,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics,
        FileSystemPathSemantics directorySemantics)
    {
        var markerDirectory = Path.GetDirectoryName(markerPath)
            ?? throw new MoveNeedsAttentionException("The ownership marker directory is unavailable.");
        foreach (var writePath in Directory.EnumerateFiles(
            markerDirectory,
            Path.GetFileName(markerPath) + ".writing-*",
            SearchOption.TopDirectoryOnly))
        {
            ValidateOwnershipMarkerWritePath(writePath, markerDirectory);
            var writeMarker = ReadOwnershipMarker(writePath);
            ValidateOwnershipMarker(
                writeMarker,
                expected,
                sourceSemantics,
                targetSemantics,
                directorySemantics);
            ValidateOwnershipMarkerWritePath(writePath, markerDirectory);
            writeMarker = ReadOwnershipMarker(writePath);
            ValidateOwnershipMarker(
                writeMarker,
                expected,
                sourceSemantics,
                targetSemantics,
                directorySemantics);
            File.Delete(writePath);
        }
    }

    private static void ValidateNewOwnershipMarkerWritePath(
        string writePath,
        string markerDirectory)
    {
        ValidateExistingMoveDirectory(markerDirectory, "ownership-marker directory");
        if (!FileSystemSafety.TryValidateMutationTarget(
                writePath,
                [markerDirectory],
                out writePath,
                out var writeReason))
        {
            throw new MoveNeedsAttentionException(writeReason);
        }

        if (File.Exists(writePath) || Directory.Exists(writePath))
        {
            throw new MoveNeedsAttentionException(
                "The ownership-marker temporary path appeared before creation.");
        }
    }

    private static void ValidateOwnershipMarkerPublicationPaths(
        string markerDirectory,
        string writePath,
        string markerPath)
    {
        ValidateExistingMoveDirectory(markerDirectory, "ownership-marker directory");
        ValidateOwnershipMarkerWritePath(writePath, markerDirectory);
        if (!FileSystemSafety.TryValidateMutationTarget(
                markerPath,
                [markerDirectory],
                out _,
                out var markerReason))
        {
            throw new MoveNeedsAttentionException(markerReason);
        }

        if (File.Exists(markerPath) || Directory.Exists(markerPath))
        {
            throw new MoveNeedsAttentionException(
                "The authoritative ownership marker appeared before publication.");
        }
    }

    private static void ValidateOwnershipMarkerWritePath(
        string writePath,
        string markerDirectory)
    {
        ValidateExistingMoveDirectory(markerDirectory, "ownership-marker directory");
        if (!FileSystemSafety.TryValidateMutationTarget(
                writePath,
                [markerDirectory],
                out writePath,
                out var writeReason))
        {
            throw new MoveNeedsAttentionException(writeReason);
        }

        if (!File.Exists(writePath)
            || (File.GetAttributes(writePath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new MoveNeedsAttentionException(
                "An ownership-marker temporary file is missing or linked.");
        }
    }

    private static string GetCleanupTombstonePath(
        string directoryPath,
        string ownedArtifactType,
        Guid jobId)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(directoryPath))
            ?? throw new MoveNeedsAttentionException("The owned directory parent is unavailable.");
        return Path.Join(
            parent,
            $".listenarr-{ownedArtifactType}-{jobId:N}.cleanup.json");
    }
}
