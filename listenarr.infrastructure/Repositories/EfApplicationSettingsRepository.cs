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

            var existing = await _db.ApplicationSettings.FirstOrDefaultAsync(s => s.Id == 1, ct);

            if (existing == null)
            {
                _db.ApplicationSettings.Add(settings);
                await _db.SaveChangesAsync(ct);
                return settings;
            }

            _db.Entry(existing).CurrentValues.SetValues(settings);
            existing.AllowedFileExtensions = settings.AllowedFileExtensions;
            existing.ImportBlacklistExtensions = settings.ImportBlacklistExtensions ?? new List<string>();

            if (settings.EnabledNotificationTriggers != null)
            {
                existing.EnabledNotificationTriggers = settings.EnabledNotificationTriggers;
                _db.Entry(existing).Property(e => e.EnabledNotificationTriggers).IsModified = true;
            }
            else
            {
                existing.EnabledNotificationTriggers ??= new List<string>();
            }

            if (settings.Webhooks != null)
            {
                existing.Webhooks = settings.Webhooks.Select(w => new WebhookConfiguration
                {
                    Name = w.Name,
                    Url = w.Url,
                    Type = w.Type,
                    Triggers = w.Triggers?.ToList() ?? new List<string>(),
                    IsEnabled = w.IsEnabled
                }).ToList();
                _db.Entry(existing).Property(e => e.Webhooks).IsModified = true;
            }
            else
            {
                existing.Webhooks ??= new List<WebhookConfiguration>();
            }

            _db.Update(existing);
            await _db.SaveChangesAsync(ct);
            return existing;
        }
    }
}
