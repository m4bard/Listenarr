using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Listenarr.Domain.Models;

namespace Listenarr.Application.Repositories
{
    public interface IDownloadProcessingJobRepository
    {
        Task<List<string>> GetPendingDownloadIdsAsync(IEnumerable<string> completedDownloadIds);
        Task<List<string>> GetAllJobDownloadIdsAsync(IEnumerable<string> completedDownloadIds);
        Task<DownloadProcessingJob?> GetActiveByDownloadIdAsync(string downloadId);
        Task<DownloadProcessingJob?> GetRecentCompletedByDownloadIdAsync(string downloadId, DateTime cutoff);
        Task<DownloadProcessingJob> AddAsync(DownloadProcessingJob job);
        Task<DownloadProcessingJob?> GetNextPendingAsync();
        Task<List<DownloadProcessingJob>> GetDueRetryJobsAsync();
        Task UpdateAsync(DownloadProcessingJob job);
        Task<DownloadProcessingJob?> GetByIdAsync(string jobId);
        Task<List<DownloadProcessingJob>> GetByDownloadIdAsync(string downloadId);
        Task<QueueStats> GetStatsAsync();
        Task CleanupOldJobsAsync(int retentionDays);
        Task<List<DownloadProcessingJob>> GetRecentAsync(int count);
        Task<List<DownloadProcessingJob>> GetStuckProcessingJobsAsync();
        Task SaveChangesAsync();
    }
}
