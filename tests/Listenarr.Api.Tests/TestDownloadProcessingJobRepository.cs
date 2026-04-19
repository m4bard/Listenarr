using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Listenarr.Application.Repositories;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Api.Tests
{
    public class TestDownloadProcessingJobRepository : IDownloadProcessingJobRepository
    {
        private readonly ListenArrDbContext? _db;

        public TestDownloadProcessingJobRepository(ListenArrDbContext? db = null)
        {
            _db = db;
        }

        public Task<List<string>> GetPendingDownloadIdsAsync(IEnumerable<string> completedDownloadIds)
        {
            var ids = (completedDownloadIds ?? Array.Empty<string>()).ToList();
            if (_db == null) return Task.FromResult(new List<string>());
            return _db.DownloadProcessingJobs
                .Where(j => ids.Contains(j.DownloadId) && (j.Status == ProcessingJobStatus.Pending || j.Status == ProcessingJobStatus.Processing || j.Status == ProcessingJobStatus.Retry))
                .Select(j => j.DownloadId)
                .Distinct()
                .ToListAsync();
        }

        public Task<List<string>> GetAllJobDownloadIdsAsync(IEnumerable<string> completedDownloadIds)
        {
            var ids = (completedDownloadIds ?? Array.Empty<string>()).ToList();
            if (_db == null) return Task.FromResult(new List<string>());
            return _db.DownloadProcessingJobs
                .Where(j => ids.Contains(j.DownloadId))
                .Select(j => j.DownloadId)
                .Distinct()
                .ToListAsync();
        }

        public Task<DownloadProcessingJob?> GetActiveByDownloadIdAsync(string downloadId)
        {
            if (_db == null) return Task.FromResult<DownloadProcessingJob?>(null);
            return _db.DownloadProcessingJobs
                .Where(j => j.DownloadId == downloadId && (j.Status == ProcessingJobStatus.Pending || j.Status == ProcessingJobStatus.Processing || j.Status == ProcessingJobStatus.Retry))
                .OrderBy(j => j.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public Task<DownloadProcessingJob?> GetRecentCompletedByDownloadIdAsync(string downloadId, DateTime cutoff)
        {
            if (_db == null) return Task.FromResult<DownloadProcessingJob?>(null);
            return _db.DownloadProcessingJobs
                .Where(j => j.DownloadId == downloadId && j.Status == ProcessingJobStatus.Completed && j.CompletedAt.HasValue && j.CompletedAt >= cutoff)
                .OrderByDescending(j => j.CompletedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<DownloadProcessingJob> AddAsync(DownloadProcessingJob job)
        {
            if (_db != null) { _db.DownloadProcessingJobs.Add(job); await _db.SaveChangesAsync(); }
            return job;
        }

        public Task<DownloadProcessingJob?> GetNextPendingAsync()
        {
            if (_db == null) return Task.FromResult<DownloadProcessingJob?>(null);
            return _db.DownloadProcessingJobs
                .Where(j => j.Status == ProcessingJobStatus.Pending)
                .OrderByDescending(j => j.Priority).ThenBy(j => j.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public Task<List<DownloadProcessingJob>> GetDueRetryJobsAsync()
        {
            if (_db == null) return Task.FromResult(new List<DownloadProcessingJob>());
            var now = DateTime.UtcNow;
            return _db.DownloadProcessingJobs
                .Where(j => j.Status == ProcessingJobStatus.Retry && j.NextRetryAt.HasValue && j.NextRetryAt <= now)
                .ToListAsync();
        }

        public async Task UpdateAsync(DownloadProcessingJob job)
        {
            if (_db != null) { _db.DownloadProcessingJobs.Update(job); await _db.SaveChangesAsync(); }
        }

        public Task<DownloadProcessingJob?> GetByIdAsync(string jobId)
        {
            if (_db == null) return Task.FromResult<DownloadProcessingJob?>(null);
            return _db.DownloadProcessingJobs.FindAsync(jobId).AsTask();
        }

        public Task<List<DownloadProcessingJob>> GetByDownloadIdAsync(string downloadId)
        {
            if (_db == null) return Task.FromResult(new List<DownloadProcessingJob>());
            return _db.DownloadProcessingJobs.Where(j => j.DownloadId == downloadId).ToListAsync();
        }

        public Task<QueueStats> GetStatsAsync() => Task.FromResult(new QueueStats());

        public Task CleanupOldJobsAsync(int retentionDays) => Task.CompletedTask;

        public Task<List<DownloadProcessingJob>> GetRecentAsync(int count)
        {
            if (_db == null) return Task.FromResult(new List<DownloadProcessingJob>());
            return _db.DownloadProcessingJobs.OrderByDescending(j => j.CreatedAt).Take(count).ToListAsync();
        }

        public Task<List<DownloadProcessingJob>> GetStuckProcessingJobsAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            if (_db == null) return Task.FromResult(new List<DownloadProcessingJob>());
            return _db.DownloadProcessingJobs.Where(j => j.Status == ProcessingJobStatus.Processing).ToListAsync(cancellationToken);
        }

    }
}
