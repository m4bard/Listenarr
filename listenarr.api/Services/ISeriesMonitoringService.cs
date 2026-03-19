using Listenarr.Domain.Models;

namespace Listenarr.Api.Services
{
    public interface ISeriesMonitoringService
    {
        Task<MonitoredSeries?> GetMonitoredSeriesAsync(
            string name,
            string region,
            string language,
            CancellationToken cancellationToken = default);

        Task<MonitorSeriesOperationResult> MonitorSeriesAsync(
            MonitorSeriesRequest request,
            CancellationToken cancellationToken = default);

        Task<bool> UnmonitorSeriesAsync(int id, CancellationToken cancellationToken = default);

        Task<MonitorSeriesSyncResult> SyncSeriesAsync(int id, CancellationToken cancellationToken = default);

        Task<int> SyncDueSeriesAsync(CancellationToken cancellationToken = default);
    }

    public sealed class MonitorSeriesRequest
    {
        public string Name { get; set; } = string.Empty;

        public string? Asin { get; set; }

        public string Region { get; set; } = "us";

        public string Language { get; set; } = "all";
    }

    public sealed class MonitorSeriesOperationResult
    {
        public MonitoredSeries? MonitoredSeries { get; set; }

        public MonitorSeriesSyncResult SyncResult { get; set; } = new();
    }

    public sealed class MonitorSeriesSyncResult
    {
        public int AddedCount { get; set; }

        public int ExistingCount { get; set; }

        public int FailedCount { get; set; }

        public bool Succeeded { get; set; }

        public string? ErrorMessage { get; set; }
    }
}
