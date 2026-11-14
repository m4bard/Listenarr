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

using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Downloads.Submission
{
    /// <summary>
    /// What the first torrent fetch ended with.
    /// </summary>
    /// <param name="Response">The last response, which the caller disposes.</param>
    /// <param name="MamId">The session id after any update a response carried.</param>
    /// <param name="Abandoned">True when the fetch stopped on a redirect with no target.</param>
    public sealed record MyAnonamouseTorrentFetch(HttpResponseMessage? Response, string MamId, bool Abandoned);

    /// <summary>
    /// Sends the requests that fetch a MyAnonamouse torrent. Every request is one gate call.
    /// </summary>
    public sealed class MyAnonamouseTorrentFetcher
    {
        private const int MaxHops = 6;

        private readonly IIndexerRepository _indexerRepository;
        private readonly IIndexerRequestGate _requestGate;
        private readonly ILogger<MyAnonamouseTorrentFetcher> _logger;

        public MyAnonamouseTorrentFetcher(
            IIndexerRepository indexerRepository,
            IIndexerRequestGate requestGate,
            ILogger<MyAnonamouseTorrentFetcher> logger)
        {
            _indexerRepository = indexerRepository;
            _requestGate = requestGate;
            _logger = logger;
        }

        public async Task<MyAnonamouseTorrentFetch> FetchAsync(
            SearchResult searchResult,
            Indexer indexer,
            Uri torrentUri,
            Uri indexerUri,
            string mamId,
            HttpClient client,
            CancellationToken cancellationToken)
        {
            var currentUri = torrentUri;
            HttpResponseMessage? response = null;
            for (int redirectAttempt = 0; redirectAttempt < MaxHops; redirectAttempt++)
            {
                using var req = BuildTorrentRequest(currentUri, indexerUri, mamId);

                _logger.LogDebug("Downloading MyAnonamouse torrent for '{Title}' from {Url} (attempt {Attempt})", searchResult.Title, LogRedaction.SanitizeUrl(currentUri.ToString()), redirectAttempt + 1);

                response = await SendThroughGateAsync(indexer, client, req, cancellationToken);
                mamId = await PersistUpdatedMamIdAsync(response, indexer, mamId);

                if (IsRedirect(response))
                {
                    if (response.Headers.Location == null)
                    {
                        _logger.LogWarning("MyAnonamouse torrent download redirect without Location header for '{Title}'", searchResult.Title);
                        response.Dispose();
                        return new MyAnonamouseTorrentFetch(null, mamId, true);
                    }

                    var next = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(currentUri, response.Headers.Location);
                    _logger.LogDebug("Following MyAnonamouse redirect to {Next}", LogRedaction.SanitizeUrl(next.ToString()));
                    response.Dispose();
                    currentUri = next;
                    continue;
                }

                break;
            }

            return new MyAnonamouseTorrentFetch(response, mamId, false);
        }

        public async Task<HttpResponseMessage?> FetchWithClientAsync(
            Indexer indexer,
            HttpClient client,
            Uri torrentUri,
            Uri indexerUri,
            string mamId,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage? response = null;
            var retryUri = torrentUri;
            for (int retryHop = 0; retryHop < MaxHops; retryHop++)
            {
                using var retryReq = BuildTorrentRequest(retryUri, indexerUri, mamId);
                response = await SendThroughGateAsync(indexer, client, retryReq, cancellationToken);

                if (IsRedirect(response) && response.Headers.Location != null)
                {
                    retryUri = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(retryUri, response.Headers.Location);
                    response.Dispose();
                    response = null;
                    continue;
                }

                break;
            }

            return response;
        }

        private async Task<HttpResponseMessage> SendThroughGateAsync(
            Indexer indexer,
            HttpClient client,
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var result = await IndexerHttpDispatch.SendAsync(
                _requestGate,
                new IndexerRequestContext(indexer.Id, SearchOrigin.Interactive, IndexerRequestKind.DownloadFetch),
                client,
                request,
                null,
                cancellationToken);

            return result switch
            {
                IndexerRequestResult.Sent sent => sent.Response,
                IndexerRequestResult.Deferred deferred => throw new IndexerRequestDeferredException(deferred.Deferral),
                _ => throw new InvalidOperationException("Unknown gate result.")
            };
        }

        private static HttpRequestMessage BuildTorrentRequest(Uri uri, Uri indexerUri, string? mamId)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            request.Headers.Referrer = new Uri("https://www.myanonamouse.net/");
            request.Headers.Accept.ParseAdd("application/x-bittorrent, application/octet-stream, */*; q=0.01");
            if (!string.IsNullOrEmpty(mamId))
            {
                request.Headers.Add("Cookie", $"mam_id={mamId}");
            }

            request.Headers.Host = indexerUri.IsDefaultPort ? indexerUri.Host : $"{indexerUri.Host}:{indexerUri.Port}";
            return request;
        }

        private async Task<string> PersistUpdatedMamIdAsync(HttpResponseMessage response, Indexer indexer, string mamId)
        {
            try
            {
                var newMam = MyAnonamouseHelper.TryExtractMamIdFromResponse(response);
                if (!string.IsNullOrEmpty(newMam) && !string.Equals(newMam, mamId, StringComparison.Ordinal))
                {
                    _logger.LogInformation("MyAnonamouse: received updated mam_id from download redirect response for indexer {Name}", indexer.Name);
                    indexer.AdditionalSettings = MyAnonamouseHelper.UpdateMamIdInAdditionalSettings(indexer.AdditionalSettings, newMam);
                    await _indexerRepository.UpdateAsync(indexer);
                    indexer.AdditionalSettings = MyAnonamouseHelper.UpdateMamIdInAdditionalSettings(indexer.AdditionalSettings, newMam);
                    return newMam;
                }
            }
            catch (Exception exMam) when (exMam is not OperationCanceledException && exMam is not OutOfMemoryException && exMam is not StackOverflowException)
            {
                _logger.LogDebug(exMam, "Failed to persist updated mam_id from MyAnonamouse redirect response");
            }

            return mamId;
        }

        private static bool IsRedirect(HttpResponseMessage response)
        {
            return response.StatusCode is
                System.Net.HttpStatusCode.MovedPermanently or
                System.Net.HttpStatusCode.Found or
                System.Net.HttpStatusCode.SeeOther or
                System.Net.HttpStatusCode.TemporaryRedirect or
                System.Net.HttpStatusCode.PermanentRedirect;
        }
    }
}
