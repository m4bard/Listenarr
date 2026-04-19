using System.Threading;
using System.Threading.Tasks;
using Listenarr.Application.Repositories;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Repositories
{
    public class EfApplicationSettingsRepository : IApplicationSettingsRepository
    {
        private readonly ListenArrDbContext _db;

        public EfApplicationSettingsRepository(ListenArrDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<ApplicationSettings?> GetAsync(CancellationToken ct = default)
        {
            return await _db.ApplicationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, ct);
        }

        public async Task<ApplicationSettings> SaveAsync(ApplicationSettings settings, CancellationToken ct = default)
        {
            settings.Id = 1;

            var existing = await _db.ApplicationSettings.FindAsync([1], ct);

            if (existing == null)
            {
                _db.ApplicationSettings.Add(settings);
                await _db.SaveChangesAsync(ct);
                return settings;
            }

            // Detach the existing tracked entity to avoid identity-map conflicts when
            // `settings` is the same object reference that was previously Add()ed.
            if (!ReferenceEquals(existing, settings))
            {
                _db.Entry(existing).State = EntityState.Detached;
            }

            _db.ApplicationSettings.Update(settings);
            await _db.SaveChangesAsync(ct);
            return settings;
        }
    }
}
