using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Listenarr.Domain.Models;

namespace Listenarr.Application.Repositories
{
    public interface IRemotePathMappingRepository
    {
        Task<List<RemotePathMapping>> GetAllAsync(CancellationToken ct = default);
        Task<RemotePathMapping?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<List<RemotePathMapping>> GetByClientAsync(string downloadClientId, CancellationToken ct = default);
        Task<RemotePathMapping> SaveAsync(RemotePathMapping mapping, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
