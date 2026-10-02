/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.DownloadClients.Qbittorrent
{
    internal sealed class QbittorrentRemovalWorkflow
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger _logger;
        private readonly string _clientType;

        public QbittorrentRemovalWorkflow(
            IHttpClientFactory httpClientFactory,
            ILogger logger,
            string clientType)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _clientType = clientType;
        }

        public async Task<bool> RemoveAsync(DownloadClientConfiguration client, string id, bool deleteFiles = false, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            var baseUrl = DownloadClientUriBuilder.BuildAuthority(client);

            try
            {
                using var httpClient = _httpClientFactory.CreateClient(_clientType);
                using var loginData = QbittorrentCookieSession.CreateLoginContent(client);

                using var loginResp = await httpClient.PostAsync($"{baseUrl}/api/v2/auth/login", loginData, ct);
                if (!loginResp.IsSuccessStatusCode)
                {
                    if (loginResp.StatusCode == HttpStatusCode.Forbidden)
                    {
                        // 403 may mean auth is disabled — probe a version endpoint to confirm
                        using var testResp = await httpClient.GetAsync($"{baseUrl}/api/v2/app/version", ct);
                        if (!testResp.IsSuccessStatusCode)
                        {
                            _logger.LogWarning("qBittorrent auth appears enabled and credentials are invalid for client {ClientId}", client.Id);
                            return false;
                        }
                        // Auth is disabled; fall through to the delete call
                    }
                    else
                    {
                        _logger.LogWarning("qBittorrent login failed with status {Status} for client {ClientId}", loginResp.StatusCode, client.Id);
                        return false;
                    }
                }

                // qBittorrent's delete endpoint is unconditionally idempotent-success: it returns
                // HTTP 200 with an empty body whether or not the given hash is actually present,
                // so a bare success check there cannot tell a real removal from a no-op against a
                // hash qBittorrent never had. Ask first, and act on three distinct answers:
                //   - cannot tell (error status, unreadable body): fail closed, report not removed;
                //   - confirmed absent: the goal of removal already holds, whatever removed it
                //     (a share-limit rule, a manual delete), so report removed without deleting.
                //     Reporting false here would leave deferred cleanup retrying a torrent that
                //     can never be found, and retaining the record indefinitely;
                //   - present: delete as before.
                using var infoResp = await httpClient.GetAsync($"{baseUrl}/api/v2/torrents/info?hashes={Uri.EscapeDataString(id)}", ct);
                if (!infoResp.IsSuccessStatusCode)
                {
                    var infoBody = await infoResp.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("qBittorrent presence check returned {Status}: {Body}", infoResp.StatusCode, LogRedaction.RedactText(infoBody, LogRedaction.GetSensitiveValuesFromEnvironment()));
                    return false;
                }

                var infoJson = await infoResp.Content.ReadAsStringAsync(ct);
                var presence = ParsePresence(infoJson, id);
                if (presence == TorrentPresence.Unknown)
                {
                    _logger.LogWarning("qBittorrent presence check returned an unparseable response for torrent {Id}", LogRedaction.SanitizeText(id));
                    return false;
                }

                if (presence == TorrentPresence.Absent)
                {
                    _logger.LogInformation("Torrent {Id} was already absent from qBittorrent; treating it as removed", LogRedaction.SanitizeText(id));
                    return true;
                }

                using var deleteData = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("hashes", id),
                    new KeyValuePair<string, string>("deleteFiles", deleteFiles ? "true" : "false")
                });

                using var deleteResp = await httpClient.PostAsync($"{baseUrl}/api/v2/torrents/delete", deleteData, ct);
                if (!deleteResp.IsSuccessStatusCode)
                {
                    var body = await deleteResp.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("qBittorrent delete returned {Status}: {Body}", deleteResp.StatusCode, LogRedaction.RedactText(body, LogRedaction.GetSensitiveValuesFromEnvironment()));
                    return false;
                }

                _logger.LogInformation("Removed torrent {Id} from qBittorrent (deleteFiles={DeleteFiles})", LogRedaction.SanitizeText(id), deleteFiles);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Error removing torrent from qBittorrent: {Id}", LogRedaction.SanitizeText(id));
                return false;
            }
        }

        private enum TorrentPresence
        {
            Unknown,
            Absent,
            Present
        }

        // The info endpoint filtered by hashes returns a JSON array of torrent objects. A torrent
        // is present when an entry carries the requested id as its hash or as either info-hash
        // (hybrid and v2 torrents expose both). Anything that is not an array of objects with a
        // string hash is treated as unreadable rather than as absence.
        private static TorrentPresence ParsePresence(string json, string id)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return TorrentPresence.Unknown;
                }

                foreach (var entry in document.RootElement.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object
                        || !entry.TryGetProperty("hash", out var hash)
                        || hash.ValueKind != JsonValueKind.String)
                    {
                        return TorrentPresence.Unknown;
                    }

                    if (HashMatches(hash, id)
                        || (entry.TryGetProperty("infohash_v1", out var v1) && HashMatches(v1, id))
                        || (entry.TryGetProperty("infohash_v2", out var v2) && HashMatches(v2, id)))
                    {
                        return TorrentPresence.Present;
                    }
                }

                return TorrentPresence.Absent;
            }
            catch (JsonException)
            {
                return TorrentPresence.Unknown;
            }
        }

        private static bool HashMatches(JsonElement value, string id) =>
            value.ValueKind == JsonValueKind.String
            && string.Equals(value.GetString(), id, StringComparison.OrdinalIgnoreCase);
    }
}
