using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Listenarr.Domain.Models;

namespace Listenarr.Application.Repositories
{
    public interface IAudiobookFileRepository
    {
        Task<AudiobookFile?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<List<AudiobookFile>> GetByAudiobookIdAsync(int audiobookId, CancellationToken ct = default);
        Task<List<AudiobookFile>> GetMissingMetadataAsync(int max, CancellationToken ct = default);
        Task<AudiobookFile> AddAsync(AudiobookFile file, CancellationToken ct = default);
        Task UpdateAsync(AudiobookFile file, CancellationToken ct = default);
        Task DeleteByAudiobookIdAsync(int audiobookId, CancellationToken ct = default);
        Task<bool> ExistsAtPathAsync(int audiobookId, string path, CancellationToken ct = default);
        Task<bool> IsPathUsedByOtherAsync(int audiobookId, string path, CancellationToken ct = default);
    }
}
