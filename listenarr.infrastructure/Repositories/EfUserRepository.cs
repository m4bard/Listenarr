using Listenarr.Application.Repositories;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Repositories
{
    public class EfUserRepository : IUserRepository
    {
        private readonly ListenArrDbContext _db;

        public EfUserRepository(ListenArrDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
        {
            return await _db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        }

        public async Task<User> AddAsync(User user, CancellationToken ct = default)
        {
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);
            return user;
        }

        public async Task UpdateAsync(User user, CancellationToken ct = default)
        {
            _db.Users.Update(user);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<List<User>> GetAdminUsersAsync(CancellationToken ct = default)
        {
            return await _db.Users.Where(u => u.IsAdmin).ToListAsync(ct);
        }

        public async Task<int> CountAsync(CancellationToken ct = default)
        {
            return await _db.Users.CountAsync(ct);
        }
    }
}
