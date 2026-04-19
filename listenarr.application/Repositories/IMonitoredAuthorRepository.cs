using Listenarr.Domain.Models;

namespace Listenarr.Application.Repositories
{
    public interface IMonitoredAuthorRepository
    {
        Task<List<MonitoredAuthor>> GetAllAsync(CancellationToken ct = default);
        Task<MonitoredAuthor?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<MonitoredAuthor?> GetByNameRegionLanguageAsync(string normalizedName, string region, string language, CancellationToken ct = default);
        Task<List<MonitoredAuthor>> GetDueForSyncAsync(DateTime cutoff, CancellationToken ct = default);
        Task<MonitoredAuthor> UpsertAsync(MonitoredAuthor author, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
