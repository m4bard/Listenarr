using System.Threading;
using System.Threading.Tasks;
using Listenarr.Domain.Models;

namespace Listenarr.Application.Repositories
{
    public interface IUserSessionRepository
    {
        Task<UserSession> CreateAsync(UserSession session, CancellationToken ct = default);
        Task<UserSession?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);
        Task InvalidateAsync(string sessionToken, CancellationToken ct = default);
        Task InvalidateAllForUserAsync(string username, CancellationToken ct = default);
        Task<int> CleanupExpiredAsync(CancellationToken ct = default);
    }
}
