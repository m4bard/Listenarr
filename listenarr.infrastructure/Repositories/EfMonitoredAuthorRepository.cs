using Listenarr.Application.Repositories;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Repositories
{
    public class EfMonitoredAuthorRepository : IMonitoredAuthorRepository
    {
        private readonly ListenArrDbContext _db;

        public EfMonitoredAuthorRepository(ListenArrDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<List<MonitoredAuthor>> GetAllAsync(CancellationToken ct = default)
        {
            return await _db.MonitoredAuthors.AsNoTracking().ToListAsync(ct);
        }

        public async Task<MonitoredAuthor?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            return await _db.MonitoredAuthors.FindAsync(new object[] { id }, ct);
        }

        public async Task<MonitoredAuthor> UpsertAsync(MonitoredAuthor author, CancellationToken ct = default)
        {
            var existing = author.Id > 0
                ? await _db.MonitoredAuthors.FindAsync(new object[] { author.Id }, ct)
                : null;

            if (existing == null)
            {
                _db.MonitoredAuthors.Add(author);
                await _db.SaveChangesAsync(ct);
                return author;
            }

            _db.Entry(existing).CurrentValues.SetValues(author);
            await _db.SaveChangesAsync(ct);
            return existing;
        }

        public async Task<MonitoredAuthor?> GetByNameRegionLanguageAsync(string normalizedName, string region, string language, CancellationToken ct = default)
        {
            return await _db.MonitoredAuthors
                .FirstOrDefaultAsync(a => a.AuthorNameNormalized == normalizedName && a.Region == region && a.Language == language, ct);
        }

        public async Task<List<MonitoredAuthor>> GetDueForSyncAsync(DateTime cutoff, CancellationToken ct = default)
        {
            return await _db.MonitoredAuthors
                .Where(a => a.LastCheckedAt == null || a.LastCheckedAt < cutoff)
                .OrderBy(a => a.LastCheckedAt ?? DateTime.MinValue)
                .ToListAsync(ct);
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var author = await _db.MonitoredAuthors.FindAsync(new object[] { id }, ct);
            if (author == null) return false;
            _db.MonitoredAuthors.Remove(author);
            await _db.SaveChangesAsync(ct);
            return true;
        }
    }
}
