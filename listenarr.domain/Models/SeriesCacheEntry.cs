using System.ComponentModel.DataAnnotations;

namespace Listenarr.Domain.Models
{
    public class SeriesCacheEntry
    {
        [Key]
        public int Id { get; set; }

        public string SeriesName { get; set; } = string.Empty;

        public string SeriesNameNormalized { get; set; } = string.Empty;

        public string? SeriesAsin { get; set; }

        public string Region { get; set; } = "us";

        public string? ImageUrl { get; set; }

        public string? Description { get; set; }

        public List<CachedSeriesCatalogBook>? CatalogBooks { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastFetchedAt { get; set; }
    }

    public class CachedSeriesCatalogBook
    {
        public string? Asin { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? Subtitle { get; set; }

        public List<string> Authors { get; set; } = new();

        public string? ImageUrl { get; set; }

        public int? Runtime { get; set; }

        public string? Language { get; set; }

        public string? Publisher { get; set; }

        public List<string> Narrators { get; set; } = new();

        public List<string> Genres { get; set; } = new();

        public string? Series { get; set; }

        public string? SeriesNumber { get; set; }

        public string? PublishedDate { get; set; }

        public string? Isbn { get; set; }

        public string? Link { get; set; }

        public string? MetadataSource { get; set; }
    }
}
