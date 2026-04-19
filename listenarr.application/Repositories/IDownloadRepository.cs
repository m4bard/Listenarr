using Listenarr.Domain.Models;

namespace Listenarr.Application.Repositories
{
    public interface IDownloadRepository
    {
        Task AddAsync(Download download);
        Task<Download?> FindAsync(string id);
        Task UpdateAsync(Download download);
        Task UpdateMetadataAsync(string id, string key, object? value);
        Task RemoveAsync(string id);
        Task<List<Download>> GetAllAsync();
        Task<List<QueueTrackedDownload>> GetQueueDisplayCandidatesAsync();
        Task<List<QueueTrackedDownload>> GetQueueMatchingCandidatesAsync();
        Task<List<string>> GetKnownClientItemIdsAsync();
        Task<List<Download>> GetByClientAsync(string clientId);
        Task<List<Download>> GetByIdsAsync(IEnumerable<string> ids);
        Task<List<Download>> GetByAudiobookIdAsync(int audiobookId, CancellationToken ct = default);
        /// <summary>Returns downloads in Completed/ImportPending/Processing status ordered by CompletedAt, for processing job creation.</summary>
        Task<List<Download>> GetCompletionCandidatesAsync(int limit);
        /// <summary>Returns downloads that require active monitoring (non-terminal, non-ImportBlocked).</summary>
        Task<List<Download>> GetActiveForMonitoringAsync();
        /// <summary>Returns the most recent <paramref name="count"/> downloads ordered by StartedAt descending.</summary>
        Task<List<Download>> GetRecentAsync(int count);
        /// <summary>Returns distinct audiobook IDs for downloads whose status is in <paramref name="statuses"/>.</summary>
        Task<List<int>> GetActiveAudiobookIdsAsync(IEnumerable<DownloadStatus> statuses);
    }
}
