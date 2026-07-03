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
        FileSystemPathSemantics semantics,
        CancellationToken cancellationToken)
    {
        foreach (var manifestEntry in manifest.OrderBy(entry => entry.EntryType))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!FileSystemPathIdentity.TryResolveRelativePathWithinBase(
                copyDestination,
                manifestEntry.RelativePath,
                semantics,
                out var destinationPath))
            {
                throw new IOException($"Move entry destination escaped target root: {manifestEntry.RelativePath}");
            }

            if (manifestEntry.EntryType == MoveJobEntryType.Directory)
            {
                if (!Directory.Exists(destinationPath)) Directory.CreateDirectory(destinationPath);
                continue;
            }

            var entry = Path.Join(source, manifestEntry.RelativePath);
            await CopyFileWithRetryAsync(entry, destinationPath, jobId, cancellationToken);
        }
    }

    private async Task CopyFileWithRetryAsync(
        string sourceFile,
        string destinationFile,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationFile);
        if (!string.IsNullOrEmpty(destinationDirectory) && !Directory.Exists(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        for (var attempt = 1; attempt <= MaxCopyAttempts; attempt++)
        {
            try
            {
                if (File.Exists(destinationFile))
                {
                    if (await FileSystemSafety.FilesHaveSameContentAsync(sourceFile, destinationFile, cancellationToken))
                    {
                        logger.LogInformation(
                            "Skipping copy for move job {JobId}; destination already has identical content: {Destination}",
                            jobId,
                            LogRedaction.SanitizeFilePath(destinationFile));
                        return;
                    }

                    File.Delete(destinationFile);
                }

                File.Copy(sourceFile, destinationFile, false);
                PreserveFileMetadata(sourceFile, destinationFile);
                return;
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
