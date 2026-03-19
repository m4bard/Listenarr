using System.ComponentModel.DataAnnotations;

namespace Listenarr.Domain.Models
{
    public class MonitoredSeries
    {
        [Key]
        public int Id { get; set; }

        public string SeriesName { get; set; } = string.Empty;

        public string SeriesNameNormalized { get; set; } = string.Empty;

        public string? SeriesAsin { get; set; }

        public string Region { get; set; } = "us";

        public string Language { get; set; } = "all";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastCheckedAt { get; set; }

        public DateTime? LastSuccessfulSyncAt { get; set; }

        public string? LastError { get; set; }
    }
}
