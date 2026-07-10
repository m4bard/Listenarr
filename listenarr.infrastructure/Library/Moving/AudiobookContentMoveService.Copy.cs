using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private async Task CopySourceContentsAsync(
        string source,
        string copyDestination,
        IReadOnlyList<MoveJobEntry> manifest,
        Guid jobId,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics,
        CancellationToken cancellationToken)
    {
        ValidateExistingDestinationContents(
            source,
            copyDestination,
            manifest,
            jobId,
            targetSemantics);

        foreach (var manifestEntry in manifest.OrderBy(entry => entry.EntryType))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!FileSystemPathIdentity.TryResolveRelativePathWithinBase(
                copyDestination,
                manifestEntry.RelativePath,
                targetSemantics,
                out var destinationPath))
            {
                throw new IOException($"Move entry destination escaped target root: {manifestEntry.RelativePath}");
            }

            if (!FileSystemSafety.TryValidateMutationTarget(
                destinationPath,
                [copyDestination],
                out destinationPath,
                out var destinationReason))
            {
                throw new MoveNeedsAttentionException(destinationReason);
            }

            if (manifestEntry.EntryType == MoveJobEntryType.Directory)
            {
                if (Directory.Exists(destinationPath)
                    && (File.GetAttributes(destinationPath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new MoveNeedsAttentionException(
                        $"Move destination directory is a symbolic link or reparse point: {manifestEntry.RelativePath}");
                }

                if (!Directory.Exists(destinationPath)) Directory.CreateDirectory(destinationPath);
                continue;
            }

            if (!FileSystemPathIdentity.TryResolveRelativePathWithinBase(
                source,
                manifestEntry.RelativePath,
                sourceSemantics,
                out var entry))
            {
                throw new IOException($"Move entry escaped source root: {manifestEntry.RelativePath}");
            }

            await CopyFileWithRetryAsync(entry, destinationPath, jobId, copyDestination, cancellationToken);
        }
    }

    private void ValidateExistingDestinationContents(
        string source,
        string destinationRoot,
        IReadOnlyCollection<MoveJobEntry> manifest,
        Guid jobId,
        FileSystemPathSemantics targetSemantics)
    {
        if (!Directory.Exists(destinationRoot))
        {
            return;
        }

        if (!FileSystemSafety.TryEnumerateTreeWithoutLinks(
            destinationRoot,
            out var files,
            out var directories,
            out var reason))
        {
            throw new MoveNeedsAttentionException(reason);
        }

        var expectedPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest)
        {
            if (!FileSystemPathIdentity.TryResolveRelativePathWithinBase(
                destinationRoot,
                entry.RelativePath,
                targetSemantics,
                out var expectedPath))
            {
                throw new MoveNeedsAttentionException("A manifest entry escaped the destination root.");
            }

            expectedPaths.Add(FileSystemPathIdentity.CreateKey("move-target", expectedPath, targetSemantics));
        }

        var markerPath = GetRecoveryMarkerPath(destinationRoot, jobId);
        var partialSuffix = $".listenarr-{jobId:N}.partial";
        var sourceInsideDestination = IsSameOrInside(source, destinationRoot, targetSemantics);

        foreach (var directory in directories)
        {
            if (sourceInsideDestination && IsSameOrInside(directory, source, targetSemantics))
            {
                continue;
            }

            var key = FileSystemPathIdentity.CreateKey("move-target", directory, targetSemantics);
            if (!expectedPaths.Contains(key))
            {
                throw new MoveNeedsAttentionException(
                    $"Destination contains an unowned directory: {Path.GetRelativePath(destinationRoot, directory)}");
            }
        }

        foreach (var file in files)
        {
            if (FileSystemPathIdentity.AreEquivalent(file, markerPath, targetSemantics)
                || (sourceInsideDestination && IsSameOrInside(file, source, targetSemantics)))
            {
                continue;
            }

            var expectedFile = file.EndsWith(partialSuffix, StringComparison.Ordinal)
                ? file[..^partialSuffix.Length]
                : file;
            var key = FileSystemPathIdentity.CreateKey("move-target", expectedFile, targetSemantics);
            if (!expectedPaths.Contains(key))
            {
                throw new MoveNeedsAttentionException(
                    $"Destination contains an unowned file: {Path.GetRelativePath(destinationRoot, file)}");
            }
        }
    }

    private async Task CopyFileWithRetryAsync(
        string sourceFile,
        string destinationFile,
        Guid jobId,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationFile);
        if (!string.IsNullOrEmpty(destinationDirectory) && !Directory.Exists(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        var partialFile = destinationFile + $".listenarr-{jobId:N}.partial";
        for (var attempt = 1; attempt <= MaxCopyAttempts; attempt++)
        {
            try
            {
                if (!FileSystemSafety.TryValidateMutationTarget(
                    destinationFile,
                    [destinationRoot],
                    out destinationFile,
                    out var destinationReason))
                {
                    throw new MoveNeedsAttentionException(destinationReason);
                }

                if (!FileSystemSafety.TryValidateMutationTarget(
                    partialFile,
                    [destinationRoot],
                    out partialFile,
                    out var partialReason))
                {
                    throw new MoveNeedsAttentionException(partialReason);
                }

                if (File.Exists(destinationFile))
                {
                    if (await FileSystemSafety.FilesHaveSameContentAsync(sourceFile, destinationFile, cancellationToken))
                    {
                        TryDeleteOwnedPartial(partialFile);
                        logger.LogInformation(
                            "Skipping copy for move job {JobId}; destination already has identical content: {Destination}",
                            jobId,
                            LogRedaction.SanitizeFilePath(destinationFile));
                        return;
                    }

                    throw new MoveNeedsAttentionException(
                        $"Destination file differs from the move manifest and will not be overwritten: {Path.GetFileName(destinationFile)}");
                }

                TryDeleteOwnedPartial(partialFile);
                File.Copy(sourceFile, partialFile, false);
                PreserveFileMetadata(sourceFile, partialFile);
                if (!await FileSystemSafety.FilesHaveSameContentAsync(sourceFile, partialFile, cancellationToken))
                {
                    TryDeleteOwnedPartial(partialFile);
                    throw new IOException("Temporary move copy failed byte verification.");
                }

                if (File.Exists(destinationFile))
                {
                    if (await FileSystemSafety.FilesHaveSameContentAsync(sourceFile, destinationFile, cancellationToken))
                    {
                        TryDeleteOwnedPartial(partialFile);
                        return;
                    }

                    throw new MoveNeedsAttentionException(
                        $"Destination file appeared during the move and differs from the manifest: {Path.GetFileName(destinationFile)}");
                }

                File.Move(partialFile, destinationFile, overwrite: false);
                return;
            }
            catch (MoveNeedsAttentionException)
            {
                throw;
            }
            catch (IOException exception) when (attempt < MaxCopyAttempts)
            {
                logger.LogWarning(
                    exception,
                    "IO error copying file {File} attempt {Attempt}",
                    LogRedaction.SanitizeFilePath(sourceFile),
                    attempt);
                var delay = TimeSpan.FromSeconds(Math.Min(8, Math.Pow(2, attempt - 1)));
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new IOException($"Failed to copy file after {MaxCopyAttempts} attempts: {sourceFile}");
    }

    private static void TryDeleteOwnedPartial(string partialFile)
    {
        try
        {
            if (File.Exists(partialFile))
            {
                File.Delete(partialFile);
            }
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            System.Diagnostics.Debug.WriteLine(
                $"Suppressed move partial cleanup failure for '{partialFile}': {exception.Message}");
        }
    }

    private void PreserveFileMetadata(string sourceFile, string destinationFile)
    {
        try
        {
            var attributes = File.GetAttributes(sourceFile);
            File.SetAttributes(destinationFile, attributes);
            File.SetLastWriteTimeUtc(destinationFile, File.GetLastWriteTimeUtc(sourceFile));
            File.SetCreationTimeUtc(destinationFile, File.GetCreationTimeUtc(sourceFile));
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            logger.LogDebug(
                exception,
                "Non-fatal: failed to preserve attributes for {File}",
                LogRedaction.SanitizeFilePath(sourceFile));
        }
    }
}
