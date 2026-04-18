using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Listenarr.Application.Repositories;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Repositories
{
    public class EfHistoryRepository : Listenarr.Application.Repositories.IHistoryRepository
    {
        private readonly ListenArrDbContext _db;

        public EfHistoryRepository(ListenArrDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<List<History>> GetPagedAsync(int limit, int offset, CancellationToken ct = default)
        {
            return await _db.History
                .AsNoTracking()
                .OrderByDescending(h => h.Timestamp)
                .Skip(offset)
                .Take(limit)
                .ToListAsync(ct);
        }

        public async Task<int> CountAsync(CancellationToken ct = default)
        {
            return await _db.History.CountAsync(ct);
        }

        public async Task<List<History>> GetByAudiobookIdAsync(int audiobookId, CancellationToken ct = default)
        {
            return await _db.History
                .AsNoTracking()
                .Where(h => h.AudiobookId == audiobookId)
                .OrderByDescending(h => h.Timestamp)
                .ToListAsync(ct);
        }

        public async Task<List<History>> GetByEventTypeAsync(string eventType, int? limit = null, CancellationToken ct = default)
        {
            var query = _db.History
                .AsNoTracking()
                .Where(h => h.EventType == eventType)
                .OrderByDescending(h => h.Timestamp);

            return limit.HasValue
                ? await query.Take(limit.Value).ToListAsync(ct)
                : await query.ToListAsync(ct);
        }

        public async Task<List<History>> GetBySourceAsync(string source, int? limit = null, CancellationToken ct = default)
        {
            var query = _db.History
                .AsNoTracking()
                .Where(h => h.Source == source)
                .OrderByDescending(h => h.Timestamp);

            return limit.HasValue
                ? await query.Take(limit.Value).ToListAsync(ct)
                : await query.ToListAsync(ct);
        }

        public async Task<List<History>> GetRecentAsync(int limit, CancellationToken ct = default)
        {
            return await _db.History
                .AsNoTracking()
                .OrderByDescending(h => h.Timestamp)
                .Take(limit)
                .ToListAsync(ct);
        }

        public async Task<History> AddAsync(History entry, CancellationToken ct = default)
        {
            _db.History.Add(entry);
            await _db.SaveChangesAsync(ct);
            return entry;
        }

        public async Task UpdateAsync(History entry, CancellationToken ct = default)
        {
            _db.History.Update(entry);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var entry = await _db.History.FindAsync(new object[] { id }, ct);
            if (entry == null) return false;
            _db.History.Remove(entry);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task DeleteAllAsync(CancellationToken ct = default)
        {
            _db.History.RemoveRange(_db.History);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
        {
            var old = await _db.History.Where(h => h.Timestamp < cutoff).ToListAsync(ct);
            _db.History.RemoveRange(old);
            await _db.SaveChangesAsync(ct);
            return old.Count;
        }
    }
}
