namespace Listenarr.Infrastructure.Torrents
{
    /// <summary>
    /// Shared helper that pre-downloads .torrent files from HTTP(S) URLs,
    /// manually following redirects. This avoids relying on a download client's
    /// built-in HTTP client, which may not handle redirects from indexers
    /// (e.g. Prowlarr returning 301).
    /// </summary>
    public interface ITorrentFileDownloader
    {
        /// <summary>
        /// Downloads a .torrent file from the given URL, following up to 10 redirects.
        /// If the indexer redirects to a magnet link, the result will contain the magnet URI.
        /// Returns an empty result if the download fails. Every request goes through the indexer request
        /// gate; a request the gate defers throws <see cref="Listenarr.Application.Search.Contracts.IndexerRequestDeferredException"/>.
        /// A null or non-positive <paramref name="indexerId"/> is unattributed.
        /// </summary>
        Task<TorrentDownloadResult> DownloadAsync(string torrentUrl, int? indexerId, CancellationToken ct = default);
    }
}
