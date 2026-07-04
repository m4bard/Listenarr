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
using System.Threading.Channels;
using Listenarr.Application.Common;
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;


namespace Listenarr.Application.Audiobooks.Jobs
{
    public class MoveQueueService : IMoveQueueService
    {
        private readonly Channel<MoveJob> _channel = Channel.CreateUnbounded<MoveJob>();
        private readonly SemaphoreSlim _enqueueGate = new(1, 1);
        private bool _identityKeysReconciled;
        private readonly ILogger<MoveQueueService> _logger;
        private readonly IMoveQueuePersistence _persistence;
        private readonly IHubBroadcaster _hubBroadcaster;
        private readonly TimeProvider _timeProvider;
        private readonly IRootFolderRelocationService? _relocationService;
        private readonly IFileSystemSemanticsResolver? _semanticsResolver;

        public MoveQueueService(
            ILogger<MoveQueueService> logger,
            IMoveQueuePersistence persistence,
            IHubBroadcaster hubBroadcaster,
            TimeProvider timeProvider,
            IRootFolderRelocationService? relocationService = null,
            IFileSystemSemanticsResolver? semanticsResolver = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _hubBroadcaster = hubBroadcaster ?? throw new ArgumentNullException(nameof(hubBroadcaster));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _relocationService = relocationService;
            _semanticsResolver = semanticsResolver;
        }

        public ChannelReader<MoveJob> Reader => _channel.Reader;

        public async Task<Guid> EnqueueMoveAsync(
            int audiobookId,
            string requestedPath,
            string? sourcePath = null,
            bool deleteEmptySource = true)
        {
            var deduplicationKey = await BuildDeduplicationKeyAsync(audiobookId, requestedPath);
            await _enqueueGate.WaitAsync();
            try
            {
                var existingDb = await _persistence.GetActiveByKeyAsync(deduplicationKey);

                if (existingDb != null)
                {
                    await ScheduleAsync(existingDb);
                    _logger.LogInformation("Found active move job {JobId} for audiobook {AudiobookId} to {Path}; deduping and returning existing job id", existingDb.Id, audiobookId, LogRedaction.SanitizeFilePath(requestedPath));
                    return existingDb.Id;
                }

                var job = new MoveJob
                {
                    AudiobookId = audiobookId,
                    RequestedPath = requestedPath,
                    ActiveDeduplicationKey = deduplicationKey,
                    EnqueuedAt = _timeProvider.GetUtcNow().UtcDateTime,
                    Status = MoveJobStatus.Queued,
                    SourcePath = sourcePath,
                    DeleteEmptySource = deleteEmptySource
                };

                try
                {
                    await _persistence.AddAsync(job);
                }
                catch (UniqueConstraintViolationException)
                {
                    existingDb = await _persistence.GetActiveByKeyAsync(deduplicationKey);
                    if (existingDb != null)
                    {
                        await ScheduleAsync(existingDb);
                        return existingDb.Id;
                    }

                    throw;
                }

                _logger.LogInformation("Enqueueing move job {JobId} for audiobook {AudiobookId} to {Path}", job.Id, audiobookId, LogRedaction.SanitizeFilePath(requestedPath));
                await ScheduleAsync(job);
                return job.Id;
            }
            finally
            {
                _enqueueGate.Release();
            }
        }

        public async Task RecoverActiveJobsAsync(CancellationToken cancellationToken = default)
        {
            if (!_identityKeysReconciled)
            {
                await _persistence.ReconcileIdentityKeysAsync(cancellationToken);
                _identityKeysReconciled = true;
            }

            var activeJobs = await _persistence.GetActiveAsync(cancellationToken);
            if (_relocationService != null)
            {
                await _relocationService.ReconcileActiveAsync(cancellationToken);
            }
            foreach (var activeJob in activeJobs)
            {
                await ScheduleAsync(activeJob, cancellationToken);
            }

            if (activeJobs.Count > 0)
            {
                _logger.LogInformation("Recovered {Count} active move jobs from persistence", activeJobs.Count);
            }
        }

        public Task<int?> TryClaimJobAsync(
            Guid jobId,
            string leaseOwner,
            CancellationToken cancellationToken = default)
        {
            var now = _timeProvider.GetUtcNow();
            return _persistence.TryClaimAsync(
                jobId,
                leaseOwner,
                now,
                now.AddMinutes(2),
                cancellationToken);
        }

        public Task<bool> HeartbeatJobAsync(
            Guid jobId,
            string leaseOwner,
            int leaseGeneration,
            CancellationToken cancellationToken = default)
        {
            return _persistence.HeartbeatAsync(
                jobId,
                leaseOwner,
                leaseGeneration,
                _timeProvider.GetUtcNow().AddMinutes(2),
                cancellationToken);
        }

        public async Task<IReadOnlyList<MoveJob>> GetActiveJobsAsync(CancellationToken cancellationToken = default)
        {
            return await _persistence.GetActiveAsync(cancellationToken);
        }

        public Task<MoveQueueHealthSnapshot> GetQueueHealthAsync(
            CancellationToken cancellationToken = default) =>
            _persistence.GetHealthAsync(_timeProvider.GetUtcNow(), cancellationToken);

        public async Task<MoveJob?> GetJobAsync(Guid id, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _persistence.GetByIdAsync(id, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogWarning(ex, "Failed to retrieve move job {JobId}", id);
                return null;
            }
        }

