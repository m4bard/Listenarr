/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
using Listenarr.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace Listenarr.Infrastructure.Library.Moving
{
    internal partial class MoveJobProcessor(
        IMoveQueueService moveQueueService,
        IToastService toastService,
        IScanQueueService scanQueueService,
        ILogger<MoveJobProcessor> logger,
        AudiobookContentMoveService contentMoveService,
        IServiceScopeFactory scopeFactory,
        IHubContext<DownloadHub> hubContext,
        IAppMetricsService metrics,
        IFileSystemSemanticsResolver semanticsResolver,
        IMoveCleanupBoundaryResolver cleanupBoundaryResolver,
        TimeProvider timeProvider) : IMoveJobProcessor
    {
        public async Task ProcessJobAsync(MoveJob job, CancellationToken stoppingToken)
        {
            using var logScope = logger.BeginScope(new Dictionary<string, object?> { ["JobId"] = job.Id, ["AudiobookId"] = job.AudiobookId });
            metrics.Increment("worker.move.job.started");
            stoppingToken.ThrowIfCancellationRequested();
            try
            {
                logger.LogInformation("Processing move job {JobId} for audiobook {AudiobookId} to {Path}", job.Id, job.AudiobookId, LogRedaction.SanitizeFilePath(job.RequestedPath));
                await UpdateJobStatusAsync(job, MoveJobStatus.Running, cancellationToken: stoppingToken);

                using var scope = scopeFactory.CreateScope();
                var audiobookRepository = scope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
                var rootFolderRepository = scope.ServiceProvider.GetRequiredService<IRootFolderRepository>();
                var audiobook = await audiobookRepository.GetByIdAsync(job.AudiobookId);
                if (audiobook == null)
                {
                    await UpdateJobStatusAsync(job, MoveJobStatus.Failed, "Audiobook not found", stoppingToken);
                    metrics.Increment("worker.move.job.failed");
                    return;
                }

                var rootFolders = await rootFolderRepository.GetAllAsync();
                var requested = job.RequestedPath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(requested))
                {
                    await UpdateJobStatusAsync(job, MoveJobStatus.Failed, "Target path not provided", stoppingToken);
                    metrics.Increment("worker.move.job.failed");
                    return;
                }
                var target = Path.GetFullPath(requested);
                var targetResolution = await semanticsResolver.ResolveAsync(
                    target,
                    cancellationToken: stoppingToken);
                if (targetResolution.State != PathIdentityState.Valid)
                {
                    await UpdateJobStatusAsync(
                        job,
                        MoveJobStatus.NeedsAttention,
                        targetResolution.Reason ?? "Target filesystem identity is unavailable.",
                        stoppingToken);
                    metrics.Increment("worker.move.job.needs_attention");
                    return;
                }

                var hasPersistedSource = !string.IsNullOrWhiteSpace(job.SourcePath);
                var source = job.SourcePath;
                AudiobookContentMoveResult? recoveredMove = null;
                MoveCleanupBoundaryResolution? cleanupBoundaryResolution = null;
                FileSystemPathSemantics? recoverySourceSemantics = null;
                if (!string.IsNullOrWhiteSpace(source))
                {
                    source = Path.GetFullPath(source);
                    var recoverySourceResolution = await semanticsResolver.ResolveAsync(
                        source,
                        cancellationToken: stoppingToken);
                    if (recoverySourceResolution.State != PathIdentityState.Valid)
                    {
                        await UpdateJobStatusAsync(
                            job,
                            MoveJobStatus.NeedsAttention,
                            recoverySourceResolution.Reason ?? "Source filesystem identity is unavailable.",
                            stoppingToken);
                        metrics.Increment("worker.move.job.needs_attention");
                        return;
                    }

                    recoverySourceSemantics = recoverySourceResolution.Semantics;
                    cleanupBoundaryResolution = await cleanupBoundaryResolver.ResolveAsync(
                        source,
                        target,
                        rootFolders,
                        job.SourceCleanupBoundary,
                        stoppingToken);
                    var recoveryRequest = new AudiobookContentMoveRequest(
                        source,
                        target,
                        job.Id,
                        job.DeleteEmptySource,
                        recoverySourceResolution.Semantics,
                        targetResolution.Semantics,
                        CreateLeaseToken(job),
                        cleanupBoundaryResolution.Boundary);
                    try
                    {
                        var resumedMove = await contentMoveService.GetRecoverableMoveAsync(
                            recoveryRequest,
                            stoppingToken);
                        if (resumedMove != null)
                        {
                            recoveredMove = resumedMove;
                            logger.LogInformation(
                                "Resuming move job {JobId} after its filesystem phase completed",
                                job.Id);
                        }
                        else if (!Directory.Exists(source))
                        {
                            logger.LogWarning(
                                "Persisted source path {Source} for job {JobId} does not exist",
                                LogRedaction.SanitizeFilePath(source),
                                job.Id);
                        }
                    }
                    catch (MoveNeedsAttentionException exception)
                    {
                        await UpdateJobStatusAsync(
                            job,
                            MoveJobStatus.NeedsAttention,
                            exception.Message,
                            stoppingToken);
                        metrics.Increment("worker.move.job.needs_attention");
                        logger.LogWarning(
                            exception,
                            "Move job {JobId} has ambiguous or invalid recovery artifacts",
                            job.Id);
                        return;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        await ScheduleTransientRetryAsync(
                            job,
                            $"Move recovery verification will be retried: {exception.Message}",
                            exception,
                            "Move job {JobId} could not verify its recovery target",
                            stoppingToken);
                        return;
                    }
                }

                if (recoveredMove == null)
                {
                    var finalizedRecovery = await TryRecoverFinalizedMoveAsync(
                        job,
                        audiobook,
                        source,
                        target,
                        recoverySourceSemantics,
                        targetResolution.Semantics,
                        cleanupBoundaryResolution,
                        contentMoveService,
                        stoppingToken);
                    if (finalizedRecovery.Handled)
                    {
                        return;
                    }

                    recoveredMove = finalizedRecovery.MoveResult;
                }

                if (string.IsNullOrWhiteSpace(source))
                {
                    source = audiobook.BasePath;
                }

                if (hasPersistedSource
                    && recoveredMove == null
                    && !Directory.Exists(source!)
                    && !string.IsNullOrWhiteSpace(audiobook.BasePath)
                    && Directory.Exists(audiobook.BasePath))
                {
                    await UpdateJobStatusAsync(
                        job,
                        MoveJobStatus.NeedsAttention,
                        "Persisted source path does not exist and cannot be recovered",
                        stoppingToken);
                    metrics.Increment("worker.move.job.needs_attention");
                    return;
                }

                if (recoveredMove == null && (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source)))
                {
                    await UpdateJobStatusAsync(job, MoveJobStatus.Failed, "Source path invalid or does not exist", stoppingToken);
                    metrics.Increment("worker.move.job.failed");
                    return;
                }

                source = recoveredMove?.Source ?? Path.GetFullPath(source!);
                var sourceResolution = await semanticsResolver.ResolveAsync(
                    source,
                    cancellationToken: stoppingToken);
                if (sourceResolution.State != PathIdentityState.Valid)
                {
                    await UpdateJobStatusAsync(
                        job,
                        MoveJobStatus.NeedsAttention,
                        sourceResolution.Reason ?? "Source filesystem identity is unavailable.",
                        stoppingToken);
                    metrics.Increment("worker.move.job.needs_attention");
                    return;
                }

                cleanupBoundaryResolution ??= await cleanupBoundaryResolver.ResolveAsync(
                    source,
                    target,
                    rootFolders,
                    job.SourceCleanupBoundary,
                    stoppingToken);
                if (job.DeleteEmptySource)
                {
                    if (cleanupBoundaryResolution.IsAvailable)
                    {
                        logger.LogInformation(
                            "Using {BoundaryKind} source cleanup boundary {Boundary} for move job {JobId}",
                            cleanupBoundaryResolution.Kind,
                            LogRedaction.SanitizeFilePath(cleanupBoundaryResolution.Boundary),
                            job.Id);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Move job {JobId} has no safe source cleanup boundary: {Reason}",
                            job.Id,
                            cleanupBoundaryResolution.Reason ?? "boundary unavailable");
                    }
                }

                if (IsFilesystemRoot(source, sourceResolution.Semantics)
                    || IsFilesystemRoot(target, targetResolution.Semantics))
                {
                    await UpdateJobStatusAsync(job, MoveJobStatus.Failed, "Refused to move a filesystem root", stoppingToken);
                    metrics.Increment("worker.move.job.failed");
                    logger.LogWarning(
                        "Blocked move job {JobId}: source or target is a filesystem root. Source={Source}, Target={Target}",
                        job.Id,
                        LogRedaction.SanitizeFilePath(source),
                        LogRedaction.SanitizeFilePath(target));
                    return;
                }

                // If source == target, nothing to do. Match the host filesystem so
                // case-only moves remain valid on Linux/macOS but no-op on Windows.
                if (recoveredMove == null
                    && FileSystemPathIdentity.AreEquivalent(
                        source.TrimEnd(Path.DirectorySeparatorChar),
                        target.TrimEnd(Path.DirectorySeparatorChar),
                        sourceResolution.Semantics))
                {
                    await UpdateJobStatusAsync(job, MoveJobStatus.Completed, cancellationToken: stoppingToken);
                    metrics.Increment("worker.move.job.skipped");
                    return;
                }

                try
                {
                    var moveRequest = new AudiobookContentMoveRequest(
                        source,
                        target,
                        job.Id,
                        job.DeleteEmptySource,
                        sourceResolution.Semantics,
                        targetResolution.Semantics,
                        CreateLeaseToken(job),
                        cleanupBoundaryResolution.Boundary);
                    var moveResult = recoveredMove ?? await contentMoveService.MoveContentsAsync(
                        moveRequest,
                        stoppingToken);
                    moveResult = await contentMoveService.ResumeSourceCleanupAsync(
                        moveRequest,
                        moveResult,
                        stoppingToken);
                    source = moveResult.Source;
                    target = moveResult.Target;

                    using (var rewriteScope = scopeFactory.CreateScope())
                    {
                        var rewriteRepository = rewriteScope.ServiceProvider
                            .GetRequiredService<IAudiobookRepository>();
                        await MovedAudiobookPathRewriter.RewriteAsync(
                            audiobook.Id,
                            source,
                            target,
                            moveRequest.SourceSemantics,
                            moveRequest.TargetSemantics,
                            rewriteRepository,
                            logger,
                            stoppingToken);
                    }

                    using var currentAudiobookScope = scopeFactory.CreateScope();
                    var currentAudiobookRepository = currentAudiobookScope.ServiceProvider
                        .GetRequiredService<IAudiobookRepository>();
                    audiobook = await currentAudiobookRepository.GetByIdAsync(audiobook.Id)
                        ?? throw new MoveNeedsAttentionException(
                            "The audiobook disappeared after its moved path references were persisted.");
                    if (!await TryFinalizeMoveAsync(
                            job,
                            contentMoveService,
                            moveRequest,
                            moveResult,
                            stoppingToken))
                    {
                        return;
                    }

                    if (!await TryCleanupCompletedMoveArtifactsAsync(
                            job,
                            contentMoveService,
                            moveRequest,
                            moveResult,
                            stoppingToken))
                    {
                        return;
                    }

                    await contentMoveService.MarkCompletionRecordingAsync(
                        moveRequest,
                        stoppingToken);

                    if (!await TryRecordMoveCompletionAsync(
                            job,
                            audiobook,
                            source,
                            target,
                            currentAudiobookRepository,
                            scope.ServiceProvider,
                            contentMoveService,
                            moveRequest,
                            stoppingToken))
                    {
                        return;
                    }

                    await UpdateJobStatusAsync(job, MoveJobStatus.Completed, cancellationToken: stoppingToken);
                    metrics.Increment("worker.move.job.completed");
                    logger.LogInformation("Move job {JobId} completed: {Source} -> {Target}", job.Id, LogRedaction.SanitizeFilePath(source), LogRedaction.SanitizeFilePath(target));
                    // Completed move job — status updated and broadcasted where configured
                }
                catch (Exception ex) when (ex is PersistenceException or MoveLeaseLostException)
                {
                    throw;
                }
                catch (MoveNeedsAttentionException ex)
                {
                    await UpdateJobStatusAsync(
                        job,
                        MoveJobStatus.NeedsAttention,
                        ex.Message,
                        stoppingToken);
                    metrics.Increment("worker.move.job.needs_attention");
                    logger.LogWarning(ex, "Move job {JobId} requires operator attention", job.Id);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    await ScheduleTransientRetryAsync(
                        job,
                        $"Move filesystem work will be retried: {ex.Message}",
                        ex,
                        "Move job {JobId} encountered a transient filesystem failure",
                        stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    // Increment attempt count for the job on failure
                    await moveQueueService.IncrementAttemptAsync(
                        job.Id,
                        job.LeaseOwner!,
                        job.LeaseGeneration,
                        stoppingToken);

                    // Record failure in history and send a toast notification
                    try
                    {
                        var historyEntry = new History
                        {
                            AudiobookId = audiobook.Id,
                            AudiobookTitle = audiobook.Title,
                            EventType = "MoveFailed",
                            Message = $"Move failed: {ex.Message}",
                            Source = "Move",
                            Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                            NotificationSent = false,
                            Data = System.Text.Json.JsonSerializer.Serialize(new { JobId = job.Id, Error = ex.Message })
                        };

                        var historyRepository = scope.ServiceProvider.GetRequiredService<IHistoryRepository>();
                        await historyRepository.AddAsync(historyEntry);
                        logger.LogInformation("Added history entry for failed move job {JobId}", job.Id);

                        try
                        {
                            var message = !string.IsNullOrEmpty(audiobook.Title)
                                ? $"Failed to move {audiobook.Title}: {ex.Message}"
                                : $"Move failed: {ex.Message}";

                            await toastService.PublishToastAsync("error", "Move Failed", message, timeoutMs: 15000);
                            logger.LogDebug("Sent toast notification for failed move job {JobId}", job.Id);
                        }
                        catch (Exception toastEx) when (toastEx is not OperationCanceledException && toastEx is not OutOfMemoryException && toastEx is not StackOverflowException)
                        {
                            logger.LogDebug(toastEx, "Failed to send toast notification for failed move job {JobId}", job.Id);
                        }
                    }
                    catch (Exception historyEx) when (historyEx is not OperationCanceledException && historyEx is not OutOfMemoryException && historyEx is not StackOverflowException)
                    {
                        logger.LogWarning(historyEx, "Failed to add history entry for failed move job {JobId}", job.Id);
                    }

                    await UpdateJobStatusAsync(job, MoveJobStatus.Failed, ex.Message, stoppingToken);
                    metrics.Increment("worker.move.job.failed");
                    logger.LogError(ex, "Move job {JobId} failed", job.Id);
                    // Failure during move job — attempt counts updated and history recorded where configured
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                logger.LogWarning(ex, "Move job {JobId} canceled/timed out", job.Id);
            }
            catch (Exception ex) when (ex is PersistenceException or MoveLeaseLostException)
            {
                logger.LogWarning(ex, "Move job {JobId} stopped because durable coordination failed", job.Id);
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogError(ex, "Unexpected error processing move job {JobId}", job.Id);
                await UpdateJobStatusAsync(job, MoveJobStatus.Failed, ex.Message, stoppingToken);
                metrics.Increment("worker.move.job.failed");
            }
        }

    }
}
