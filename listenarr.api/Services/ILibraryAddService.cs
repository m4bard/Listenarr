using Listenarr.Domain.Models;

namespace Listenarr.Api.Services
{
    public interface ILibraryAddService
    {
        Task<LibraryAddOperationResult> AddToLibraryAsync(
            LibraryAddOperationRequest request,
            CancellationToken cancellationToken = default);
    }

    public sealed class LibraryAddOperationRequest
    {
        public AudibleBookMetadata Metadata { get; set; } = new();

        public bool Monitored { get; set; } = true;

        public int? QualityProfileId { get; set; }

        public bool AutoSearch { get; set; }

        public string? DestinationPath { get; set; }

        public SearchResult? SearchResult { get; set; }

        public string HistorySource { get; set; } = "AddNew";

        public string? HistoryMessage { get; set; }
    }

    public sealed class LibraryAddOperationResult
    {
        public bool Added { get; set; }

        public bool AlreadyExists { get; set; }

        public string Message { get; set; } = string.Empty;

        public Audiobook? Audiobook { get; set; }
    }
}