        public async Task UpdateJobStatusAsync(
            Guid id,
            int leaseGeneration,
            MoveJobStatus status,
            string? error = null,
            CancellationToken cancellationToken = default)
        {
            var updatedAt = _timeProvider.GetUtcNow();
            MoveJob? dbJob;
            try
            {
                dbJob = await _persistence.GetByIdAsync(id, cancellationToken);
                var phase = status == MoveJobStatus.Running
                    && (dbJob?.Phase ?? MoveJobPhase.None) == MoveJobPhase.None
                        ? MoveJobPhase.Planned
                        : dbJob?.Phase ?? MoveJobPhase.None;
                var failureKind = status is MoveJobStatus.Failed or MoveJobStatus.NeedsAttention
                    ? MoveFailureKind.Unknown
                    : MoveFailureKind.None;
                var updated = await PersistWithRetryAsync(
                    () => _persistence.UpdateStatusAsync(id, leaseGeneration, status, phase, error, failureKind, updatedAt, cancellationToken),
                    cancellationToken);
                if (!updated)
                {
                    throw new MoveLeaseLostException(id, leaseGeneration);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogWarning(ex, "Failed to persist move job status change for {JobId}", id);
                throw;
            }

            LogStatusChange(id, status, error);

            if (_relocationService != null)
            {
                try
                {
                    await _relocationService.OnMoveJobStateChangedAsync(id, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    _logger.LogWarning(ex, "Failed to reconcile relocation for move job {JobId}", id);
                }
            }

            try
            {
                await _hubBroadcaster.BroadcastAsync("MoveJobUpdate", new
                {
                    jobId = id.ToString(),
                    audiobookId = dbJob?.AudiobookId,
                    status = status.ToString(),
                    error,
                    target = dbJob?.RequestedPath,
                    updatedAt
                }, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogWarning(ex, "Failed to broadcast MoveJobUpdate for job {JobId}", id);
            }
        }

        private void LogStatusChange(Guid id, MoveJobStatus status, string? error)
        {
            if (status == MoveJobStatus.Failed && !string.IsNullOrWhiteSpace(error))
            {
                _logger.LogError("Move job {JobId} FAILED with error: {Error}", id, error);
            }
            else
            {
                _logger.LogInformation("Updated move job {JobId} status to {Status}", id, status);
            }
        }

        public async Task<Guid?> RequeueMoveAsync(Guid jobId)
        {
            MoveJob? job;
            try
            {
                job = await _persistence.GetByIdAsync(jobId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogWarning(ex, "Failed to read move job from DB while requeueing {JobId}", jobId);
                return null;
            }

            if (job == null)
            {
                _logger.LogWarning("Attempted to requeue unknown move job {JobId}", jobId);
                return null;
            }

            if (!CanRequeueJobStatus(job.Status))
            {
                _logger.LogInformation("Move job {JobId} has status {Status} and cannot be requeued", jobId, job.Status);
                return null;
            }

            if (job.Status == MoveJobStatus.Queued)
            {
                await ScheduleAsync(job);
                return job.Id;
            }

            var deduplicationKey = await BuildDeduplicationKeyAsync(job.AudiobookId, job.RequestedPath);
            var activeJob = await _persistence.GetActiveByKeyAsync(deduplicationKey);
            if (activeJob != null && activeJob.Id != job.Id)
            {
                await ScheduleAsync(activeJob);
                return activeJob.Id;
            }

            job.Status = MoveJobStatus.Queued;
            job.Phase = MoveJobPhase.None;
            job.Error = null;
            job.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
            job.ActiveDeduplicationKey = deduplicationKey;
            await _persistence.RequeueAsync(job);
            await ScheduleAsync(job);
            _logger.LogInformation("Requeued move job {JobId} for audiobook {AudiobookId}", jobId, job.AudiobookId);
            return jobId;
        }

        private async Task ScheduleAsync(MoveJob job, CancellationToken cancellationToken = default)
        {
            await _channel.Writer.WriteAsync(job, cancellationToken);
        }

        private static bool CanRequeueJobStatus(MoveJobStatus status)
        {
            return status is MoveJobStatus.Failed or
                MoveJobStatus.NeedsAttention or
                MoveJobStatus.Completed or
                MoveJobStatus.Queued;
        }

        private async Task<string> BuildDeduplicationKeyAsync(int audiobookId, string? requestedPath)
        {
            var absolutePath = FileSystemPathIdentity.ResolveNativeAbsolutePath(requestedPath ?? string.Empty);
            var semantics = FileSystemPathSemantics.CurrentHostDefault;
            if (_semanticsResolver != null)
            {
                var resolution = await _semanticsResolver.ResolveAsync(absolutePath);
                if (resolution.State != PathIdentityState.Valid)
                {
                    throw new InvalidOperationException(
                        resolution.Reason ?? "Target filesystem identity is unavailable.");
                }

                semantics = resolution.Semantics;
            }

            return FileSystemPathIdentity.CreateKey(
                $"move:{audiobookId}",
                absolutePath,
                semantics);
        }

        private async Task<bool> PersistWithRetryAsync(Func<Task<bool>> operation, CancellationToken cancellationToken)
        {
            const int maxAttempts = 3;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await operation();
                }
                catch (PersistenceException) when (attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt * attempt), cancellationToken);
                }
            }
        }
    }
}
