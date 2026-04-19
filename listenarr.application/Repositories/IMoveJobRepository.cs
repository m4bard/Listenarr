using Listenarr.Domain.Models;

namespace Listenarr.Application.Repositories
{
    public interface IMoveJobRepository
    {
        Task<MoveJob?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<List<MoveJob>> GetByStatusAsync(IEnumerable<string> statuses, CancellationToken ct = default);
        Task<MoveJob> AddAsync(MoveJob job, CancellationToken ct = default);
        Task UpdateAsync(MoveJob job, CancellationToken ct = default);
    }
}
