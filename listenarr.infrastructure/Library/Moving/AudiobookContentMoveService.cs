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

internal enum OwnershipMarkerKind
{
    TemporaryDirectory,
    QuarantineDirectory,
    CleanupTombstone
}

internal enum OwnershipMarkerWriteFaultPoint
{
    BeforeTemporaryFileCreation,
    DuringJsonWrite,
    DuringFlush,
    AfterTemporaryFileWritten,
    BeforePublication,
    BeforeTemporaryFileDeletion
}

internal enum SourceCleanupFaultPoint
{
    BeforeSourceFileMove,
    BeforeQuarantineFileDelete
}

internal enum CopyMutationFaultPoint
{
    AfterChunkWritten
}

internal enum OwnershipCleanupFaultPoint
{
    BeforeOwnershipMarkerDelete,
    BeforeDirectoryDelete,
    BeforeTombstoneDelete
}

internal enum CompletedArtifactCleanupFaultPoint
{
    BeforeRecoveryMarkerDelete
}

internal enum MoveFinalizationFaultPoint
{
    BeforeSourceAncestorDelete
}

internal interface IMoveFaultInjector
{
    Task AfterPublishedAsync(Guid jobId, CancellationToken cancellationToken) => Task.CompletedTask;

    void OnRecoveryMarkerWrite(Guid jobId, RecoveryMarkerWriteFaultPoint faultPoint)
    {
    }

    void OnOwnershipMarkerWrite(
        Guid jobId,
        OwnershipMarkerKind markerKind,
        OwnershipMarkerWriteFaultPoint faultPoint)
    {
    }

    void OnSourceCleanupMutation(
        Guid jobId,
        SourceCleanupFaultPoint faultPoint)
    {
    }

    void OnCopyMutation(
        Guid jobId,
        CopyMutationFaultPoint faultPoint)
    {
    }

    void OnOwnershipCleanup(
        Guid jobId,
        OwnershipMarkerKind markerKind,
        OwnershipCleanupFaultPoint faultPoint)
    {
    }

    void OnCompletedArtifactCleanup(
        Guid jobId,
        CompletedArtifactCleanupFaultPoint faultPoint)
    {
    }

    void OnMoveFinalization(
        Guid jobId,
        MoveFinalizationFaultPoint faultPoint)
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
        ValidateMoveSourceRoot(source);
        ValidateMoveTargetRoot(target);

        var targetInsideSource = IsSameOrInside(target, source, sourceSemantics);
        var sourceInsideTarget = IsSameOrInside(source, target, targetSemantics);

