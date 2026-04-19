using Listenarr.Application.Repositories;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Repositories
{
    public class EfUserSessionRepository : IUserSessionRepository
    {
        private readonly ListenArrDbContext _db;

        public EfUserSessionRepository(ListenArrDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<UserSession> CreateAsync(UserSession session, CancellationToken ct = default)
        {
            _db.UserSessions.Add(session);
            await _db.SaveChangesAsync(ct);
            return session;
        }

        public async Task<UserSession?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default)
        {
            var session = await _db.UserSessions.SingleOrDefaultAsync(s => s.TokenHash == tokenHash, ct);
            if (session == null) return null;

            if (session.ExpiresAt < DateTime.UtcNow)
            {
                _db.UserSessions.Remove(session);
                await _db.SaveChangesAsync(ct);
                return null;
            }

            session.LastAccessed = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return session;
        }

        public async Task InvalidateAsync(string sessionToken, CancellationToken ct = default)
        {
            var session = await _db.UserSessions.SingleOrDefaultAsync(s => s.TokenHash == sessionToken, ct);
            if (session == null) return;
            _db.UserSessions.Remove(session);
            await _db.SaveChangesAsync(ct);
        }

        public async Task InvalidateAllForUserAsync(string username, CancellationToken ct = default)
        {
            var sessions = await _db.UserSessions.Where(s => s.Username == username).ToListAsync(ct);
            _db.UserSessions.RemoveRange(sessions);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<int> CleanupExpiredAsync(CancellationToken ct = default)
        {
            var expired = await _db.UserSessions.Where(s => s.ExpiresAt < DateTime.UtcNow).ToListAsync(ct);
            _db.UserSessions.RemoveRange(expired);
            await _db.SaveChangesAsync(ct);
            return expired.Count;
        }

        public async Task<int> GetActiveCountAsync(string username, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var expired = await _db.UserSessions
                .Where(s => s.Username == username && s.ExpiresAt <= now)
                .ToListAsync(ct);
            if (expired.Count > 0)
            {
                _db.UserSessions.RemoveRange(expired);
                await _db.SaveChangesAsync(ct);
            }
            return await _db.UserSessions.CountAsync(s => s.Username == username && s.ExpiresAt > now, ct);
        }
    }
}
