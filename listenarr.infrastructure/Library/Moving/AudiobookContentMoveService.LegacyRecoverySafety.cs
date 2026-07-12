namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private static bool HasLegacyFilesystemRecoveryArtifacts(
        string source,
        string target,
        Guid jobId)
    {
        var sourceMarker = GetRecoveryMarkerPath(source, jobId);
        var targetMarker = GetRecoveryMarkerPath(target, jobId);
        if (HasMarkerPublicationEvidence(source, sourceMarker)
            || HasMarkerPublicationEvidence(target, targetMarker))
        {
            return true;
        }

        var targetParent = Path.GetDirectoryName(target);
        if (!string.IsNullOrWhiteSpace(targetParent))
        {
            var tempDirectory = Path.Join(
                targetParent,
                Path.GetFileName(target) + ".tmp-" + jobId.ToString("N"));
            if (ArtifactPathExists(tempDirectory)
                || ArtifactPathExists(GetCleanupDirectoryPath(
                    tempDirectory,
                    TemporaryDirectoryArtifactType,
                    jobId))
                || HasMarkerPublicationEvidence(
                    targetParent,
                    GetCleanupTombstonePath(
                        tempDirectory,
                        TemporaryDirectoryArtifactType,
                        jobId)))
            {
                return true;
            }
        }

        var sourceParent = Path.GetDirectoryName(source);
        if (!string.IsNullOrWhiteSpace(sourceParent))
        {
            var quarantineDirectory = Path.Join(
                sourceParent,
                $".listenarr-quarantine-{jobId:N}");
            if (ArtifactPathExists(quarantineDirectory)
                || ArtifactPathExists(GetCleanupDirectoryPath(
                    quarantineDirectory,
                    QuarantineDirectoryArtifactType,
                    jobId))
                || HasMarkerPublicationEvidence(
                    sourceParent,
                    GetCleanupTombstonePath(
                        quarantineDirectory,
                        QuarantineDirectoryArtifactType,
                        jobId)))
            {
                return true;
            }
        }

        if (!Directory.Exists(target))
        {
            return false;
        }

        ValidateExistingMoveDirectory(target, "legacy recovery target");
        var publishedTempMarker = Path.Join(target, TempOwnershipMarkerFileName);
        if (HasMarkerPublicationEvidence(target, publishedTempMarker))
        {
            return true;
        }

        if (!FileSystemSafety.TryEnumerateTreeWithoutLinks(
                target,
                out var files,
                out _,
                out var reason))
        {
            throw new MoveNeedsAttentionException(
                $"Legacy move recovery artifacts could not be inspected safely: {reason}");
        }

        var partialSuffix = $".listenarr-{jobId:N}.partial";
        return files.Any(file =>
            file.EndsWith(partialSuffix, StringComparison.Ordinal));
    }

    private static bool HasMarkerPublicationEvidence(
        string directory,
        string markerPath)
    {
        if (ArtifactPathExists(markerPath))
        {
            return true;
        }

        if (!Directory.Exists(directory))
        {
            return false;
        }

        ValidateExistingMoveDirectory(directory, "legacy recovery artifact directory");
        return Directory.EnumerateFiles(
            directory,
            Path.GetFileName(markerPath) + ".writing-*",
            SearchOption.TopDirectoryOnly).Any();
    }

    private static bool ArtifactPathExists(string path) =>
        File.Exists(path) || Directory.Exists(path);
}
