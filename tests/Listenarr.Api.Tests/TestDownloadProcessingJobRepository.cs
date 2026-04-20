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

        public Task<List<DownloadProcessingJob>> GetDueRetryJobsAsync(CancellationToken cancellationToken = default)
        {
            if (_db == null) return Task.FromResult(new List<DownloadProcessingJob>());
            var now = DateTime.UtcNow;
            return _db.DownloadProcessingJobs
                .Where(j => j.Status == ProcessingJobStatus.Retry && j.NextRetryAt.HasValue && j.NextRetryAt <= now)
                .OrderByDescending(j => j.Priority)
                .ThenBy(j => j.NextRetryAt)
                .ToListAsync(cancellationToken);
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

        public async Task<QueueStats> GetStatsAsync()
        {
            if (_db == null) return new QueueStats();
            var jobs = await _db.DownloadProcessingJobs.ToListAsync();
            var result = new QueueStats();
            foreach (var j in jobs)
            {
                result.TotalJobs++;
                switch (j.Status)
                {
                    case ProcessingJobStatus.Pending: result.PendingJobs++; break;
                    case ProcessingJobStatus.Processing: result.ProcessingJobs++; break;
                    case ProcessingJobStatus.Completed: result.CompletedJobs++; break;
                    case ProcessingJobStatus.Failed: result.FailedJobs++; break;
                    case ProcessingJobStatus.Retry: result.RetryJobs++; break;
                }
            }
            var oldest = jobs.Where(j => j.Status == ProcessingJobStatus.Pending)
                .OrderBy(j => j.CreatedAt).Select(j => j.CreatedAt).FirstOrDefault();
            if (oldest != default) result.OldestPendingJob = oldest;
            return result;
        }

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
