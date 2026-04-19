using Listenarr.Application.Repositories;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Repositories
{
    public class EfRemotePathMappingRepository : IRemotePathMappingRepository
    {
        private readonly ListenArrDbContext _db;

        public EfRemotePathMappingRepository(ListenArrDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<List<RemotePathMapping>> GetAllAsync(CancellationToken ct = default)
        {
            return await _db.RemotePathMappings.AsNoTracking().ToListAsync(ct);
        }

        public async Task<RemotePathMapping?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            return await _db.RemotePathMappings.FindAsync(new object[] { id }, ct);
        }

        public async Task<List<RemotePathMapping>> GetByClientAsync(string downloadClientId, CancellationToken ct = default)
        {
            return await _db.RemotePathMappings
                .AsNoTracking()
                .Where(m => m.DownloadClientId == downloadClientId)
                .OrderByDescending(m => m.RemotePath.Length)
                .ToListAsync(ct);
        }

        public async Task<RemotePathMapping> SaveAsync(RemotePathMapping mapping, CancellationToken ct = default)
        {
            var existing = await _db.RemotePathMappings.FindAsync(new object[] { mapping.Id }, ct);
            if (existing == null)
            {
                _db.RemotePathMappings.Add(mapping);
            }
            else
            {
                _db.Entry(existing).CurrentValues.SetValues(mapping);
            }
            await _db.SaveChangesAsync(ct);
            return existing ?? mapping;
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var mapping = await _db.RemotePathMappings.FindAsync(new object[] { id }, ct);
            if (mapping == null) return false;
            _db.RemotePathMappings.Remove(mapping);
            await _db.SaveChangesAsync(ct);
            return true;
        }
    }
}
