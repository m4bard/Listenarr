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
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories
{
    public class EfDownloadProcessingJobRepository : IDownloadProcessingJobRepository
    {
        private const int CleanupBatchSize = 500;

        private readonly IDbContextFactory<ListenArrDbContext> _dbFactory;

        public EfDownloadProcessingJobRepository(IDbContextFactory<ListenArrDbContext> dbFactory)
        {
            _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        }

        public async Task<List<string>> GetPendingDownloadIdsAsync(IEnumerable<string> completedDownloadIds)
        {
            var ids = (completedDownloadIds ?? Array.Empty<string>()).ToList();
            if (!ids.Any()) return new List<string>();

            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.DownloadProcessingJobs
                .Where(j => ids.Contains(j.DownloadId) && (j.Status == ProcessingJobStatus.Pending || j.Status == ProcessingJobStatus.Processing || j.Status == ProcessingJobStatus.Retry))
                .Select(j => j.DownloadId)
                .Distinct()
                .ToListAsync();
        }

        public async Task<List<string>> GetAllJobDownloadIdsAsync(IEnumerable<string> completedDownloadIds)
        {
            var ids = (completedDownloadIds ?? Array.Empty<string>()).ToList();
            if (!ids.Any()) return new List<string>();

            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.DownloadProcessingJobs
                .Where(j => ids.Contains(j.DownloadId))
                .Select(j => j.DownloadId)
                .Distinct()
                .ToListAsync();
        }

        public async Task<DownloadProcessingJob?> GetActiveByDownloadIdAsync(string downloadId)
        {
            var deduplicationKey = BuildActiveDeduplicationKey(downloadId);
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.DownloadProcessingJobs
                .Where(j => j.ActiveDeduplicationKey == deduplicationKey)
                .OrderBy(j => j.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<DownloadProcessingJob?> GetRecentCompletedByDownloadIdAsync(string downloadId, DateTime cutoff)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.DownloadProcessingJobs
                .Where(j => j.DownloadId == downloadId && j.Status == ProcessingJobStatus.Completed && j.CompletedAt.HasValue && j.CompletedAt >= cutoff)
                .OrderByDescending(j => j.CompletedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<DownloadProcessingJob> AddAsync(DownloadProcessingJob job)
        {
            job.ActiveDeduplicationKey = IsActive(job.Status)
                ? BuildActiveDeduplicationKey(job.DownloadId)
                : null;
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            ctx.DownloadProcessingJobs.Add(job);
            await ctx.SaveChangesAsync();
            return job;
        }

        public async Task<List<DownloadProcessingJob>> GetJobsByStatusAsync(ProcessingJobStatus status, CancellationToken cancellationToken = default)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.DownloadProcessingJobs
                .Where(j => j.Status == status)
                .ToListAsync(cancellationToken);

        }

        public async Task<List<DownloadProcessingJob>> GetDueRetryJobsAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            await using var ctx = await _dbFactory.CreateDbContextAsync(cancellationToken);
            return await ctx.DownloadProcessingJobs
                .Where(j => j.Status == ProcessingJobStatus.Retry && j.NextRetryAt.HasValue && j.NextRetryAt <= now)
                .OrderByDescending(j => j.Priority)
                .ThenBy(j => j.NextRetryAt)
                .ToListAsync(cancellationToken);
        }

        public async Task UpdateAsync(DownloadProcessingJob job)
        {
            job.ActiveDeduplicationKey = IsActive(job.Status)
                ? BuildActiveDeduplicationKey(job.DownloadId)
                : null;
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            ctx.DownloadProcessingJobs.Update(job);
            await ctx.SaveChangesAsync();
        }

        public async Task<DownloadProcessingJob?> GetByIdAsync(string jobId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.DownloadProcessingJobs.FindAsync(jobId);
        }

        public async Task<List<DownloadProcessingJob>> GetByDownloadIdAsync(string downloadId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.DownloadProcessingJobs
                .Where(j => j.DownloadId == downloadId)
                .OrderBy(j => j.CreatedAt)
                .ToListAsync();
        }

        public async Task<QueueStats> GetStatsAsync()
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var stats = await ctx.DownloadProcessingJobs
                .GroupBy(j => j.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var oldestPending = await ctx.DownloadProcessingJobs
                .Where(j => j.Status == ProcessingJobStatus.Pending)
                .OrderBy(j => j.CreatedAt)
                .Select(j => j.CreatedAt)
                .FirstOrDefaultAsync();

            var result = new QueueStats { OldestPendingJob = oldestPending == default ? null : oldestPending };
            foreach (var s in stats)
            {
                switch (s.Status)
                {
                    case ProcessingJobStatus.Pending: result.PendingJobs = s.Count; break;
                    case ProcessingJobStatus.Processing: result.ProcessingJobs = s.Count; break;
                    case ProcessingJobStatus.Completed: result.CompletedJobs = s.Count; break;
                    case ProcessingJobStatus.Failed: result.FailedJobs = s.Count; break;
                    case ProcessingJobStatus.Retry: result.RetryJobs = s.Count; break;
                }
                result.TotalJobs += s.Count;
            }
            return result;
        }

        public async Task<int> ArchiveAndDeleteCompletedBeforeAsync(
            IReadOnlyCollection<ProcessingJobStatus> statuses,
            DateTime cutoffUtc,
            CancellationToken cancellationToken = default)
        {
            if (statuses.Count == 0)
            {
                return 0;
            }

            await using var ctx = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var removed = 0;

            while (true)
            {
                var oldJobs = await ctx.DownloadProcessingJobs
                    .Where(j =>
                        statuses.Contains(j.Status) &&
                        j.CompletedAt.HasValue &&
                        j.CompletedAt < cutoffUtc)
                    .OrderBy(j => j.CompletedAt)
                    .ThenBy(j => j.Id)
                    .Take(CleanupBatchSize)
                    .ToListAsync(cancellationToken);

                if (oldJobs.Count == 0)
                {
                    return removed;
                }

                await ArchiveFailureDetailAsync(ctx, oldJobs, cancellationToken);

                ctx.DownloadProcessingJobs.RemoveRange(oldJobs);
                await ctx.SaveChangesAsync(cancellationToken);

                removed += oldJobs.Count;
                ctx.ChangeTracker.Clear();
            }
        }

        /// <summary>
        /// Stages the failure explanation of every failed job in the batch onto the download it
        /// describes. No save of its own: the caller's single SaveChanges covers the writes and the
        /// deletes together, so the detail cannot be lost between them or preserved twice.
        /// </summary>
        private static async Task ArchiveFailureDetailAsync(
            ListenArrDbContext ctx,
            List<DownloadProcessingJob> batch,
            CancellationToken cancellationToken)
        {
            // Only a failed job explains anything; a completed one has nothing worth keeping.
            var failedJobs = batch
                .Where(job => job.Status == ProcessingJobStatus.Failed)
                .ToList();
            if (failedJobs.Count == 0)
            {
                return;
            }

            var downloadIds = failedJobs
                .Select(job => job.DownloadId)
                .Distinct()
                .ToList();

            // Downloads already cleared by hand are simply absent, which is the case where there is
            // nothing left to explain.
            var downloads = await ctx.Downloads
                .Where(download => downloadIds.Contains(download.Id))
                .ToDictionaryAsync(download => download.Id, cancellationToken);

            // The batch is ordered oldest first, so a download that failed repeatedly ends up with
            // its entries in the order the failures happened.
            foreach (var job in failedJobs)
            {
                if (downloads.TryGetValue(job.DownloadId, out var download))
                {
                    job.ArchiveFailureDetailOnto(download);
                }
            }
        }

        public async Task<List<DownloadProcessingJob>> GetRecentAsync(int count)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.DownloadProcessingJobs
                .OrderByDescending(j => j.CreatedAt)
                .Take(count)
                .ToListAsync();
        }

        public async Task<List<DownloadProcessingJob>> GetStuckProcessingJobsAsync(CancellationToken cancellationToken = default)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.DownloadProcessingJobs
                .Where(j => j.Status == ProcessingJobStatus.Processing)
                .ToListAsync(cancellationToken);
        }

        private static bool IsActive(ProcessingJobStatus status) =>
            status is ProcessingJobStatus.Pending
                or ProcessingJobStatus.Processing
                or ProcessingJobStatus.Retry;

        private static string BuildActiveDeduplicationKey(string downloadId) =>
            downloadId.Trim().ToUpperInvariant();
    }
}
