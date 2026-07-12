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
using System.Collections.Concurrent;
using System.Threading.Channels;
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Jobs
{
    public class ScanQueueService : IScanQueueService
    {
        private readonly ConcurrentDictionary<Guid, ScanJob> _jobs = new();
        private readonly Channel<ScanJob> _channel = Channel.CreateUnbounded<ScanJob>();
        private readonly SemaphoreSlim _enqueueGate = new(1, 1);
        private readonly ILogger<ScanQueueService> _logger;
        private readonly IFileSystemSemanticsResolver _semanticsResolver;

        public ScanQueueService(
            ILogger<ScanQueueService> logger,
            IFileSystemSemanticsResolver semanticsResolver)
        {
            _logger = logger;
            _semanticsResolver = semanticsResolver;
        }

        public async Task<Guid> EnqueueScanAsync(
            Audiobook audiobook,
            string? path = null,
            string? correlationId = null,
            string? downloadId = null)
        {
            return await EnqueueScanCoreAsync(
                audiobook,
                path,
                correlationId,
                downloadId,
                stillPending: null)
                ?? throw new InvalidOperationException("A normal scan enqueue was unexpectedly canceled.");
        }

        public Task<Guid?> EnqueueRecoveredScanAsync(
            Audiobook audiobook,
            string correlationId,
            Func<Task<bool>> stillPending) =>
            EnqueueScanCoreAsync(
                audiobook,
                path: null,
                correlationId,
                downloadId: null,
                stillPending);

        private async Task<Guid?> EnqueueScanCoreAsync(
            Audiobook audiobook,
            string? path,
            string? correlationId,
            string? downloadId,
            Func<Task<bool>>? stillPending)
        {
            var pathSemantics = !string.IsNullOrWhiteSpace(path)
                ? await ResolvePathSemanticsAsync(path)
                : null;
            await _enqueueGate.WaitAsync();
            try
            {
                if (stillPending != null && !await stillPending())
                {
                    return null;
                }

                // Deduplicate against active jobs while holding the same gate used to
                // publish new jobs. This keeps immediate move dispatch and outbox replay
                // from both observing an empty correlation and enqueueing duplicate scans.
                try
                {
                    var matchingJobs = _jobs.Values.Where(job =>
                    {
                        if (job.AudiobookId != audiobook.Id) return false;
                        var bothNull = job.Path == null && path == null;
                        var bothMatch = job.Path != null
                            && path != null
                            && AreEquivalentPaths(job.Path, path, pathSemantics);
                        return bothNull || bothMatch;
                    });

                    // A correlation id identifies one active completion handoff. Durable
                    // terminal history decides whether an outbox replay is finished; this
                    // in-memory queue only deduplicates jobs that can still be processed.
                    var correlated = !string.IsNullOrWhiteSpace(correlationId)
                        ? matchingJobs.FirstOrDefault(job => string.Equals(
                            job.CorrelationId,
                            correlationId,
                            StringComparison.Ordinal))
                        : null;
                    if (correlated != null
                        && (string.Equals(
                                correlated.Status,
                                "Queued",
                                StringComparison.OrdinalIgnoreCase)
                            || string.Equals(
                                correlated.Status,
                                "Processing",
                                StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogInformation(
                            "Found active correlated scan job {JobId} for audiobook {AudiobookId}; reusing completion handoff",
                            correlated.Id,
                            audiobook.Id);
                        return correlated.Id;
                    }

                    var active = matchingJobs.FirstOrDefault(job =>
                        string.Equals(job.Status, "Queued", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(job.Status, "Processing", StringComparison.OrdinalIgnoreCase));
                    if (active != null)
                    {
                        _logger.LogInformation("Found active scan job {JobId} for audiobook {AudiobookId} (path: {Path}) with status {Status}; deduping and returning existing job id", active.Id, audiobook.Id, LogRedaction.SanitizeFilePath(path), active.Status);
                        return active.Id;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    // If dedupe check fails for any reason, fall back to enqueueing a new job.
                    _logger.LogWarning(ex, "Failed while checking existing scan jobs for dedupe; will enqueue new job");
                }

                var job = new ScanJob
                {
                    AudiobookId = audiobook.Id,
                    Path = path,
                    CorrelationId = correlationId,
                    DownloadId = downloadId
                };
                _jobs[job.Id] = job;
                _logger.LogInformation("Enqueueing scan job {JobId} for audiobook {AudiobookId} (path: {Path})", job.Id, audiobook.Id, LogRedaction.SanitizeFilePath(path));
                await _channel.Writer.WriteAsync(job);
                _logger.LogInformation("Scan job {JobId} written to channel", job.Id);
                return job.Id;
            }
            finally
            {
                _enqueueGate.Release();
            }
        }

        public bool TryGetJob(Guid id, out ScanJob? job) => _jobs.TryGetValue(id, out job);

        public ChannelReader<ScanJob> Reader => _channel.Reader;

        public void UpdateJobStatus(Guid id, string status, string? error = null, int? found = null, int? created = null)
        {
            _enqueueGate.Wait();
            try
            {
                UpdateJobStatusCore(id, status, error);
            }
            finally
            {
                _enqueueGate.Release();
            }
        }

        public async Task CommitTerminalJobStatusAsync(
            Guid jobId,
            Func<Task<(string Status, string? Error)>> persistTerminalState,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(persistTerminalState);
            await _enqueueGate.WaitAsync(cancellationToken);
            try
            {
                // Recovered enqueue uses this same gate while checking durable terminal
                // history. Commit the history and authoritative in-memory status together
                // so replay cannot enqueue between those two state transitions.
                var terminalState = await persistTerminalState();
                UpdateJobStatusCore(jobId, terminalState.Status, terminalState.Error);
            }
            finally
            {
                _enqueueGate.Release();
            }
        }

        private void UpdateJobStatusCore(Guid id, string status, string? error)
        {
            if (_jobs.TryGetValue(id, out var job))
            {
                job.Status = status;
                job.Error = error;
                _jobs[id] = job;
                _logger.LogInformation("Updated scan job {JobId} status to {Status}", id, status);
            }
            else
            {
                _logger.LogWarning("Attempted to update unknown scan job {JobId} to {Status}", id, status);
            }
        }

        public async Task<Guid?> RequeueScanAsync(Guid jobId)
        {
            await _enqueueGate.WaitAsync();
            try
            {
                if (!_jobs.TryGetValue(jobId, out var job))
                {
                    _logger.LogWarning("Attempted to requeue unknown scan job {JobId}", jobId);
                    return null;
                }

                // Allow requeue for Failed jobs or Completed (explicit re-run).
                if (!CanRequeueJobStatus(job.Status))
                {
                    _logger.LogInformation("Scan job {JobId} has status {Status} and cannot be requeued", jobId, job.Status);
                    return null;
                }

                var pathSemantics = !string.IsNullOrWhiteSpace(job.Path)
                    ? await ResolvePathSemanticsAsync(job.Path)
                    : null;
                var activeReplacement = _jobs.Values.FirstOrDefault(candidate =>
                    candidate.Id != job.Id
                    && candidate.AudiobookId == job.AudiobookId
                    && string.Equals(candidate.CorrelationId, job.CorrelationId, StringComparison.Ordinal)
                    && ((candidate.Path == null && job.Path == null)
                        || (candidate.Path != null
                            && job.Path != null
                            && AreEquivalentPaths(candidate.Path, job.Path, pathSemantics)))
                    && (string.Equals(candidate.Status, "Queued", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(candidate.Status, "Processing", StringComparison.OrdinalIgnoreCase)));
                if (activeReplacement != null)
                {
                    return activeReplacement.Id;
                }

                var newJob = new ScanJob
                {
                    AudiobookId = job.AudiobookId,
                    Path = job.Path,
                    CorrelationId = job.CorrelationId,
                    DownloadId = job.DownloadId
                };
                _jobs[newJob.Id] = newJob;
                _logger.LogInformation("Requeueing scan job {OldJobId} as new job {NewJobId} for audiobook {AudiobookId}", jobId, newJob.Id, job.AudiobookId);
                await _channel.Writer.WriteAsync(newJob);
                return newJob.Id;
            }
            finally
            {
                _enqueueGate.Release();
            }
        }

        private async Task<FileSystemPathSemantics?> ResolvePathSemanticsAsync(string path)
        {
            try
            {
                var resolution = await _semanticsResolver.ResolveAsync(path);
                return resolution.State == PathIdentityState.Valid ? resolution.Semantics : null;
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
            {
                _logger.LogDebug(exception, "Failed to resolve scan job path semantics for {Path}", LogRedaction.SanitizeFilePath(path));
                return null;
            }
        }

        private static bool AreEquivalentPaths(
            string left,
            string right,
            FileSystemPathSemantics? semantics)
        {
            return semantics != null
                ? FileSystemPathIdentity.AreEquivalent(left, right, semantics.Value)
                : string.Equals(left, right, StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines if a job with the given status can be requeued.
        /// </summary>
        private static bool CanRequeueJobStatus(string status)
        {
            return string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "Queued", StringComparison.OrdinalIgnoreCase);
        }
    }
}
