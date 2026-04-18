using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Listenarr.Domain.Models;

namespace Listenarr.Application.Repositories
{
    public interface IHistoryRepository
    {
        Task<List<History>> GetPagedAsync(int limit, int offset, CancellationToken ct = default);
        Task<int> CountAsync(CancellationToken ct = default);
        Task<List<History>> GetByAudiobookIdAsync(int audiobookId, CancellationToken ct = default);
        Task<List<History>> GetByEventTypeAsync(string eventType, int? limit = null, CancellationToken ct = default);
        Task<List<History>> GetBySourceAsync(string source, int? limit = null, CancellationToken ct = default);
        Task<List<History>> GetRecentAsync(int limit, CancellationToken ct = default);
        Task<History> AddAsync(History entry, CancellationToken ct = default);
        Task UpdateAsync(History entry, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
        Task DeleteAllAsync(CancellationToken ct = default);
        Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
    }
}
