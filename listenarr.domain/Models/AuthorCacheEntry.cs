using System.ComponentModel.DataAnnotations;

namespace Listenarr.Domain.Models
{
    public class AuthorCacheEntry
    {
        [Key]
        public int Id { get; set; }

        public string AuthorName { get; set; } = string.Empty;

        public string AuthorNameNormalized { get; set; } = string.Empty;

        public string? AuthorAsin { get; set; }

        public string Region { get; set; } = "us";

        public string? ImageUrl { get; set; }

        public string? Description { get; set; }

        public List<CachedRelatedAuthor>? SimilarAuthors { get; set; } = new();

        public List<CachedAuthorCatalogBook>? CatalogBooks { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastFetchedAt { get; set; }
    }

    public class CachedRelatedAuthor
    {
        public string? Asin { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class CachedAuthorCatalogBook
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
