/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Downloads.Submission
{
    public sealed class MyAnonamouseTorrentPreparationService
    {
        private readonly IIndexerRepository _indexerRepository;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly MyAnonamouseTorrentFetcher _fetcher;
        private readonly DownloadCachedTorrentStore _cachedTorrentStore;
        private readonly ILogger<MyAnonamouseTorrentPreparationService> _logger;

        public MyAnonamouseTorrentPreparationService(
            IIndexerRepository indexerRepository,
            IHttpClientFactory httpClientFactory,
            MyAnonamouseTorrentFetcher fetcher,
            DownloadCachedTorrentStore cachedTorrentStore,
            ILogger<MyAnonamouseTorrentPreparationService> logger)
        {
            _indexerRepository = indexerRepository;
            _httpClientFactory = httpClientFactory;
            _fetcher = fetcher;
            _cachedTorrentStore = cachedTorrentStore;
            _logger = logger;
        }

        public async Task PrepareAsync(
            SearchResult searchResult,
            string? downloadId = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(searchResult);

            _logger.LogInformation("TryPrepareMyAnonamouseTorrentAsync called for '{Title}', IndexerId: {IndexerId}, TorrentUrl: '{TorrentUrl}'",
                LogRedaction.SanitizeText(searchResult.Title), searchResult.IndexerId, LogRedaction.SanitizeUrl(searchResult.TorrentUrl));

            if (searchResult.IndexerId == null)
            {
                _logger.LogWarning("TryPrepareMyAnonamouseTorrentAsync: No IndexerId for '{Title}' - skipping", searchResult.Title);
                return;
            }

            if (string.IsNullOrEmpty(searchResult.TorrentUrl))
            {
                _logger.LogDebug("Skipping MyAnonamouse cache: no TorrentUrl for '{Title}'", LogRedaction.SanitizeText(searchResult.Title));
                return;
            }

            if (searchResult.TorrentFileContent != null && searchResult.TorrentFileContent.Length > 0)
            {
                _logger.LogDebug("MyAnonamouse torrent already cached for '{Title}'", searchResult.Title);
                return;
            }

            try
            {
                var indexer = await _indexerRepository.GetByIdAsync(searchResult.IndexerId.Value);
                if (indexer == null)
                {
                    _logger.LogWarning("Unable to cache MyAnonamouse torrent for '{Title}': indexer configuration not found", searchResult.Title);
                    return;
                }

                if (!string.Equals(indexer.Implementation, "MyAnonamouse", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("Skipping MyAnonamouse cache: indexer {IndexerName} is not MyAnonamouse (is {Implementation})",
                        indexer.Name, indexer.Implementation);
                    return;
                }

                if (!Uri.TryCreate(searchResult.TorrentUrl, UriKind.Absolute, out var torrentUri) ||
                    !Uri.TryCreate(indexer.Url, UriKind.Absolute, out var indexerUri))
                {
                    _logger.LogWarning("Unable to cache MyAnonamouse torrent for '{Title}': invalid URL(s). Torrent={Url}, Indexer={IndexerUrl}", searchResult.Title, LogRedaction.SanitizeUrl(searchResult.TorrentUrl), indexer.Url);
                    return;
                }

                if (!string.Equals(torrentUri.Host, indexerUri.Host, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("MyAnonamouse torrent host {TorrentHost} differs from indexer host {IndexerHost}. Proceeding with explicit cookie header.", torrentUri.Host, indexerUri.Host);
                }

                var mamId = MyAnonamouseHelper.TryGetMamId(indexer.AdditionalSettings);
                if (string.IsNullOrEmpty(mamId))
                {
                    _logger.LogWarning("Unable to cache MyAnonamouse torrent for '{Title}': mam_id missing from indexer {IndexerName}", searchResult.Title, indexer.Name);
                    return;
                }

                var httpClientToUse = _httpClientFactory.CreateClient();

                _logger.LogDebug("Downloading MyAnonamouse torrent for '{Title}' from {Url}", searchResult.Title, LogRedaction.SanitizeUrl(searchResult.TorrentUrl));

                var fetched = await _fetcher.FetchAsync(searchResult, indexer, torrentUri, indexerUri, mamId, httpClientToUse, cancellationToken);
                var response = fetched.Response;
                mamId = fetched.MamId;
                if (fetched.Abandoned)
                {
                    return;
                }

                if (response == null)
                {
                    _logger.LogWarning("Failed to download MyAnonamouse torrent for '{Title}': no response", searchResult.Title);
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("MyAnonamouse torrent download failed for '{Title}' with status {Status}", searchResult.Title, response.StatusCode);
                    response.Dispose();
                    return;
                }

                var torrentBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (torrentBytes == null || torrentBytes.Length == 0)
                {
                    _logger.LogWarning("MyAnonamouse torrent download for '{Title}' returned empty payload", searchResult.Title);
                    response.Dispose();
                    return;
                }

                var looksLikeTorrent = LooksLikeTorrent(torrentBytes);
                if (!looksLikeTorrent)
                {
                    _logger.LogDebug("Factory client returned non-torrent payload for '{Title}', retrying with authenticated MAM client", searchResult.Title);
                    response.Dispose();
                    response = null;

                    try
                    {
                        using var authClient = MyAnonamouseHelper.CreateAuthenticatedHttpClient(mamId, indexer.Url);
                        response = await _fetcher.FetchWithClientAsync(indexer, authClient, torrentUri, indexerUri, mamId, cancellationToken);

                        if (response != null && response.IsSuccessStatusCode)
                        {
                            torrentBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                            looksLikeTorrent = torrentBytes != null && torrentBytes.Length > 0 && LooksLikeTorrent(torrentBytes);
                            if (looksLikeTorrent)
                            {
                                _logger.LogInformation("Authenticated MAM client successfully downloaded torrent for '{Title}' ({Bytes} bytes)", searchResult.Title, torrentBytes!.Length);
                            }
                        }
                    }
                    catch (Exception retryEx) when (retryEx is not OperationCanceledException && retryEx is not IndexerRequestDeferredException && retryEx is not OutOfMemoryException && retryEx is not StackOverflowException)
                    {
                        _logger.LogDebug(retryEx, "Retry with authenticated MAM client also failed (non-fatal)");
                    }
                }

                if (!looksLikeTorrent)
                {
                    var snippet = System.Text.Encoding.UTF8.GetString((torrentBytes ?? Array.Empty<byte>()).Take(Math.Min(512, torrentBytes?.Length ?? 0)).ToArray());
                    if (Regex.IsMatch(snippet, "Unrecognized host|PassKey|Pass Key|Unrecognized", RegexOptions.IgnoreCase))
                    {
                        _logger.LogWarning("MyAnonamouse torrent download for '{Title}' returned an authorization error page from tracker: {Snippet}", searchResult.Title, LogRedaction.RedactText(snippet, LogRedaction.GetSensitiveValuesFromEnvironment()));
                    }
                    else
                    {
                        _logger.LogWarning("MyAnonamouse torrent download for '{Title}' returned unexpected non-torrent payload (first 200 chars): {Snippet}", searchResult.Title, LogRedaction.RedactText(snippet, LogRedaction.GetSensitiveValuesFromEnvironment()));
                    }

                    response?.Dispose();
                    return;
                }

                if (torrentBytes == null)
                {
                    return;
                }

                LogTorrentPayloadDebug(searchResult.Title, response, torrentBytes);
                torrentBytes = RewriteTrackerHosts(searchResult.Title, torrentBytes, torrentUri, indexerUri);
                torrentBytes = AppendMamIdToAnnounces(searchResult.Title, torrentBytes, mamId);

                searchResult.TorrentFileContent = torrentBytes;
                searchResult.TorrentFileName = response != null ? MyAnonamouseHelper.ResolveTorrentFileName(response, searchResult.TorrentUrl) : "myanonamouse.torrent";
                _logger.LogInformation("Cached MyAnonamouse torrent for '{Title}' ({Bytes} bytes)", searchResult.Title, torrentBytes.Length);

                CachePreparedTorrent(searchResult.Title, torrentBytes, searchResult.TorrentFileName, downloadId);
                response?.Dispose();
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not IndexerRequestDeferredException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogWarning(ex, "Failed to cache MyAnonamouse torrent for '{Title}'", searchResult.Title);
            }
        }

        private static bool LooksLikeTorrent(byte[] torrentBytes)
        {
            return (torrentBytes.Length > 0 && torrentBytes[0] == (byte)'d') ||
                System.Text.Encoding.ASCII.GetString(torrentBytes.Take(Math.Min(200, torrentBytes.Length)).ToArray())
                    .IndexOf("announce", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void LogTorrentPayloadDebug(string title, HttpResponseMessage? response, byte[] torrentBytes)
        {
            var contentType = response?.Content.Headers.ContentType?.ToString() ?? "(none)";
            var firstBytesHex = BitConverter.ToString(torrentBytes.Take(Math.Min(16, torrentBytes.Length)).ToArray()).Replace("-", " ");
            var containsAnnounce = System.Text.Encoding.ASCII.GetString(torrentBytes.Take(Math.Min(512, torrentBytes.Length)).ToArray()).IndexOf("announce", StringComparison.OrdinalIgnoreCase) >= 0;
            _logger.LogDebug("MyAnonamouse torrent payload debug: ContentType={ContentType}, FirstBytes={FirstBytesHex}, ContainsAnnounce={ContainsAnnounce}", contentType, firstBytesHex, containsAnnounce);
        }

        private byte[] RewriteTrackerHosts(string title, byte[] torrentBytes, Uri torrentUri, Uri indexerUri)
        {
            try
            {
                if (string.IsNullOrEmpty(indexerUri.Host))
                {
                    return torrentBytes;
                }

                var ascii = System.Text.Encoding.ASCII.GetString(torrentBytes);
                if (!string.IsNullOrEmpty(torrentUri.Host) &&
                    ascii.IndexOf(torrentUri.Host, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    !string.Equals(torrentUri.Host, indexerUri.Host, StringComparison.OrdinalIgnoreCase))
                {
                    var replaced = MyAnonamouseHelper.ReplaceHostInTorrent(torrentBytes, torrentUri.Host, indexerUri.Host);
                    if (replaced != null && replaced.Length > 0)
                    {
                        torrentBytes = replaced;
                        _logger.LogInformation("Rewrote torrent tracker host from {OldHost} to {NewHost} for '{Title}'", torrentUri.Host, indexerUri.Host, title);
                        ascii = System.Text.Encoding.ASCII.GetString(torrentBytes);
                    }
                }

                try
                {
                    var ipMatches = Regex.Matches(ascii, @"\b\d{1,3}(?:\.\d{1,3}){3}\b");
                    var distinctIps = ipMatches.Cast<Match>().Select(match => match.Value).Distinct().ToList();
                    foreach (var ip in distinctIps.Where(ip =>
                        !ip.StartsWith("127.")
                        && !ip.StartsWith("10.")
                        && !ip.StartsWith("192.168.")
                        && !ip.StartsWith("172.")
                        && !string.Equals(ip, indexerUri.Host, StringComparison.OrdinalIgnoreCase)))
                    {
                        var replaced = MyAnonamouseHelper.ReplaceHostInTorrent(torrentBytes, ip, indexerUri.Host);
                        if (replaced != null && replaced.Length > 0)
                        {
                            torrentBytes = replaced;
                            _logger.LogInformation("Rewrote torrent IP host {Ip} to indexer host {Host} for '{Title}'", ip, indexerUri.Host, title);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    _logger.LogDebug(ex, "Failed to rewrite numeric IPs inside torrent (non-fatal)");
                }

                try
                {
                    var announces = MyAnonamouseHelper.ExtractAnnounceUrls(torrentBytes);
                    if (announces != null && announces.Count > 0)
                    {
                        _logger.LogDebug("Torrent announce URLs for '{Title}': {Announces}", title, string.Join(", ", announces.Distinct()));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    _logger.LogDebug(ex, "Failed to extract announce URLs from torrent (non-fatal)");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogDebug(ex, "Failed to rewrite torrent tracker hosts (non-fatal)");
            }

            return torrentBytes;
        }

        private byte[] AppendMamIdToAnnounces(string title, byte[] torrentBytes, string? mamId)
        {
            try
            {
                if (string.IsNullOrEmpty(mamId))
                {
                    return torrentBytes;
                }

                var normalizedMamId = MyAnonamouseHelper.NormalizeMamId(mamId);
                _logger.LogInformation("MyAnonamouse: normalizing mam_id from '{Raw}' to '{Normalized}' for '{Title}'", LogRedaction.RedactText(mamId, LogRedaction.GetSensitiveValuesFromEnvironment()), LogRedaction.RedactText(normalizedMamId, LogRedaction.GetSensitiveValuesFromEnvironment()), title);

                var currentAnnounces = MyAnonamouseHelper.ExtractAnnounceUrls(torrentBytes);
                var updatedAnnounces = new List<string>();
                var modified = false;

                foreach (var announce in (currentAnnounces ?? new List<string>())
                    .Where(announce => !string.IsNullOrWhiteSpace(announce))
                    .Distinct())
                {
                    if (!announce.Contains("/announce", StringComparison.OrdinalIgnoreCase) && !announce.Contains("/tracker", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Skipping non-tracker URL for mam_id append: {Url}", announce);
                        continue;
                    }

                    if (announce.IndexOf("mam_id=", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        updatedAnnounces.Add(announce);
                        continue;
                    }

                    try
                    {
                        var separator = announce.Contains("?") ? "&" : "?";
                        var newAnnounce = announce + separator + "mam_id=" + normalizedMamId;

                        var replaced = MyAnonamouseHelper.ReplaceStringInTorrent(torrentBytes, announce, newAnnounce);
                        if (replaced != null && replaced.Length > 0)
                        {
                            torrentBytes = replaced;
                            modified = true;
                        }

                        updatedAnnounces.Add(newAnnounce);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                    {
                        _logger.LogDebug(ex, "Non-fatal failure while attempting to append mam_id to announce {Ann} for '{Title}'", announce, title);
                        updatedAnnounces.Add(announce);
                    }
                }

                if (modified)
                {
                    _logger.LogInformation("Appended mam_id to MyAnonamouse announce URLs for '{Title}' - count={Count}", title, updatedAnnounces.Count);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogDebug(ex, "Failed to append mam_id to MyAnonamouse announces (non-fatal)");
            }

            return torrentBytes;
        }

        private void CachePreparedTorrent(string title, byte[] torrentBytes, string? fileName, string? downloadId)
        {
            if (!string.IsNullOrEmpty(downloadId))
            {
                try
                {
                    _cachedTorrentStore.CacheTorrent(downloadId, torrentBytes, fileName ?? "download.torrent");
                    _logger.LogInformation("Cached MyAnonamouse torrent bytes and filename to memory for download {DownloadId}", downloadId);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    _logger.LogDebug(ex, "Failed to place cached MyAnonamouse torrent into memory cache (non-fatal)");
                }
            }

            try
            {
                var announces = MyAnonamouseHelper.ExtractAnnounceUrls(torrentBytes);
                _cachedTorrentStore.LogCachedAnnounces(title, announces);

                if (!string.IsNullOrEmpty(downloadId) && announces != null && announces.Count > 0)
                {
                    try
                    {
                        _cachedTorrentStore.CacheAnnounces(downloadId, announces);
                        _logger.LogInformation("Cached MyAnonamouse torrent announces to memory for download {DownloadId}", downloadId);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                    {
                        _logger.LogDebug(ex, "Failed to place cached MyAnonamouse announces into memory cache (non-fatal)");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogDebug(ex, "Failed to extract announce URLs from cached torrent (non-fatal)");
            }
        }
    }
}
