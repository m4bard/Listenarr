using Listenarr.Application.Repositories;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Repositories
{
    public class EfMonitoredSeriesRepository : IMonitoredSeriesRepository
    {
        private readonly ListenArrDbContext _db;

        public EfMonitoredSeriesRepository(ListenArrDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<List<MonitoredSeries>> GetAllAsync(CancellationToken ct = default)
        {
            return await _db.MonitoredSeries.AsNoTracking().ToListAsync(ct);
        }

        public async Task<MonitoredSeries?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            return await _db.MonitoredSeries.FindAsync(new object[] { id }, ct);
        }

        public async Task<MonitoredSeries> UpsertAsync(MonitoredSeries series, CancellationToken ct = default)
        {
            var existing = series.Id > 0
                ? await _db.MonitoredSeries.FindAsync(new object[] { series.Id }, ct)
                : null;

            if (existing == null)
            {
                _db.MonitoredSeries.Add(series);
                await _db.SaveChangesAsync(ct);
                return series;
            }

            _db.Entry(existing).CurrentValues.SetValues(series);
            await _db.SaveChangesAsync(ct);
            return existing;
        }

        public async Task<MonitoredSeries?> GetByNameRegionLanguageAsync(string normalizedName, string region, string language, CancellationToken ct = default)
        {
            return await _db.MonitoredSeries
                .FirstOrDefaultAsync(s => s.SeriesNameNormalized == normalizedName && s.Region == region && s.Language == language, ct);
        }

        public async Task<List<MonitoredSeries>> GetDueForSyncAsync(DateTime cutoff, CancellationToken ct = default)
        {
            return await _db.MonitoredSeries
                .Where(s => s.LastCheckedAt == null || s.LastCheckedAt < cutoff)
                .OrderBy(s => s.LastCheckedAt ?? DateTime.MinValue)
                .ToListAsync(ct);
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var series = await _db.MonitoredSeries.FindAsync(new object[] { id }, ct);
            if (series == null) return false;
            _db.MonitoredSeries.Remove(series);
            await _db.SaveChangesAsync(ct);
            return true;
        }
    }
}
