using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Listenarr.Domain.Models;

namespace Listenarr.Application.Repositories
{
    public interface IIndexerRepository
    {
        Task<Indexer?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<Indexer?> GetByNameAsync(string name, CancellationToken ct = default);
        Task<List<Indexer>> GetAllAsync(CancellationToken ct = default);
        Task<List<Indexer>> GetEnabledAsync(bool isAutomaticSearch, CancellationToken ct = default);
        Task<Indexer> AddAsync(Indexer indexer, CancellationToken ct = default);
        Task UpdateAsync(Indexer indexer, CancellationToken ct = default);
        Task DeleteAsync(int id, CancellationToken ct = default);
    }
}
