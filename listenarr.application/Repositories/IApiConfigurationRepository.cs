using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Listenarr.Domain.Models;

namespace Listenarr.Application.Repositories
{
    public interface IApiConfigurationRepository
    {
        Task<List<ApiConfiguration>> GetAllAsync(CancellationToken ct = default);
        Task<ApiConfiguration?> GetByIdAsync(string id, CancellationToken ct = default);
        Task<ApiConfiguration> SaveAsync(ApiConfiguration config, CancellationToken ct = default);
        Task<bool> DeleteAsync(string id, CancellationToken ct = default);
    }
}
