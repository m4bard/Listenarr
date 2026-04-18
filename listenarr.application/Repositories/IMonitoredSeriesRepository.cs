using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Listenarr.Domain.Models;

namespace Listenarr.Application.Repositories
{
    public interface IMonitoredSeriesRepository
    {
        Task<List<MonitoredSeries>> GetAllAsync(CancellationToken ct = default);
        Task<MonitoredSeries?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<MonitoredSeries> UpsertAsync(MonitoredSeries series, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
