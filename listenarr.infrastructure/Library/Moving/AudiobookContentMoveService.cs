/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed record AudiobookContentMoveRequest(
    string Source,
    string Target,
    Guid JobId,
    bool DeleteEmptySource = true);

internal sealed record AudiobookContentMoveResult(
    string Source,
    string Target,
    bool TargetInsideSource,
    bool SourceInsideTarget,
    string RecoveryMarkerPath,
    bool SourceCleanupCompleted);

internal sealed partial class AudiobookContentMoveService(ILogger<AudiobookContentMoveService> logger)
{
    private const int MaxCopyAttempts = 5;

    public async Task<AudiobookContentMoveResult> MoveContentsAsync(
        AudiobookContentMoveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var source = Path.GetFullPath(request.Source);
        var target = Path.GetFullPath(request.Target);
        var targetInsideSource = FileUtils.IsPathInsideOf(target, source);
        var sourceInsideTarget = FileUtils.IsPathInsideOf(source, target);

        var targetParent = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(targetParent))
        {
            throw new IOException("Invalid target path");
        }

        if (!Directory.Exists(targetParent)) Directory.CreateDirectory(targetParent);

        var recoveryMarkerPath = GetRecoveryMarkerPath(target, request.JobId);
        var recoveryStage = ReadRecoveryStage(recoveryMarkerPath);
        var resumingDirectCopy = string.Equals(recoveryStage, CopyStartedStage, StringComparison.Ordinal);
        EnsureTargetCanReceiveContents(source, target, sourceInsideTarget, resumingDirectCopy);

        var tempName = Path.Join(targetParent, Path.GetFileName(target) + ".tmp-" + request.JobId.ToString("N"));
        if (!FileSystemSafety.TryValidateMutationTarget(tempName, [targetParent], out tempName, out var tempReason))
        {
            logger.LogWarning("Blocked move temp path for job {JobId}: {Reason}", request.JobId, tempReason);
            throw new IOException(tempReason);
        }