        var targetParent = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(targetParent))
        {
            throw new IOException("Invalid target path");
        }

        if (!Directory.Exists(targetParent))
        {
            await EnsureMutationAuthorizedAsync(request, source, target, cancellationToken);
            Directory.CreateDirectory(targetParent);
        }
        ValidateMoveTargetRoot(target);

        await EnsureMutationAuthorizedAsync(request, source, target, cancellationToken);
        await DeleteOwnedRecoveryMarkerWriteFilesAsync(
            source,
            request,
            source,
            target,
            cancellationToken);
        await EnsureMutationAuthorizedAsync(request, source, target, cancellationToken);
        await DeleteOwnedRecoveryMarkerWriteFilesAsync(
            target,
            request,
            source,
            target,
            cancellationToken);

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
        RejectUnownedPartialArtifacts(
            target,
            request.JobId,
            recoveryMarker?.StructuredMarker != null);
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
            var atomicResult = await TryMoveByAtomicRenameAsync(
                request,
                source,
                target,
                tempName,
                targetInsideSource,
                sourceInsideTarget,
                recoveryStage,
                sourceSemantics,
                targetSemantics,
                cancellationToken);
            if (atomicResult != null)
            {
                return atomicResult;
            }

            ValidateMoveSourceRoot(source);
            ValidateMoveTargetRoot(target);

            var manifest = persistedManifest.Count > 0
                ? persistedManifest
                : await LoadOrCreateManifestAsync(
                    request.JobId,
                    request.LeaseToken,
                    validatedSourceEntries,
                    cancellationToken);
            ValidateTargetManifest(target, manifest, targetSemantics);
            await UpdateJobPhaseAsync(
                request.JobId,
                request.LeaseToken,
                MoveJobPhase.Planned,
                cancellationToken);

            // The move operation relocates the contents of the audiobook BasePath, not the
            // BasePath directory itself. Child destinations must copy directly and skip their
            // own subtree to avoid recursively copying the destination into itself.
            var useTemp = !targetInsideSource && !Directory.Exists(target);
            var copyDestination = useTemp ? tempName : target;
            await EnsureMutationAuthorizedAsync(request, source, target, cancellationToken);
            var tempOwnership = useTemp
                ? await CreateOrValidateOwnedTempDirectoryAsync(
                    tempName,
                    targetParent,
                    request,
                    source,
                    target,
                    cancellationToken)
                : null;

            if (!Directory.Exists(copyDestination))
            {
                await EnsureMutationAuthorizedAsync(request, source, target, cancellationToken);
                Directory.CreateDirectory(copyDestination);
            }
            if (!useTemp && !resumingDirectCopy)
            {
                await EnsureMutationAuthorizedAsync(request, source, target, cancellationToken);
                await WriteRecoveryMarkerAsync(
                    copyDestination,
                    request,
                    source,
                    target,
                    CopyStartedStage,
                    cancellationToken);
            }

            await UpdateJobPhaseAsync(request.JobId, request.LeaseToken, MoveJobPhase.Copying, cancellationToken);
            await CopySourceContentsAsync(
                request,
                source,
                target,
                copyDestination,
                manifest,
                sourceSemantics,
                targetSemantics,
                tempOwnership,
                directCopyOwnershipValidated: !useTemp,
                cancellationToken);

            await VerifyPublishedManifestAsync(copyDestination, manifest, targetSemantics, cancellationToken);
            await UpdateCopyStateAsync(request.JobId, request.LeaseToken, cancellationToken);

            await EnsureMutationAuthorizedAsync(request, source, target, cancellationToken);
            await WriteRecoveryMarkerAsync(
                copyDestination,
                request,
                source,
                target,
                CopyCompletedStage,
                cancellationToken);

            if (useTemp)
            {
                await ValidateOwnedTempDirectoryAsync(
                    tempName,
                    targetParent,
                    request,
                    source,
                    target,
                    cancellationToken);
                ValidateMoveTargetRoot(target);
                if (Directory.Exists(target))
                {
                    throw new MoveNeedsAttentionException(
                        "The move target appeared before temporary publication.");
                }

                await ValidateOwnedTempDirectoryAsync(
                    tempName,
                    targetParent,
                    request,
                    source,
                    target,
                    cancellationToken);
                ValidateMoveTargetRoot(target);
                if (Directory.Exists(target))
                {
                    throw new MoveNeedsAttentionException(
                        "The move target appeared immediately before temporary publication.");
                }

                await EnsureMutationAuthorizedAsync(request, source, target, cancellationToken);
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
            VerifySourceCleanupState(request, source, target);
            await UpdateJobPhaseAsync(request.JobId, request.LeaseToken, MoveJobPhase.Finalizing, cancellationToken);
            await EnsureMutationAuthorizedAsync(request, source, target, cancellationToken);
            await WriteRecoveryMarkerAsync(
                target,
                request,
                source,
                target,
                SourceCleanupCompletedStage,
                cancellationToken);

            return new AudiobookContentMoveResult(
                source,
                target,
                targetInsideSource,
                sourceInsideTarget,
                recoveryMarkerPath,
                SourceCleanupCompleted: true);
        }
        catch (Exception exception) when (exception is MoveLeaseLostException or PersistenceException)
        {
            throw;
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            await TryDeleteOwnedTempDirectoryAsync(
                tempName,
                targetParent,
                request,
                source,
                target,
                cancellationToken);
            throw;
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
