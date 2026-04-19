using Listenarr.Application.Repositories;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Repositories
{
    public class EfApiConfigurationRepository : IApiConfigurationRepository
    {
        private readonly ListenArrDbContext _db;

        public EfApiConfigurationRepository(ListenArrDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<List<ApiConfiguration>> GetAllAsync(CancellationToken ct = default)
        {
            return await _db.ApiConfigurations.AsNoTracking().OrderBy(c => c.Priority).ToListAsync(ct);
        }

        public async Task<ApiConfiguration?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            return await _db.ApiConfigurations.FirstOrDefaultAsync(c => c.Id == id, ct);
        }

        public async Task<ApiConfiguration> SaveAsync(ApiConfiguration config, CancellationToken ct = default)
        {
            var existing = await _db.ApiConfigurations.FirstOrDefaultAsync(c => c.Id == config.Id, ct);
            if (existing == null)
            {
                _db.ApiConfigurations.Add(config);
                await _db.SaveChangesAsync(ct);
                return config;
            }

            _db.Entry(existing).CurrentValues.SetValues(config);
            existing.HeadersJson = config.HeadersJson;
            existing.ParametersJson = config.ParametersJson;
            await _db.SaveChangesAsync(ct);
            return existing;
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
        {
            var config = await _db.ApiConfigurations.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (config == null) return false;
            _db.ApiConfigurations.Remove(config);
            await _db.SaveChangesAsync(ct);
            return true;
        }
    }
}
