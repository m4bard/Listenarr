using Listenarr.Domain.Models;

namespace Listenarr.Application.Repositories
{
    public interface IMonitoredSeriesRepository
    {
        Task<List<MonitoredSeries>> GetAllAsync(CancellationToken ct = default);
        Task<MonitoredSeries?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<MonitoredSeries?> GetByNameRegionLanguageAsync(string normalizedName, string region, string language, CancellationToken ct = default);
        Task<List<MonitoredSeries>> GetDueForSyncAsync(DateTime cutoff, CancellationToken ct = default);
        Task<MonitoredSeries> UpsertAsync(MonitoredSeries series, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
