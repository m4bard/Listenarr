using Listenarr.Application.Repositories;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Repositories
{
    public class EfDownloadClientConfigurationRepository : IDownloadClientConfigurationRepository
    {
        private readonly ListenArrDbContext _db;

        public EfDownloadClientConfigurationRepository(ListenArrDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<List<DownloadClientConfiguration>> GetAllAsync(CancellationToken ct = default)
        {
            return await _db.DownloadClientConfigurations.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
        }

        public async Task<DownloadClientConfiguration?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            return await _db.DownloadClientConfigurations.FirstOrDefaultAsync(c => c.Id == id, ct);
        }

        public async Task<DownloadClientConfiguration> SaveAsync(DownloadClientConfiguration config, CancellationToken ct = default)
        {
            var existing = await _db.DownloadClientConfigurations.FirstOrDefaultAsync(c => c.Id == config.Id, ct);
            if (existing == null)
            {
                _db.DownloadClientConfigurations.Add(config);
                await _db.SaveChangesAsync(ct);
                return config;
            }

            _db.Entry(existing).CurrentValues.SetValues(config);
            existing.SettingsJson = config.SettingsJson;
            await _db.SaveChangesAsync(ct);
            return existing;
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
        {
            var config = await _db.DownloadClientConfigurations.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (config == null) return false;
            _db.DownloadClientConfigurations.Remove(config);
            await _db.SaveChangesAsync(ct);
            return true;
        }
    }
}
