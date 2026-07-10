from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, content: str) -> None:
    (ROOT / path).write_text(content, encoding="utf-8", newline="\n")


def replace_once(path: str, old: str, new: str) -> None:
    content = read(path)
    count = content.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected one match, found {count}")
    write(path, content.replace(old, new, 1))


write(
    "listenarr.infrastructure/Library/Moving/AudiobookContentMoveService.Recovery.cs",
    '''/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Text.Json;
using Listenarr.Domain.Common;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private const int RecoveryMarkerVersion = 1;
    private const string CopyStartedStage = "copy-started";
    private const string CopyCompletedStage = "copy-complete";
    private const string AtomicRenameCompletedStage = "atomic-rename-complete";
    private const string SourceCleanupCompletedStage = "source-cleanup-complete";

    private sealed record MoveRecoveryMarker(
        int Version,
        Guid JobId,
        string Source,
        string Target,
        string Stage,
        bool IsLegacy = false);

    private static void WriteRecoveryMarker(
        string markerDirectory,
        Guid jobId,
        string source,
        string target,
        string stage)
    {
        var markerPath = GetRecoveryMarkerPath(markerDirectory, jobId);
        if (OperatingSystem.IsWindows() && File.Exists(markerPath))
        {
            File.SetAttributes(markerPath, FileAttributes.Normal);
        }

        var marker = new MoveRecoveryMarker(
            RecoveryMarkerVersion,
            jobId,
            Path.GetFullPath(source),
            Path.GetFullPath(target),
            stage);
        File.WriteAllText(markerPath, JsonSerializer.Serialize(marker));
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(markerPath, FileAttributes.Hidden);
        }
    }

    private MoveRecoveryMarker? ReadRecoveryMarker(string markerPath)
    {
        if (!File.Exists(markerPath))
        {
            return null;
        }

        try
        {
            var content = File.ReadAllText(markerPath).Trim();
            if (IsKnownRecoveryStage(content))
            {
                return new MoveRecoveryMarker(
                    0,
                    Guid.Empty,
                    string.Empty,
                    string.Empty,
                    content,
                    IsLegacy: true);
            }

            var marker = JsonSerializer.Deserialize<MoveRecoveryMarker>(content);
            if (marker == null || !IsKnownRecoveryStage(marker.Stage))
            {
                throw new MoveNeedsAttentionException("Move recovery marker is invalid.");
            }

            return marker;
        }
        catch (MoveNeedsAttentionException)
        {
            throw;
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            logger.LogWarning(
                exception,
                "Failed to read move recovery marker {Marker}",
                LogRedaction.SanitizeFilePath(markerPath));
            throw new MoveNeedsAttentionException("Move recovery marker could not be read safely.");
        }
    }

    private static void ValidateRecoveryMarker(
        MoveRecoveryMarker? marker,
        AudiobookContentMoveRequest request,
        string source,
        string target)
    {
        if (marker == null || marker.IsLegacy)
        {
            return;
        }

        if (marker.Version != RecoveryMarkerVersion || marker.JobId != request.JobId)
        {
            throw new MoveNeedsAttentionException(
                "Move recovery marker is owned by a different job or unsupported manifest version.");
        }

        try
        {
            if (!FileSystemPathIdentity.AreEquivalent(marker.Source, source, request.SourceSemantics)
                || !FileSystemPathIdentity.AreEquivalent(marker.Target, target, request.TargetSemantics))
            {
                throw new MoveNeedsAttentionException(
                    "Move recovery marker source or target identity does not match the persisted job.");
            }
        }
        catch (ArgumentException)
        {
            throw new MoveNeedsAttentionException(
                "Move recovery marker contains an invalid source or target identity.");
        }
    }

    private static bool IsKnownRecoveryStage(string? stage) =>
        stage is CopyStartedStage
            or CopyCompletedStage
            or AtomicRenameCompletedStage
            or SourceCleanupCompletedStage;

    private static string GetRecoveryMarkerPath(string target, Guid jobId) =>
        Path.Join(target, $".listenarr-move-{jobId:N}.pending");
}
''',
)

