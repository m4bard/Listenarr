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

namespace Listenarr.Infrastructure.Library.Moving
{
    internal partial class MoveJobProcessor
    {
        private static MoveLeaseToken CreateLeaseToken(MoveJob job)
        {
            if (string.IsNullOrWhiteSpace(job.LeaseOwner) || job.LeaseGeneration <= 0)
            {
                throw new MoveLeaseLostException(job.Id, job.LeaseGeneration);
            }

            return new MoveLeaseToken(job.LeaseOwner, job.LeaseGeneration);
        }

        private Task UpdateJobStatusAsync(
            MoveJob job,
            MoveJobStatus status,
            string? error = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(job.LeaseOwner))
            {
                throw new MoveLeaseLostException(job.Id, job.LeaseGeneration);
            }

            return moveQueueService.UpdateJobStatusAsync(
                job.Id,
                job.LeaseOwner,
                job.LeaseGeneration,
                status,
                error,
                cancellationToken);
        }

        private async Task<FinalizedMoveRecoveryOutcome> TryRecoverFinalizedMoveAsync(
            MoveJob job,
            Audiobook audiobook,
            string? source,
            string target,
            FileSystemPathSemantics? sourceSemantics,
            FileSystemPathSemantics targetSemantics,
            MoveCleanupBoundaryResolution? cleanupBoundaryResolution,
            AudiobookContentMoveService contentMoveService,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(source)
                || !sourceSemantics.HasValue
                || FileSystemPathIdentity.AreEquivalent(
                    source,
                    target,
                    sourceSemantics.Value)
                || !HasFinalizedMoveEvidence(job, audiobook, target, targetSemantics)
                || !AudiobookContentMoveService.CanAttemptFinalizedMoveVerification(
                    source,
                    target,
                    sourceSemantics.Value))
            {
                return FinalizedMoveRecoveryOutcome.NotAttempted;
            }

            var finalizedRequest = new AudiobookContentMoveRequest(
                source,
                target,
                job.Id,
                job.DeleteEmptySource,
                sourceSemantics.Value,
                targetSemantics,
                CreateLeaseToken(job),
                cleanupBoundaryResolution?.Boundary);
            try
            {
                await contentMoveService.VerifyFinalizedMoveAsync(
                    finalizedRequest,
                    cancellationToken);
            }
            catch (MoveNeedsAttentionException exception)
            {
                await UpdateJobStatusAsync(
                    job,
                    MoveJobStatus.NeedsAttention,
                    exception.Message,
                    cancellationToken);
                metrics.Increment("worker.move.job.needs_attention");
                logger.LogWarning(
                    exception,
                    "Move job {JobId} could not prove markerless completion",
                    job.Id);
                return FinalizedMoveRecoveryOutcome.HandledFailure;
            }

            var targetInsideSource = FileSystemPathIdentity.IsSameOrInside(
                target,
                source,
                sourceSemantics.Value);
            var sourceInsideTarget = FileSystemPathIdentity.IsSameOrInside(
                source,
                target,
                targetSemantics);
            return new FinalizedMoveRecoveryOutcome(
                Handled: false,
                new AudiobookContentMoveResult(
                    source,
                    target,
                    targetInsideSource,
                    sourceInsideTarget,
                    Path.Join(target, $".listenarr-move-{job.Id:N}.pending"),
                    SourceCleanupCompleted: true));
        }

        private static bool HasFinalizedMoveEvidence(
            MoveJob job,
            Audiobook audiobook,
            string target,
            FileSystemPathSemantics targetSemantics)
        {
            if (job.Phase >= MoveJobPhase.Published)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(audiobook.BasePath)
                && FileSystemPathIdentity.AreEquivalent(
                    Path.GetFullPath(audiobook.BasePath)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    targetSemantics);
        }

        private sealed record FinalizedMoveRecoveryOutcome(
            bool Handled,
            AudiobookContentMoveResult? MoveResult)
        {
            public static FinalizedMoveRecoveryOutcome NotAttempted { get; } = new(false, null);
            public static FinalizedMoveRecoveryOutcome HandledFailure { get; } = new(true, null);
        }

        private async Task<bool> TryFinalizeMoveAsync(
            MoveJob job,
            AudiobookContentMoveService contentMoveService,
            AudiobookContentMoveRequest request,
            AudiobookContentMoveResult result,
            CancellationToken cancellationToken)
        {
            try
            {
                await contentMoveService.FinalizeMoveAsync(
                    request,
                    result,
                    cancellationToken);
                return true;
            }
            catch (Exception exception) when (exception is
                MoveNeedsAttentionException or MoveLeaseLostException or PersistenceException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await moveQueueService.IncrementAttemptAsync(
                    job.Id,
                    job.LeaseOwner!,
                    job.LeaseGeneration,
                    cancellationToken);
                await UpdateJobStatusAsync(
                    job,
                    MoveJobStatus.RetryScheduled,
                    $"Move finalization will be retried: {exception.Message}",
                    cancellationToken);
                metrics.Increment("worker.move.job.retry_scheduled");
                logger.LogWarning(
                    exception,
                    "Move job {JobId} could not finish source-boundary finalization and was scheduled for retry",
                    job.Id);
                return false;
            }
        }

        private async Task<bool> TryCleanupCompletedMoveArtifactsAsync(
            MoveJob job,
            AudiobookContentMoveService contentMoveService,
            AudiobookContentMoveRequest request,
            AudiobookContentMoveResult result,
            CancellationToken cancellationToken)
        {
            try
            {
                await contentMoveService.CleanupCompletedMoveArtifactsAsync(
                    request,
                    result,
                    cancellationToken);
                return true;
            }
            catch (Exception exception) when (exception is
                MoveNeedsAttentionException or MoveLeaseLostException or PersistenceException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await moveQueueService.IncrementAttemptAsync(
                    job.Id,
                    job.LeaseOwner!,
                    job.LeaseGeneration,
                    cancellationToken);
                await UpdateJobStatusAsync(
                    job,
                    MoveJobStatus.RetryScheduled,
                    $"Owned move artifact cleanup will be retried: {exception.Message}",
                    cancellationToken);
                metrics.Increment("worker.move.job.retry_scheduled");
                logger.LogWarning(
                    exception,
                    "Move job {JobId} could not remove its owned recovery artifacts and was scheduled for retry",
                    job.Id);
                return false;
            }
        }

        private static bool IsFilesystemRoot(string path, FileSystemPathSemantics semantics)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            return !string.IsNullOrWhiteSpace(root)
                && FileSystemPathIdentity.AreEquivalent(fullPath, root, semantics);
        }
    }
}
