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
    Guid JobId);

internal sealed record AudiobookContentMoveResult(
    string Source,
    string Target,
    bool TargetInsideSource,
    bool SourceInsideTarget);

internal sealed class AudiobookContentMoveService(ILogger<AudiobookContentMoveService> logger)
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

        EnsureTargetCanReceiveContents(source, target, sourceInsideTarget);

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

            await CopySourceContentsAsync(
                source,
                target,
                copyDestination,
                targetInsideSource,
                request.JobId,
                cancellationToken);

            if (useTemp)
            {
                Directory.Move(tempName, target);
            }

            DeleteOriginalSource(source, target, targetInsideSource);

            return new AudiobookContentMoveResult(
                source,
                target,
                targetInsideSource,
                sourceInsideTarget);
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            TryDeleteTempDirectory(tempName, targetParent);
            throw;
        }
    }

    private static void EnsureTargetCanReceiveContents(
        string source,
        string target,
        bool sourceInsideTarget)
    {
        if (!Directory.Exists(target))
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
            throw new IOException("Target directory already exists and contains files");
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
                if (File.Exists(destinationFile)
                    && await FileSystemSafety.FilesHaveSameContentAsync(sourceFile, destinationFile, cancellationToken))
                {
                    logger.LogInformation(
                        "Skipping copy for move job {JobId}; destination already has identical content: {Destination}",
                        jobId,
                        LogRedaction.SanitizeFilePath(destinationFile));
                    return;
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

    private static void DeleteOriginalSource(string source, string target, bool targetInsideSource)
    {
        if (!Directory.Exists(source) || IsFilesystemRoot(source))
        {
            throw new IOException("Source path became invalid before cleanup.");
        }

        if (targetInsideSource)
        {
            DeleteSourceContentsExceptTarget(source, target);
            return;
        }

        Directory.Delete(source, true);
        DeleteEmptySourceAncestors(source, target);
    }

    private static void DeleteEmptySourceAncestors(string source, string target)
    {
        var candidate = Path.GetDirectoryName(source);
        while (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate) && !IsFilesystemRoot(candidate))
        {
            // Stop at the destination or any folder containing it, but allow empty wrapper
            // folders inside the destination to be removed after source-to-parent moves.
            if (IsSameOrInside(target, candidate))
            {
                return;
            }

            if (Directory.EnumerateFileSystemEntries(candidate).Any())
            {
                return;
            }

            Directory.Delete(candidate, false);
            candidate = Path.GetDirectoryName(candidate);
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
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
        {
            File.Delete(file);
        }

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.TopDirectoryOnly))
        {
            if (IsSameOrInside(directory, target) || IsSameOrInside(target, directory))
            {
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

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(normalizedCandidate, normalizedRoot, comparison)
            || FileUtils.IsPathInsideOf(normalizedCandidate, normalizedRoot);
    }

    private static bool IsFilesystemRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !string.IsNullOrWhiteSpace(root)
            && string.Equals(fullPath, root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