replace_once(
    "listenarr.infrastructure/Library/Moving/AudiobookContentMoveService.Persistence.cs",
    """    private async Task PersistManifestAsync(
""",
    """    private async Task ValidatePersistedMoveIdentityAsync(
        Guid jobId,
        string source,
        string target,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var identity = await db.MoveJobs
            .AsNoTracking()
            .Where(job => job.Id == jobId)
            .Select(job => new { job.SourcePath, job.RequestedPath })
            .SingleOrDefaultAsync(cancellationToken);
        if (identity == null
            || string.IsNullOrWhiteSpace(identity.SourcePath)
            || string.IsNullOrWhiteSpace(identity.RequestedPath))
        {
            throw new MoveNeedsAttentionException(
                "Persisted move source and target identity are required before filesystem recovery.");
        }

        try
        {
            if (!FileSystemPathIdentity.AreEquivalent(identity.SourcePath, source, sourceSemantics)
                || !FileSystemPathIdentity.AreEquivalent(identity.RequestedPath, target, targetSemantics))
            {
                throw new MoveNeedsAttentionException(
                    "Persisted move source or target identity does not match the requested filesystem operation.");
            }
        }
        catch (ArgumentException)
        {
            throw new MoveNeedsAttentionException(
                "Persisted move source or target identity is invalid.");
        }
    }

    private async Task PersistManifestAsync(
""",
)

