using Listenarr.Domain.Models;

namespace Listenarr.Application.Audiobooks
{
    public interface IAudiobookPathPreviewService
    {
        Task<PathPreviewResult> PreviewAsync(
            Audiobook audiobook,
            string? destinationRoot = null,
            CancellationToken ct = default);
    }
}
