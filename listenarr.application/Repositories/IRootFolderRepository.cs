using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Listenarr.Domain.Models;

namespace Listenarr.Application.Repositories
{
    public interface IRootFolderRepository
    {
        Task<List<RootFolder>> GetAllAsync();
        Task<RootFolder?> GetByIdAsync(int id);
        Task<RootFolder?> GetByPathAsync(string path);
        Task AddAsync(RootFolder root);
        Task UpdateAsync(RootFolder root);
        Task RemoveAsync(int id);
        Task<RootFolder?> GetDefaultAsync();
        Task ClearDefaultExceptAsync(int? excludeId, CancellationToken ct = default);
        Task<bool> HasAudiobooksUnderPathAsync(string rootPath, CancellationToken ct = default);
        Task<List<Audiobook>> GetAudiobooksUnderPathAsync(string rootPath, CancellationToken ct = default);
        Task<List<(int audiobookId, string original, string target)>> MigrateAudiobookPathsAsync(string oldRootPath, string newRootPath, CancellationToken ct = default);
        Task SaveChangesAsync(CancellationToken ct = default);
    }
}