main_path = "listenarr.infrastructure/Library/Moving/AudiobookContentMoveService.cs"
replace_once(
    main_path,
    """        var sourceSemantics = request.SourceSemantics;
        var targetSemantics = request.TargetSemantics;
""",
    """        var sourceSemantics = request.SourceSemantics;
        var targetSemantics = request.TargetSemantics;
        await ValidatePersistedMoveIdentityAsync(
            request.JobId,
            source,
            target,
            sourceSemantics,
            targetSemantics,
            cancellationToken);
""",
)
replace_once(
    main_path,
    """        var recoveryMarkerPath = GetRecoveryMarkerPath(target, request.JobId);
        var recoveryStage = ReadRecoveryStage(recoveryMarkerPath);
        if (string.Equals(recoveryStage, CopyCompletedStage, StringComparison.Ordinal)
            && LoadManifest(request.JobId).Count == 0)
        {
            throw new MoveNeedsAttentionException(
                "A legacy copy-complete marker has no byte-verified manifest; source cleanup is blocked.");
        }

        var resumingDirectCopy = string.Equals(recoveryStage, CopyStartedStage, StringComparison.Ordinal);
        EnsureTargetCanReceiveContents(source, target, sourceInsideTarget, resumingDirectCopy, targetSemantics);
""",
    """        var recoveryMarkerPath = GetRecoveryMarkerPath(target, request.JobId);
        var recoveryMarker = ReadRecoveryMarker(recoveryMarkerPath);
        ValidateRecoveryMarker(recoveryMarker, request, source, target);
        var recoveryStage = recoveryMarker?.Stage;
        var persistedManifest = LoadManifest(request.JobId);
        if (recoveryStage is CopyStartedStage or CopyCompletedStage or SourceCleanupCompletedStage
            && persistedManifest.Count == 0)
        {
            throw new MoveNeedsAttentionException(
                "A move recovery marker exists without a persisted manifest; destination ownership cannot be proven.");
        }

        var resumingDirectCopy = recoveryStage == CopyStartedStage && persistedManifest.Count > 0;
        EnsureTargetCanReceiveContents(source, target, sourceInsideTarget, resumingDirectCopy, targetSemantics);
""",
)
replace_once(
    main_path,
    """                WriteRecoveryMarker(source, request.JobId, AtomicRenameCompletedStage);
""",
    """                WriteRecoveryMarker(
                    source,
                    request.JobId,
                    source,
                    target,
                    AtomicRenameCompletedStage);
""",
)
replace_once(
    main_path,
    """            var manifest = await LoadOrCreateManifestAsync(
                request.JobId,
                request.LeaseToken,
                source,
                target,
                targetInsideSource,
                sourceSemantics,
                cancellationToken);
""",
    """            var manifest = persistedManifest.Count > 0
                ? persistedManifest
                : await LoadOrCreateManifestAsync(
                    request.JobId,
                    request.LeaseToken,
                    source,
                    target,
                    targetInsideSource,
                    sourceSemantics,
                    cancellationToken);
""",
)
replace_once(
    main_path,
    """                WriteRecoveryMarker(copyDestination, request.JobId, CopyStartedStage);
""",
    """                WriteRecoveryMarker(
                    copyDestination,
                    request.JobId,
                    source,
                    target,
                    CopyStartedStage);
""",
)
replace_once(
    main_path,
    """            WriteRecoveryMarker(copyDestination, request.JobId, CopyCompletedStage);
""",
    """            WriteRecoveryMarker(
                copyDestination,
                request.JobId,
                source,
                target,
                CopyCompletedStage);
""",
)
replace_once(
    main_path,
    """            WriteRecoveryMarker(target, request.JobId, SourceCleanupCompletedStage);
""",
    """            WriteRecoveryMarker(
                target,
                request.JobId,
                source,
                target,
                SourceCleanupCompletedStage);
""",
)
replace_once(
    main_path,
    """        var recoveryMarkerPath = GetRecoveryMarkerPath(target, request.JobId);
        var recoveryStage = ReadRecoveryStage(recoveryMarkerPath);
        var sourceSemantics = request.SourceSemantics;
        var targetSemantics = request.TargetSemantics;
        var manifest = LoadManifest(request.JobId);
""",
    """        var recoveryMarkerPath = GetRecoveryMarkerPath(target, request.JobId);
        var sourceSemantics = request.SourceSemantics;
        var targetSemantics = request.TargetSemantics;
        MoveRecoveryMarker? recoveryMarker;
        try
        {
            recoveryMarker = ReadRecoveryMarker(recoveryMarkerPath);
            ValidateRecoveryMarker(recoveryMarker, request, source, target);
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            logger.LogWarning(
                exception,
                "Rejected invalid recovery marker for move job {JobId}",
                request.JobId);
            return null;
        }

        var recoveryStage = recoveryMarker?.Stage;
        var manifest = LoadManifest(request.JobId);
""",
)
replace_once(
    main_path,
    """            if (!atomicRenameCompleted)
            {
                await VerifyPublishedManifestAsync(
                    target,
                    manifest,
                    targetSemantics,
                    cancellationToken);
            }
""",
    """            if (!atomicRenameCompleted)
            {
                ValidateExistingDestinationContents(
                    source,
                    target,
                    manifest,
                    request.JobId,
                    targetSemantics);
                await VerifyPublishedManifestAsync(
                    target,
                    manifest,
                    targetSemantics,
                    cancellationToken);
            }
""",
)
replace_once(
    main_path,
    """        WriteRecoveryMarker(result.Target, request.JobId, SourceCleanupCompletedStage);
""",
    """        WriteRecoveryMarker(
            result.Target,
            request.JobId,
            result.Source,
            result.Target,
            SourceCleanupCompletedStage);
""",
)
replace_once(
    main_path,
    """        return Directory
            .EnumerateFileSystemEntries(source, "*", SearchOption.AllDirectories)
            .All(entry => IsSameOrInside(entry, target, semantics) || IsSameOrInside(target, entry, semantics));
""",
    """        if (!FileSystemSafety.TryEnumerateTreeWithoutLinks(
                source,
                out var files,
                out var directories,
                out _))
        {
            return false;
        }

        return files
            .Concat(directories)
            .All(entry => IsSameOrInside(entry, target, semantics) || IsSameOrInside(target, entry, semantics));
""",
)
content = read(main_path)
pattern = re.compile(
    r"\n    private string\? ReadRecoveryStage\(string markerPath\)\n    \{\n.*?\n    \}\n\n    private static bool IsTargetEntryAllowedBySourceSubtree",
    re.DOTALL,
)
content, count = pattern.subn(
    "\n    private static bool IsTargetEntryAllowedBySourceSubtree",
    content,
    count=1,
)
if count != 1:
    raise RuntimeError(f"{main_path}: failed to remove ReadRecoveryStage")
write(main_path, content)

copy_path = "listenarr.infrastructure/Library/Moving/AudiobookContentMoveService.Copy.cs"
replace_once(
    copy_path,
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
    copy_path,
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
