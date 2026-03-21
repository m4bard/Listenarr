namespace Listenarr.Domain.Models
{
    public class ProwlarrImportConnectionSettings
    {
        public string Url { get; set; } = string.Empty;
        public int? Port { get; set; }
        public string? ApiKey { get; set; }
        public string? TagFilter { get; set; }
        public bool HasSavedApiKey { get; set; }
    }
}
