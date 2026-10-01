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
                // hash qBittorrent never had. Confirm presence first and fail closed on anything
                // we can't read cleanly, rather than letting the caller believe something was
                // resolved when it wasn't.
                using var infoResp = await httpClient.GetAsync($"{baseUrl}/api/v2/torrents/info?hashes={Uri.EscapeDataString(id)}", ct);
                if (!infoResp.IsSuccessStatusCode)
                {
                    var infoBody = await infoResp.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("qBittorrent presence check returned {Status}: {Body}", infoResp.StatusCode, LogRedaction.RedactText(infoBody, LogRedaction.GetSensitiveValuesFromEnvironment()));
                    return false;
                }

                var infoJson = await infoResp.Content.ReadAsStringAsync(ct);
                List<JsonElement>? matches;
                try
                {
                    matches = JsonSerializer.Deserialize<List<JsonElement>>(infoJson);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "qBittorrent presence check returned an unparseable response for torrent {Id}", LogRedaction.SanitizeText(id));
                    return false;
                }

                if (matches == null || matches.Count == 0)
                {
                    _logger.LogWarning("qBittorrent no longer has torrent {Id}; it was already absent from the client, nothing to delete", LogRedaction.SanitizeText(id));
                    return false;
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
    }
}
