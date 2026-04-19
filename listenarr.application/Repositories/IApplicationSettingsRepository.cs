using Listenarr.Domain.Models;

namespace Listenarr.Application.Repositories
{
    public interface IApplicationSettingsRepository
    {
        Task<ApplicationSettings?> GetAsync(CancellationToken ct = default);
        Task<ApplicationSettings> SaveAsync(ApplicationSettings settings, CancellationToken ct = default);
    }
}
