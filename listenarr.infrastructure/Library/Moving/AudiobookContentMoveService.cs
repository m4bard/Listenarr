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
    MoveLeaseToken LeaseToken,
    string? SourceCleanupBoundary = null)
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

internal enum RecoveryMarkerWriteFaultPoint
{
    BeforeTemporaryFileCreation,
    DuringJsonWrite,
    DuringFlush,
    AfterTemporaryFileWritten,
    BeforePublication,
    BeforeTemporaryFileDeletion
}

internal interface IMoveFaultInjector
{
    Task AfterPublishedAsync(Guid jobId, CancellationToken cancellationToken) => Task.CompletedTask;

    void OnRecoveryMarkerWrite(Guid jobId, RecoveryMarkerWriteFaultPoint faultPoint)
    {
    }
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
        await ValidatePersistedMoveIdentityAsync(
            request.JobId,
            source,
            target,
            sourceSemantics,
            targetSemantics,
            request.LeaseToken,
            cancellationToken);
        var targetInsideSource = IsSameOrInside(target, source, sourceSemantics);
        var sourceInsideTarget = IsSameOrInside(source, target, targetSemantics);

        var targetParent = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(targetParent))
        {
            throw new IOException("Invalid target path");
        }

        if (!Directory.Exists(targetParent)) Directory.CreateDirectory(targetParent);

        DeleteOwnedRecoveryMarkerWriteFiles(source, request, source, target);
        DeleteOwnedRecoveryMarkerWriteFiles(target, request, source, target);

        var recoveryMarkerPath = GetRecoveryMarkerPath(target, request.JobId);
        var recoveryMarker = ReadRecoveryMarker(recoveryMarkerPath);
        ValidateRecoveryMarker(recoveryMarker, request, source, target);
        var recoveryStage = recoveryMarker?.Stage;
        var persistedManifest = await LoadManifestAsync(
            request.JobId,
            cancellationToken);
        if (recoveryMarker != null
            && persistedManifest.Count == 0
            && !string.Equals(recoveryStage, AtomicRenameCompletedStage, StringComparison.Ordinal))
        {
            throw new MoveNeedsAttentionException(
                "A move recovery marker exists without a persisted manifest; destination ownership cannot be proven.");
        }

        var resumingDirectCopy = recoveryStage == CopyStartedStage && persistedManifest.Count > 0;
        EnsureTargetCanReceiveContents(source, target, sourceInsideTarget, resumingDirectCopy, targetSemantics);
        var validatedSourceEntries = ValidateSourceTreeForMove(
            source,
            target,
            targetInsideSource,
            sourceSemantics,
            cancellationToken);

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
                && !IsSourceCleanupBoundary(source, request.SourceCleanupBoundary, sourceSemantics)
                && !Directory.Exists(target)
                && !Directory.Exists(tempName)
                && recoveryStage == null)
            {
                await EnsureLeaseOwnedAsync(request.JobId, request.LeaseToken, cancellationToken);
                await ValidatePersistedMoveIdentityAsync(
                    request.JobId,
                    source,
                    target,
                    sourceSemantics,
                    targetSemantics,
                    request.LeaseToken,
                    cancellationToken);
                if (!Directory.Exists(source)
                    || (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0
                    || Directory.Exists(target))
                {
                    throw new MoveNeedsAttentionException(
                        "Atomic rename preconditions changed after validation; no filesystem mutation was performed.");
                }

                var atomicMarkerPath = GetRecoveryMarkerPath(source, request.JobId);
                WriteRecoveryMarker(
                    source,
                    request.JobId,
                    source,
                    target,
                    AtomicRenameCompletedStage);
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

            var manifest = persistedManifest.Count > 0
                ? persistedManifest
                : await LoadOrCreateManifestAsync(
                    request.JobId,
                    request.LeaseToken,
                    validatedSourceEntries,
                    cancellationToken);
            ValidateTargetManifest(target, manifest, targetSemantics);
            await UpdateJobPhaseAsync(request.JobId, request.LeaseToken, MoveJobPhase.Planned, cancellationToken);

            // The move operation relocates the contents of the audiobook BasePath, not the
            // BasePath directory itself. Child destinations must copy directly and skip their
            // own subtree to avoid recursively copying the destination into itself.
            var useTemp = !targetInsideSource && !Directory.Exists(target);
            var copyDestination = useTemp ? tempName : target;
            var tempOwnership = useTemp
                ? CreateOrValidateOwnedTempDirectory(
                    tempName,
                    targetParent,
                    request,
                    source,
                    target)
                : null;

            if (!Directory.Exists(copyDestination)) Directory.CreateDirectory(copyDestination);
            if (!useTemp && !resumingDirectCopy)
            {
                WriteRecoveryMarker(
                    copyDestination,
                    request.JobId,
                    source,
                    target,
                    CopyStartedStage);
            }

            await UpdateJobPhaseAsync(request.JobId, request.LeaseToken, MoveJobPhase.Copying, cancellationToken);
            await CopySourceContentsAsync(
                source,
                copyDestination,
                manifest,
                request.JobId,
                sourceSemantics,
                targetSemantics,
                tempOwnership,
                cancellationToken);

            await VerifyPublishedManifestAsync(copyDestination, manifest, targetSemantics, cancellationToken);
            await UpdateCopyStateAsync(request.JobId, request.LeaseToken, cancellationToken);

            WriteRecoveryMarker(
                copyDestination,
                request.JobId,
                source,
                target,
                CopyCompletedStage);

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
                request.SourceCleanupBoundary,
                cancellationToken);
            await UpdateJobPhaseAsync(request.JobId, request.LeaseToken, MoveJobPhase.Finalizing, cancellationToken);
            WriteRecoveryMarker(
                target,
                request.JobId,
                source,
                target,
                SourceCleanupCompletedStage);

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
            TryDeleteOwnedTempDirectory(
                tempName,
                targetParent,
                request,
                source,
                target);
            throw;
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

        if (!FileSystemSafety.TryEnumerateTreeWithoutLinks(
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
