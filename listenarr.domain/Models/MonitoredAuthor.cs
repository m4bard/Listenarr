using System.ComponentModel.DataAnnotations;

namespace Listenarr.Domain.Models
{
    public class MonitoredAuthor
    {
        [Key]
        public int Id { get; set; }

        public string AuthorName { get; set; } = string.Empty;

        public string AuthorNameNormalized { get; set; } = string.Empty;

        public string? AuthorAsin { get; set; }

        public string Region { get; set; } = "us";

        public string Language { get; set; } = "all";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastCheckedAt { get; set; }

        public DateTime? LastSuccessfulSyncAt { get; set; }

        public string? LastError { get; set; }
    }
}
