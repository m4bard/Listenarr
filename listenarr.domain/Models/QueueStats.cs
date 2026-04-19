using System;

namespace Listenarr.Domain.Models
{
    public class QueueStats
    {
        public int PendingJobs { get; set; }
        public int ProcessingJobs { get; set; }
        public int CompletedJobs { get; set; }
        public int FailedJobs { get; set; }
        public int RetryJobs { get; set; }
        public int TotalJobs { get; set; }
        public DateTime? OldestPendingJob { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
