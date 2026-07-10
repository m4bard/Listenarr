from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PATH = ROOT / "listenarr.infrastructure/Library/Moving/AudiobookContentMoveService.Copy.cs"


def read() -> str:
    return PATH.read_text(encoding="utf-8")


def write(content: str) -> None:
    PATH.write_text(content, encoding="utf-8", newline="\n")


def replace_once(old: str, new: str) -> None:
    content = read()
    count = content.count(old)
    if count != 1:
        raise RuntimeError(f"AudiobookContentMoveService.Copy.cs: expected one match, found {count}")
    write(content.replace(old, new, 1))


replace_once(
    """            await CopyFileWithRetryAsync(
                entry,
                destinationPath,
                jobId,
                copyDestination,
                destinationIsJobOwnedTemp,
                cancellationToken);
""",
    """            await CopyFileWithRetryAsync(
                entry,
                destinationPath,
                manifestEntry,
                jobId,
                copyDestination,
                destinationIsJobOwnedTemp,
                cancellationToken);
""",
)
replace_once(
    '''    private async Task CopyFileWithRetryAsync(
        string sourceFile,
        string destinationFile,
        Guid jobId,
        string destinationRoot,
        bool destinationIsJobOwnedTemp,
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

                    if (!destinationIsJobOwnedTemp)
                    {
                        throw new MoveNeedsAttentionException(
                            $"Destination file differs from the move manifest and will not be overwritten: {Path.GetFileName(destinationFile)}");
                    }

                    File.Delete(destinationFile);
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
''',
    '''    private async Task CopyFileWithRetryAsync(
        string sourceFile,
        string destinationFile,
        MoveJobEntry manifestEntry,
        Guid jobId,
        string destinationRoot,
        bool destinationIsJobOwnedTemp,
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
                if (!await FileMatchesManifestAsync(sourceFile, manifestEntry, cancellationToken))
                {
                    throw new MoveNeedsAttentionException(
                        $"Source file no longer matches the persisted move manifest: {manifestEntry.RelativePath}");
                }

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
                    if (await FileMatchesManifestAsync(destinationFile, manifestEntry, cancellationToken))
                    {
                        TryDeleteOwnedPartial(partialFile);
                        logger.LogInformation(
                            "Skipping copy for move job {JobId}; destination already matches the persisted manifest: {Destination}",
                            jobId,
                            LogRedaction.SanitizeFilePath(destinationFile));
                        return;
                    }

                    if (!destinationIsJobOwnedTemp)
                    {
                        throw new MoveNeedsAttentionException(
                            $"Destination file differs from the move manifest and will not be overwritten: {Path.GetFileName(destinationFile)}");
                    }

                    File.Delete(destinationFile);
                }

                if (File.Exists(partialFile))
                {
                    if (await FileMatchesManifestAsync(partialFile, manifestEntry, cancellationToken))
                    {
                        File.Move(partialFile, destinationFile, overwrite: false);
                        return;
                    }

                    TryDeleteOwnedPartial(partialFile);
                }

                File.Copy(sourceFile, partialFile, false);
                PreserveFileMetadata(sourceFile, partialFile);
                if (!await FileMatchesManifestAsync(partialFile, manifestEntry, cancellationToken))
                {
                    TryDeleteOwnedPartial(partialFile);
                    throw new IOException("Temporary move copy failed persisted-manifest verification.");
                }

                if (!FileSystemSafety.TryValidateMutationTarget(
                        destinationFile,
                        [destinationRoot],
                        out destinationFile,
                        out destinationReason)
                    || !FileSystemSafety.TryValidateMutationTarget(
                        partialFile,
                        [destinationRoot],
                        out partialFile,
                        out partialReason))
                {
                    TryDeleteOwnedPartial(partialFile);
                    throw new MoveNeedsAttentionException(
                        string.IsNullOrWhiteSpace(destinationReason)
                            ? partialReason
                            : destinationReason);
                }

                if (File.Exists(destinationFile))
                {
                    if (await FileMatchesManifestAsync(destinationFile, manifestEntry, cancellationToken))
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

    private static async Task<bool> FileMatchesManifestAsync(
        string path,
        MoveJobEntry manifestEntry,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)
            || manifestEntry.EntryType != MoveJobEntryType.File
            || new FileInfo(path).Length != manifestEntry.Length
            || string.IsNullOrWhiteSpace(manifestEntry.Sha256))
        {
            return false;
        }

        return string.Equals(
            await ComputeSha256Async(path, cancellationToken),
            manifestEntry.Sha256,
            StringComparison.Ordinal);
    }
''',
)
