namespace Listenarr.Api.Services
{
    public interface ISeriesCatalogService
    {
        Task<SeriesCatalogFetchResult?> GetCatalogAsync(
            string name,
            string region = "us",
            int limit = 250,
            string? language = null,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default);
    }

    public sealed class SeriesCatalogFetchResult
    {
        public SeriesLookupItem Series { get; set; } = new();

        public List<AudibleSearchResult> Books { get; set; } = new();

        public int TotalBooks => Books.Count;
    }
}
