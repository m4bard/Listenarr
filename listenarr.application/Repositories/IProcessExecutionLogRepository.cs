using Listenarr.Domain.Models;

namespace Listenarr.Application.Repositories
{
    public interface IProcessExecutionLogRepository
    {
        Task AddAsync(ProcessExecutionLog log, CancellationToken ct = default);
        Task<List<ProcessExecutionLog>> GetRecentAsync(int limit, CancellationToken ct = default);
    }
}
