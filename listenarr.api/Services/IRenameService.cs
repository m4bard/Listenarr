using Listenarr.Api.Models;

namespace Listenarr.Api.Services
{
    public interface IRenameService
    {
        Task<List<RenamePreview>> PreviewRenameAsync(int[] audiobookIds, CancellationToken ct = default);
        Task<List<RenameResult>> ExecuteRenameAsync(List<RenameOperation> operations, CancellationToken ct = default);
    }
}
