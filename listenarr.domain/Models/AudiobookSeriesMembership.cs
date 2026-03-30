using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Listenarr.Domain.Models
{
    public class AudiobookSeriesMembership
    {
        [Key]
        public int Id { get; set; }

        public int AudiobookId { get; set; }

        [JsonIgnore]
        public Audiobook? Audiobook { get; set; }

        public string? SeriesName { get; set; }

        public string? SeriesNumber { get; set; }

        public string? SeriesAsin { get; set; }

        public bool IsPrimary { get; set; }

        public int SortOrder { get; set; }
    }
}
