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
        string? OwnedArtifactType = null,
        string? OwnedDirectoryPath = null);

    private MoveOwnershipMarker CreateOwnershipMarker(
        string artifactType,
        Guid jobId,
        string source,
        string target,
        string directoryPath,
        string? ownedArtifactType = null,
        string? ownedDirectoryPath = null) =>
        new(
            OwnershipMarkerVersion,
            artifactType,
            jobId,
            Path.GetFullPath(source),
            Path.GetFullPath(target),
            Path.GetFullPath(directoryPath),
            ownedArtifactType,
            string.IsNullOrWhiteSpace(ownedDirectoryPath)
                ? null
                : Path.GetFullPath(ownedDirectoryPath));

    private async Task<MoveOwnershipMarker> RecoverOrReadOwnershipMarkerAsync(
        string markerPath,
        MoveOwnershipMarker expected,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics,
        FileSystemPathSemantics directorySemantics,
        MoveLeaseToken leaseToken,
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
                leaseToken,
                authorizeMutation);
            return marker;
        }

        var validWrites = new List<(string Path, MoveOwnershipMarker Marker)>();
        var discardedTruncatedPredecessor = false;
        foreach (var writePath in Directory.EnumerateFiles(
            markerDirectory,
            Path.GetFileName(markerPath) + ".writing-*",
            SearchOption.TopDirectoryOnly))
        {
            ValidateOwnershipMarkerWritePath(writePath, markerDirectory);
            if (!TryParseMarkerWriteIdentity(writePath, markerPath, out var writeIdentity)
                || writeIdentity.JobId != expected.JobId)
            {
                throw new MoveNeedsAttentionException(
                    "An ownership-marker write filename does not match the active move job.");
            }

            if (writeIdentity.LeaseGeneration > leaseToken.Generation)
            {
                throw new MoveNeedsAttentionException(
                    "A future-generation ownership-marker write file was preserved.");
            }

            var recovered = TryReadOwnershipMarkerWriteFile(writePath);
            if (recovered == null)
            {
                if (writeIdentity.LeaseGeneration >= leaseToken.Generation)
                {
                    throw new MoveNeedsAttentionException(
                        "A current or future-generation ownership-marker write file is truncated and was preserved.");
                }

                await authorizeMutation();
                ValidateOwnershipMarkerWritePath(writePath, markerDirectory);
                if (!TryParseMarkerWriteIdentity(writePath, markerPath, out var currentIdentity)
                    || currentIdentity != writeIdentity)
                {
                    throw new MoveNeedsAttentionException(
                        "A truncated ownership-marker write file changed before cleanup.");
                }

                File.Delete(writePath);
                discardedTruncatedPredecessor = true;
                continue;
            }

            ValidateOwnershipMarker(
                recovered,
                expected,
                sourceSemantics,
                targetSemantics,
                directorySemantics);
            validWrites.Add((writePath, recovered));
        }

        if (validWrites.Count == 0)
        {
            if (discardedTruncatedPredecessor)
            {
                throw new InterruptedOwnershipPublicationException(
                    "A truncated predecessor ownership marker was removed; the empty owned directory must be reclaimed.");
            }

            throw new MoveNeedsAttentionException(
                "The owned directory has no valid ownership marker.");
        }

        if (validWrites.Count != 1)
        {
            throw new MoveNeedsAttentionException(
                "The owned directory has multiple incomplete ownership marker publications.");
        }

        var (validWritePath, validMarker) = validWrites[0];
        if (OperatingSystem.IsWindows())
        {
            await authorizeMutation();
            ValidateOwnershipMarkerWritePath(validWritePath, markerDirectory);
            File.SetAttributes(
                validWritePath,
                File.GetAttributes(validWritePath) | FileAttributes.Hidden);
        }

        ValidateOwnershipMarkerPublicationPaths(
            markerDirectory,
            validWritePath,
            markerPath);
        await authorizeMutation();
        ValidateOwnershipMarkerPublicationPaths(
            markerDirectory,
            validWritePath,
            markerPath);
        File.Move(validWritePath, markerPath, overwrite: false);
        return validMarker;
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

    private static MoveOwnershipMarker? TryReadOwnershipMarkerWriteFile(string writePath)
    {
        try
        {
            return JsonSerializer.Deserialize<MoveOwnershipMarker>(
                File.ReadAllText(writePath));
        }
        catch (Exception exception) when (exception is
            JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
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
            || !string.Equals(marker.OwnedArtifactType, expected.OwnedArtifactType, StringComparison.Ordinal)
            || (marker.OwnedDirectoryPath == null) != (expected.OwnedDirectoryPath == null))
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
                    directorySemantics)
                || (marker.OwnedDirectoryPath != null
                    && expected.OwnedDirectoryPath != null
                    && !FileSystemPathIdentity.AreEquivalent(
                        marker.OwnedDirectoryPath,
                        expected.OwnedDirectoryPath,
                        directorySemantics)))
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
        MoveLeaseToken leaseToken,
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
            if (!TryParseMarkerWriteIdentity(writePath, markerPath, out var writeIdentity)
                || writeIdentity.JobId != expected.JobId)
            {
                throw new MoveNeedsAttentionException(
                    "An ownership-marker write filename does not match the active move job.");
            }

            if (writeIdentity.LeaseGeneration > leaseToken.Generation)
            {
                throw new MoveNeedsAttentionException(
                    "A future-generation ownership-marker write file was preserved.");
            }

            var writeMarker = TryReadOwnershipMarkerWriteFile(writePath);
            if (writeMarker == null)
            {
                if (writeIdentity.LeaseGeneration >= leaseToken.Generation)
                {
                    throw new MoveNeedsAttentionException(
                        "A current or future-generation ownership-marker write file is truncated and was preserved.");
                }

                await authorizeMutation();
                ValidateOwnershipMarkerWritePath(writePath, markerDirectory);
                File.Delete(writePath);
                continue;
            }

            ValidateOwnershipMarker(
                writeMarker,
                expected,
                sourceSemantics,
                targetSemantics,
                directorySemantics);
            await authorizeMutation();
            ValidateOwnershipMarkerWritePath(writePath, markerDirectory);
            writeMarker = TryReadOwnershipMarkerWriteFile(writePath)
                ?? throw new MoveNeedsAttentionException(
                    "An ownership-marker write file changed before deletion.");
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

    private static string GetCleanupDirectoryPath(
        string directoryPath,
        string ownedArtifactType,
        Guid jobId)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(directoryPath))
            ?? throw new MoveNeedsAttentionException("The owned directory parent is unavailable.");
        return Path.Join(
            parent,
            $".listenarr-{ownedArtifactType}-{jobId:N}.cleanup-dir");
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