        try
        {
            // The move operation relocates the contents of the audiobook BasePath, not the
            // BasePath directory itself. Child destinations must copy directly and skip their
            // own subtree to avoid recursively copying the destination into itself.
            var useTemp = !targetInsideSource && !Directory.Exists(target);
            var copyDestination = useTemp ? tempName : target;

            if (useTemp) Directory.CreateDirectory(tempName);
            if (!Directory.Exists(copyDestination)) Directory.CreateDirectory(copyDestination);
            if (!useTemp && !resumingDirectCopy)
            {
                WriteRecoveryMarker(copyDestination, request.JobId, CopyStartedStage);
            }

            await CopySourceContentsAsync(
                source,
                target,
                copyDestination,
                targetInsideSource,
                request.JobId,
                cancellationToken);

            WriteRecoveryMarker(copyDestination, request.JobId, CopyCompletedStage);

            if (useTemp)
            {
                Directory.Move(tempName, target);
            }

            DeleteOriginalSource(source, target, targetInsideSource, request.DeleteEmptySource);
            WriteRecoveryMarker(target, request.JobId, SourceCleanupCompletedStage);

            return new AudiobookContentMoveResult(
                source,
                target,
                targetInsideSource,
                sourceInsideTarget,
                recoveryMarkerPath,
                SourceCleanupCompleted: true);
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            TryDeleteTempDirectory(tempName, targetParent);
            throw;
        }
    }

    public bool TryGetRecoverableMove(
        AudiobookContentMoveRequest request,
        out AudiobookContentMoveResult result)
    {
        var source = Path.GetFullPath(request.Source);
        var target = Path.GetFullPath(request.Target);
        var recoveryMarkerPath = GetRecoveryMarkerPath(target, request.JobId);
        var recoveryStage = ReadRecoveryStage(recoveryMarkerPath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (IsFilesystemRoot(source)
            || IsFilesystemRoot(target)
            || string.Equals(source, target, comparison)
            || !Directory.Exists(target)
            || recoveryStage is not (CopyCompletedStage or SourceCleanupCompletedStage))
        {
            result = null!;
            return false;
        }

        var targetInsideSource = FileUtils.IsPathInsideOf(target, source);
        var sourceInsideTarget = FileUtils.IsPathInsideOf(source, target);
        result = new AudiobookContentMoveResult(
            source,
            target,
            targetInsideSource,
            sourceInsideTarget,
            recoveryMarkerPath,
            string.Equals(recoveryStage, SourceCleanupCompletedStage, StringComparison.Ordinal));
        return true;
    }

    public AudiobookContentMoveResult ResumeSourceCleanup(
        AudiobookContentMoveRequest request,
        AudiobookContentMoveResult result)
    {
        if (result.SourceCleanupCompleted)
        {
            return result;
        }

        DeleteOriginalSource(
            result.Source,
            result.Target,
            result.TargetInsideSource,
            request.DeleteEmptySource);
        WriteRecoveryMarker(result.Target, request.JobId, SourceCleanupCompletedStage);
        return result with { SourceCleanupCompleted = true };
    }

    public void CompleteMove(AudiobookContentMoveResult result)
    {
        try
        {
            if (File.Exists(result.RecoveryMarkerPath))
            {
                File.Delete(result.RecoveryMarkerPath);
            }
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            logger.LogWarning(
                exception,
                "Failed to remove move recovery marker {Marker}",
                LogRedaction.SanitizeFilePath(result.RecoveryMarkerPath));
        }
    }

    public bool IsSourceCleanupComplete(string? sourcePath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return true;
        }

        var source = Path.GetFullPath(sourcePath);
        if (!Directory.Exists(source))
        {
            return true;
        }

        var target = Path.GetFullPath(targetPath);
        if (!FileUtils.IsPathInsideOf(target, source))
        {
            return !Directory.EnumerateFileSystemEntries(source).Any();
        }

        return Directory
            .EnumerateFileSystemEntries(source, "*", SearchOption.AllDirectories)
            .All(entry => IsSameOrInside(entry, target) || IsSameOrInside(target, entry));
    }

    private static void EnsureTargetCanReceiveContents(
        string source,
        string target,
        bool sourceInsideTarget,
        bool resumingOwnedDirectCopy)
    {
        if (!Directory.Exists(target) || resumingOwnedDirectCopy)
        {
            return;
        }

        // When moving a child folder back into its parent, the target necessarily contains
        // the source subtree. That subtree is not a collision because it is the content being moved.
        var targetHasBlockingContent = Directory
            .EnumerateFileSystemEntries(target)
            .Any(entry => !(sourceInsideTarget && IsTargetEntryAllowedBySourceSubtree(entry, source)));
        if (targetHasBlockingContent)
        {
            throw new IOException(sourceInsideTarget
                ? "Destination contains unrelated content outside the source subtree"
                : "Target directory already exists and contains files");
        }
    }

    private async Task CopySourceContentsAsync(
        string source,
        string target,
        string copyDestination,
        bool targetInsideSource,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var entries = Directory.EnumerateFileSystemEntries(source, "*", SearchOption.AllDirectories);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (targetInsideSource && IsSameOrInside(entry, target))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(source, entry);
            if (!FileUtils.TryResolveRelativePathWithinBase(copyDestination, relativePath, out var destinationPath))
            {
                throw new IOException($"Move entry destination escaped target root: {relativePath}");
            }

            if (Directory.Exists(entry))
            {
                if (!Directory.Exists(destinationPath)) Directory.CreateDirectory(destinationPath);
                continue;
            }

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

                    // A previous run of this same move job can leave a truncated file in the
                    // job-scoped temp directory. Replace mismatched content so retries can heal.
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

            var lastWrite = File.GetLastWriteTimeUtc(sourceFile);
            var creation = File.GetCreationTimeUtc(sourceFile);
            File.SetLastWriteTimeUtc(destinationFile, lastWrite);
            File.SetCreationTimeUtc(destinationFile, creation);
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            logger.LogDebug(
                exception,
                "Non-fatal: failed to preserve attributes for {File}",
                LogRedaction.SanitizeFilePath(sourceFile));
        }
    }

    private static void DeleteOriginalSource(
        string source,
        string target,
        bool targetInsideSource,
        bool deleteEmptySource)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        if (IsFilesystemRoot(source))
        {
            throw new IOException("Source path became invalid before cleanup.");
        }

        if (targetInsideSource)
        {
            DeleteSourceContentsExceptTarget(source, target);
            return;
        }

        DeleteDirectoryContents(source);
        if (deleteEmptySource && Directory.Exists(source))
        {
            Directory.Delete(source, false);
        }
    }

    private static void DeleteDirectoryContents(string source)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(source).ToList())
        {
            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, true);
            }
            else
            {
                File.Delete(entry);
            }
        }
    }

    private string? ReadRecoveryStage(string markerPath)
    {
        try
        {
            return File.Exists(markerPath) ? File.ReadAllText(markerPath) : null;
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            logger.LogWarning(
                exception,
                "Failed to read move recovery marker {Marker}",
                LogRedaction.SanitizeFilePath(markerPath));
            return null;
        }
    }

    private static bool IsTargetEntryAllowedBySourceSubtree(string entry, string source)
    {
        if (IsSameOrInside(entry, source))
        {
            return true;
        }

        if (!Directory.Exists(entry) || !IsSameOrInside(source, entry))
        {
            return false;
        }

        return Directory
            .EnumerateFileSystemEntries(entry, "*", SearchOption.AllDirectories)
            .All(child => IsSameOrInside(child, source) || IsSameOrInside(source, child));
    }

    private static void DeleteSourceContentsExceptTarget(string source, string target)
    {
        foreach (var file in Directory
            .EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Where(file => !IsSameOrInside(file, target))
            .ToList())
        {
            File.Delete(file);
        }

        foreach (var directory in Directory
            .EnumerateDirectories(source, "*", SearchOption.AllDirectories)
            .OrderByDescending(directory => directory.Length)
            .ToList())
        {
            if (!Directory.Exists(directory) || IsSameOrInside(directory, target))
            {
                continue;
            }

            if (IsSameOrInside(target, directory))
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory, false);
                }

                continue;
            }

            Directory.Delete(directory, true);
        }
    }

    private static void TryDeleteTempDirectory(string tempName, string targetParent)
    {
        try
        {
            if (Directory.Exists(tempName)
                && FileSystemSafety.TryValidateMutationTarget(tempName, [targetParent], out var safeTempName, out _))
            {
                Directory.Delete(safeTempName, true);
            }
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            System.Diagnostics.Debug.WriteLine($"Suppressed temp cleanup failure: {exception.Message}");
        }
    }

    private static bool IsSameOrInside(string candidate, string root)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var normalizedCandidate = Path.GetFullPath(candidate);
        var normalizedRoot = Path.GetFullPath(root);

        return FileUtils.AreFilesystemPathsEquivalentForCurrentOs(normalizedCandidate, normalizedRoot)
            || FileUtils.IsPathInsideOf(normalizedCandidate, normalizedRoot);
    }

    private static bool IsFilesystemRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return !string.IsNullOrWhiteSpace(root)
            && FileUtils.AreFilesystemPathsEquivalentForCurrentOs(fullPath, root);
    }
}
