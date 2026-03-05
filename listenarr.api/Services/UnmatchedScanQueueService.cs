using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Listenarr.Api.Services
{
    public class UnmatchedFileResult
    {
        public string FullPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string BookFolder { get; set; } = string.Empty;
        public long Size { get; set; }
        public int FileCount { get; set; }
        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? Series { get; set; }
        public string? SeriesNumber { get; set; }
        public string? Year { get; set; }
        public string? Narrator { get; set; }
        public string? Description { get; set; }
        public string? CoverPath { get; set; }
        public string? Asin { get; set; }
        public string Format { get; set; } = string.Empty;
    }

    public class UnmatchedScanJob
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string RootFolderPath { get; set; } = string.Empty;
        public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public string Status { get; set; } = "Queued";
        public string? Error { get; set; }
        public List<UnmatchedFileResult>? Results { get; set; }
    }

    public interface IUnmatchedScanQueueService
    {
        Task<Guid> EnqueueAsync(string rootFolderPath);
        bool TryGetJob(Guid id, out UnmatchedScanJob? job);
        void UpdateJob(Guid id, string status, List<UnmatchedFileResult>? results = null, string? error = null);
        bool TryGetLastJobForPath(string rootFolderPath, out UnmatchedScanJob? job);
        ChannelReader<UnmatchedScanJob> Reader { get; }
    }

    public class UnmatchedScanQueueService : IUnmatchedScanQueueService
    {
        private readonly ConcurrentDictionary<Guid, UnmatchedScanJob> _jobs = new();
        private readonly ConcurrentDictionary<string, Guid> _lastJobByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Channel<UnmatchedScanJob> _channel = Channel.CreateUnbounded<UnmatchedScanJob>();
        private readonly ILogger<UnmatchedScanQueueService> _logger;

        public UnmatchedScanQueueService(ILogger<UnmatchedScanQueueService> logger)
        {
            _logger = logger;
        }

        public ChannelReader<UnmatchedScanJob> Reader => _channel.Reader;

        public async Task<Guid> EnqueueAsync(string rootFolderPath)
        {
            // Dedupe: if a queued/processing job exists for the same path, return it
            var existing = _jobs.Values.FirstOrDefault(j =>
                string.Equals(j.RootFolderPath, rootFolderPath, StringComparison.OrdinalIgnoreCase) &&
                (j.Status == "Queued" || j.Status == "Processing"));

            if (existing != null)
            {
                _logger.LogInformation("Deduping unmatched scan job {JobId} for path {Path}", existing.Id, rootFolderPath);
                return existing.Id;
            }

            var job = new UnmatchedScanJob { RootFolderPath = rootFolderPath };
            _jobs[job.Id] = job;
            _logger.LogInformation("Enqueueing unmatched scan job {JobId} for {Path}", job.Id, rootFolderPath);
            await _channel.Writer.WriteAsync(job);
            return job.Id;
        }

        public bool TryGetJob(Guid id, out UnmatchedScanJob? job) => _jobs.TryGetValue(id, out job);

        public void UpdateJob(Guid id, string status, List<UnmatchedFileResult>? results = null, string? error = null)
        {
            if (!_jobs.TryGetValue(id, out var job)) return;
            job.Status = status;
            job.Error = error;
            if (results != null) job.Results = results;
            if (status == "Completed")
            {
                job.CompletedAt = DateTime.UtcNow;
                _lastJobByPath[job.RootFolderPath] = id;
            }
            _jobs[id] = job;
        }

        public bool TryGetLastJobForPath(string rootFolderPath, out UnmatchedScanJob? job)
        {
            job = null;
            if (!_lastJobByPath.TryGetValue(rootFolderPath, out var jobId)) return false;
            return _jobs.TryGetValue(jobId, out job);
        }
    }
}
