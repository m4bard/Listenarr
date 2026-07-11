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

        private async Task<bool> TryCompleteFinalizedMoveAsync(
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
                || string.IsNullOrWhiteSpace(audiobook.BasePath)
                || FileSystemPathIdentity.AreEquivalent(
                    source,
                    target,
                    sourceSemantics.Value)
                || !FileSystemPathIdentity.AreEquivalent(
                    Path.GetFullPath(audiobook.BasePath)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    targetSemantics)
                || !contentMoveService.IsSourceCleanupComplete(
                    source,
                    target,
                    targetSemantics))
            {
                return false;
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
                return true;
            }

            await UpdateJobStatusAsync(
                job,
                MoveJobStatus.Completed,
                cancellationToken: cancellationToken);
            metrics.Increment("worker.move.job.completed");
            logger.LogInformation(
                "Move job {JobId} completed after markerless manifest verification",
                job.Id);
            return true;
        }

        private void CleanupCompletedMoveArtifacts(
            AudiobookContentMoveService contentMoveService,
            AudiobookContentMoveRequest request,
            AudiobookContentMoveResult result,
            Guid jobId)
        {
            try
            {
                contentMoveService.CleanupCompletedMoveArtifacts(request, result);
            }
            catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
            {
                logger.LogWarning(
                    exception,
                    "Move job {JobId} completed, but owned recovery artifacts could not be removed",
                    jobId);
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
