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
using Listenarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed record MoveLeaseToken(string Owner, int Generation);

internal sealed record AudiobookContentMoveRequest(
    string Source,
    string Target,
    Guid JobId,
    bool DeleteEmptySource,
    FileSystemPathSemantics SourceSemantics,
    FileSystemPathSemantics TargetSemantics,
    MoveLeaseToken LeaseToken)
{
    public string LeaseOwner => LeaseToken.Owner;
    public int LeaseGeneration => LeaseToken.Generation;
}

internal sealed record AudiobookContentMoveResult(
    string Source,
    string Target,
    bool TargetInsideSource,
    bool SourceInsideTarget,
    string RecoveryMarkerPath,
    bool SourceCleanupCompleted);

internal sealed class MoveNeedsAttentionException(string message) : IOException(message);

internal interface IMoveFaultInjector
{
    Task AfterPublishedAsync(Guid jobId, CancellationToken cancellationToken);
}

internal sealed partial class AudiobookContentMoveService(
    ILogger<AudiobookContentMoveService> logger,
    IDbContextFactory<ListenArrDbContext> dbContextFactory,
    TimeProvider timeProvider,
    IMoveFaultInjector? faultInjector = null)
{
    private const int MaxCopyAttempts = 5;

    public async Task<AudiobookContentMoveResult> MoveContentsAsync(
        AudiobookContentMoveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureLeaseOwnedAsync(request.JobId, request.LeaseToken, cancellationToken);

        var source = Path.GetFullPath(request.Source);
        var target = Path.GetFullPath(request.Target);
        var sourceSemantics = request.SourceSemantics;
        var targetSemantics = request.TargetSemantics;
        var targetInsideSource = IsSameOrInside(target, source, sourceSemantics);
        var sourceInsideTarget = IsSameOrInside(source, target, targetSemantics);

        var targetParent = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(targetParent))
        {
            throw new IOException("Invalid target path");
        }

        if (!Directory.Exists(targetParent)) Directory.CreateDirectory(targetParent);

        var recoveryMarkerPath = GetRecoveryMarkerPath(target, request.JobId);
        var recoveryStage = ReadRecoveryStage(recoveryMarkerPath);
        if (string.Equals(recoveryStage, CopyCompletedStage, StringComparison.Ordinal)
            && LoadManifest(request.JobId).Count == 0)
        {
            throw new MoveNeedsAttentionException(
                "A legacy copy-complete marker has no byte-verified manifest; source cleanup is blocked.");
        }

        var resumingDirectCopy = string.Equals(recoveryStage, CopyStartedStage, StringComparison.Ordinal);
        EnsureTargetCanReceiveContents(source, target, sourceInsideTarget, resumingDirectCopy, targetSemantics);

        var tempName = Path.Join(targetParent, Path.GetFileName(target) + ".tmp-" + request.JobId.ToString("N"));
        if (!FileSystemSafety.TryValidateMutationTarget(tempName, [targetParent], out tempName, out var tempReason))
        {
            logger.LogWarning("Blocked move temp path for job {JobId}: {Reason}", request.JobId, tempReason);
            throw new IOException(tempReason);
        }

        try
        {
            if (!targetInsideSource
                && !sourceInsideTarget
                && faultInjector == null
                && request.DeleteEmptySource
                && !Directory.Exists(target)
                && !Directory.Exists(tempName)
                && recoveryStage == null)
            {
                var atomicMarkerPath = GetRecoveryMarkerPath(source, request.JobId);
                WriteRecoveryMarker(source, request.JobId, AtomicRenameCompletedStage);
                var renamed = false;
                try
                {
                    Directory.Move(source, target);
                    renamed = true;
                }
                catch (IOException)
                {
                    // Cross-device and unsupported atomic renames use the verified copy path.
                    try
                    {
                        File.Delete(atomicMarkerPath);
                    }
                    catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
                    {
                        throw new MoveNeedsAttentionException(
                            $"Atomic rename failed and its recovery marker could not be removed: {exception.Message}");
                    }
                }

                if (renamed)
                {
                    await UpdateJobPhaseAsync(request.JobId, request.LeaseToken, MoveJobPhase.Finalizing, cancellationToken);
                    return new AudiobookContentMoveResult(
                        source,
                        target,
                        false,
                        false,
                        recoveryMarkerPath,
                        SourceCleanupCompleted: true);
                }
            }

            var manifest = await LoadOrCreateManifestAsync(
                request.JobId,
                request.LeaseToken,
                source,
                target,
                targetInsideSource,
                sourceSemantics,
                cancellationToken);
            ValidateTargetManifest(target, manifest, targetSemantics);
            await UpdateJobPhaseAsync(request.JobId, request.LeaseToken, MoveJobPhase.Planned, cancellationToken);

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

            await UpdateJobPhaseAsync(request.JobId, request.LeaseToken, MoveJobPhase.Copying, cancellationToken);
            await CopySourceContentsAsync(
                source,
                copyDestination,
                manifest,
                request.JobId,
                sourceSemantics,
                targetSemantics,
                cancellationToken);

            await VerifyPublishedManifestAsync(copyDestination, manifest, targetSemantics, cancellationToken);
            await UpdateCopyStateAsync(request.JobId, request.LeaseToken, cancellationToken);

            WriteRecoveryMarker(copyDestination, request.JobId, CopyCompletedStage);

            if (useTemp)
            {
                Directory.Move(tempName, target);
            }

            await UpdateJobPhaseAsync(request.JobId, request.LeaseToken, MoveJobPhase.Published, cancellationToken);

            if (faultInjector != null)
            {
                await faultInjector.AfterPublishedAsync(request.JobId, cancellationToken);
            }

            await UpdateJobPhaseAsync(request.JobId, request.LeaseToken, MoveJobPhase.CleaningSource, cancellationToken);
            await DeleteOriginalSourceAsync(
                source,
                target,
                targetInsideSource,
                request.DeleteEmptySource,
                request.JobId,
                request.LeaseToken,
                manifest,
                sourceSemantics,
                targetSemantics,
                cancellationToken);
            await UpdateJobPhaseAsync(request.JobId, request.LeaseToken, MoveJobPhase.Finalizing, cancellationToken);
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
        var sourceSemantics = request.SourceSemantics;
        var targetSemantics = request.TargetSemantics;
        var manifest = LoadManifest(request.JobId);
        var atomicRenameCompleted = manifest.Count == 0
            && string.Equals(recoveryStage, AtomicRenameCompletedStage, StringComparison.Ordinal);
        if (IsFilesystemRoot(source, sourceSemantics)
            || IsFilesystemRoot(target, targetSemantics)
            || FileSystemPathIdentity.AreEquivalent(source, target, sourceSemantics)
            || !Directory.Exists(target)
            || (!atomicRenameCompleted && manifest.Count == 0)
            || (!atomicRenameCompleted
                && recoveryStage is not (CopyCompletedStage or SourceCleanupCompletedStage)))
        {
            result = null!;
            return false;
        }

        try
        {
            if (!atomicRenameCompleted)
            {
                VerifyPublishedManifestAsync(target, manifest, targetSemantics, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            logger.LogWarning(exception, "Rejected unverifiable recovery marker for move job {JobId}", request.JobId);
            result = null!;
            return false;
        }

        var targetInsideSource = IsSameOrInside(target, source, sourceSemantics);
        var sourceInsideTarget = IsSameOrInside(source, target, targetSemantics);
        result = new AudiobookContentMoveResult(
            source,
            target,
            targetInsideSource,
            sourceInsideTarget,
            recoveryMarkerPath,
            atomicRenameCompleted
                || string.Equals(recoveryStage, SourceCleanupCompletedStage, StringComparison.Ordinal));
        return true;
    }

    public async Task<AudiobookContentMoveResult> ResumeSourceCleanupAsync(
        AudiobookContentMoveRequest request,
        AudiobookContentMoveResult result,
        CancellationToken cancellationToken)
    {
        if (result.SourceCleanupCompleted)
        {
            return result;
        }

        var manifest = LoadManifest(request.JobId);
        if (manifest.Count == 0)
        {
            throw new MoveNeedsAttentionException(
                "Source cleanup is blocked because no persisted move manifest is available.");
        }

        await EnsureLeaseOwnedAsync(request.JobId, request.LeaseToken, cancellationToken);

        await DeleteOriginalSourceAsync(
            result.Source,
            result.Target,
            result.TargetInsideSource,
            request.DeleteEmptySource,
            request.JobId,
            request.LeaseToken,
            manifest,
            request.SourceSemantics,
            request.TargetSemantics,
            cancellationToken);
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

    public bool IsSourceCleanupComplete(
        string? sourcePath,
        string targetPath,
        FileSystemPathSemantics? resolvedSemantics = null)
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
        var semantics = resolvedSemantics ?? throw new InvalidOperationException("Filesystem semantics are required for source cleanup checks.");
        if (!IsSameOrInside(target, source, semantics))
        {
            return !Directory.EnumerateFileSystemEntries(source).Any();
        }

        return Directory
            .EnumerateFileSystemEntries(source, "*", SearchOption.AllDirectories)
            .All(entry => IsSameOrInside(entry, target, semantics) || IsSameOrInside(target, entry, semantics));
    }

    private static void EnsureTargetCanReceiveContents(
        string source,
        string target,
        bool sourceInsideTarget,
        bool resumingOwnedDirectCopy,
        FileSystemPathSemantics semantics)
    {
        if (!Directory.Exists(target) || resumingOwnedDirectCopy)
        {
            return;
        }

        // When moving a child folder back into its parent, the target necessarily contains
        // the source subtree. That subtree is not a collision because it is the content being moved.
        var targetHasBlockingContent = Directory
            .EnumerateFileSystemEntries(target)
            .Any(entry => !(sourceInsideTarget && IsTargetEntryAllowedBySourceSubtree(entry, source, semantics)));
        if (targetHasBlockingContent)
        {
            throw new IOException(sourceInsideTarget
                ? "Destination contains unrelated content outside the source subtree"
                : "Target directory already exists and contains files");
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

    private static bool IsTargetEntryAllowedBySourceSubtree(
        string entry,
        string source,
        FileSystemPathSemantics semantics)
    {
        if (IsSameOrInside(entry, source, semantics))
        {
            return true;
        }

        if (!Directory.Exists(entry) || !IsSameOrInside(source, entry, semantics))
        {
            return false;
        }

        return Directory
            .EnumerateFileSystemEntries(entry, "*", SearchOption.AllDirectories)
            .All(child => IsSameOrInside(child, source, semantics) || IsSameOrInside(source, child, semantics));
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

    private static bool IsSameOrInside(
        string candidate,
        string root,
        FileSystemPathSemantics semantics)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var normalizedCandidate = Path.GetFullPath(candidate);
        var normalizedRoot = Path.GetFullPath(root);

        return FileSystemPathIdentity.IsSameOrInside(
            normalizedCandidate,
            normalizedRoot,
            semantics);
    }

    private static bool IsFilesystemRoot(
        string path,
        FileSystemPathSemantics? resolvedSemantics = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return !string.IsNullOrWhiteSpace(root)
            && FileSystemPathIdentity.AreEquivalent(
                fullPath,
                root,
                resolvedSemantics ?? throw new InvalidOperationException("Filesystem semantics are required for filesystem root checks."));
    }
}
