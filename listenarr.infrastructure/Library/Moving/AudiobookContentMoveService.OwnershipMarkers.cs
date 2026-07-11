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

    private async Task<MoveOwnershipMarker> RecoverOrReadOwnershipMarkerAsync(
        string markerPath,
        MoveOwnershipMarker expected,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics,
        FileSystemPathSemantics directorySemantics,
        Func<Task> authorizeMutation)
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
            await DeleteValidatedOwnershipWriteFilesAsync(
                markerPath,
                expected,
                sourceSemantics,
                targetSemantics,
                directorySemantics,
                authorizeMutation);
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
            await authorizeMutation();
            ValidateOwnershipMarkerWritePath(writePath, markerDirectory);
            File.SetAttributes(
                writePath,
                File.GetAttributes(writePath) | FileAttributes.Hidden);
        }

        ValidateOwnershipMarkerPublicationPaths(
            markerDirectory,
            writePath,
            markerPath);
        await authorizeMutation();
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

    private static async Task DeleteValidatedOwnershipWriteFilesAsync(
        string markerPath,
        MoveOwnershipMarker expected,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics,
        FileSystemPathSemantics directorySemantics,
        Func<Task> authorizeMutation)
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
            await authorizeMutation();
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
