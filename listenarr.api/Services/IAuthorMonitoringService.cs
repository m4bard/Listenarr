using Listenarr.Domain.Models;

namespace Listenarr.Api.Services
{
    public interface IAuthorMonitoringService
    {
        Task<MonitoredAuthor?> GetMonitoredAuthorAsync(
            string name,
            string region,
            string language,
            CancellationToken cancellationToken = default);

        Task<MonitorAuthorOperationResult> MonitorAuthorAsync(
            MonitorAuthorRequest request,
            CancellationToken cancellationToken = default);

        Task<bool> UnmonitorAuthorAsync(int id, CancellationToken cancellationToken = default);

        Task<MonitorAuthorSyncResult> SyncAuthorAsync(int id, CancellationToken cancellationToken = default);

        Task<int> SyncDueAuthorsAsync(CancellationToken cancellationToken = default);
    }

    public sealed class MonitorAuthorRequest
    {
        public string Name { get; set; } = string.Empty;

        public string? Asin { get; set; }

        public string Region { get; set; } = "us";

        public string Language { get; set; } = "all";
    }

    public sealed class MonitorAuthorOperationResult
    {
        public MonitoredAuthor? MonitoredAuthor { get; set; }

        public MonitorAuthorSyncResult SyncResult { get; set; } = new();
    }

    public sealed class MonitorAuthorSyncResult
    {
        public int AddedCount { get; set; }

        public int ExistingCount { get; set; }

        public int FailedCount { get; set; }

        public bool Succeeded { get; set; }

        public string? ErrorMessage { get; set; }
    }
}
