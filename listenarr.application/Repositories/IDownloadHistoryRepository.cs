using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Listenarr.Domain.Models;

namespace Listenarr.Application.Repositories
{
    public interface IDownloadHistoryRepository
    {
        Task<DownloadHistory> AddAsync(DownloadHistory history, CancellationToken ct = default);
        Task<List<DownloadHistory>> GetByDownloadIdAsync(string downloadId, CancellationToken ct = default);
        Task<DownloadHistory?> GetLatestEventAsync(string downloadId, CancellationToken ct = default);
        Task<List<DownloadHistory>> GetPendingImportsAsync(CancellationToken ct = default);
        Task MarkAsImportedAsync(string downloadId, CancellationToken ct = default);
        Task<bool> WasImportedAsync(string downloadId, CancellationToken ct = default);
        Task<List<DownloadHistory>> GetRecentAsync(int count = 100, CancellationToken ct = default);
        Task<List<DownloadHistory>> GetFailedDownloadsAsync(DateTime since, CancellationToken ct = default);
        Task<int> DeleteOlderThanAsync(DateTime cutoffDate, CancellationToken ct = default);
        Task<int> GetCountAsync(CancellationToken ct = default);
    }
}
