using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Listenarr.Domain.Models;

namespace Listenarr.Application.Repositories
{
    public interface IDownloadClientConfigurationRepository
    {
        Task<List<DownloadClientConfiguration>> GetAllAsync(CancellationToken ct = default);
        Task<DownloadClientConfiguration?> GetByIdAsync(string id, CancellationToken ct = default);
        Task<DownloadClientConfiguration> SaveAsync(DownloadClientConfiguration config, CancellationToken ct = default);
        Task<bool> DeleteAsync(string id, CancellationToken ct = default);
    }
}
