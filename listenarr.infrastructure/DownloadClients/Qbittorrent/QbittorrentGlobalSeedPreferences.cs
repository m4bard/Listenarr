/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.DownloadClients.Qbittorrent
{
    /// <summary>
    /// Fetches qBittorrent's global ratio/seeding-time preferences, used to resolve a
    /// per-torrent seed limit that is set to "inherit global" (Sonarr parity). Shared between
    /// the item-fetch and queue-poll workflows so both can reach the same seed-limit-aware
    /// <c>CanBeRemoved</c>/<c>CanRemove</c> computation.
    /// </summary>
    internal static class QbittorrentGlobalSeedPreferences
    {
        public static async Task<(bool GlobalMaxRatioEnabled, float GlobalMaxRatio, bool GlobalMaxSeedingTimeEnabled, long GlobalMaxSeedingTime)> FetchAsync(
            HttpClient httpClient,
            string baseUrl,
            ILogger logger,
            CancellationToken ct)
        {
            var globalMaxRatioEnabled = false;
            var globalMaxRatio = -1f;
            var globalMaxSeedingTimeEnabled = false;
            var globalMaxSeedingTime = -1L;

            try
            {
                using var prefsResp = await httpClient.GetAsync($"{baseUrl}/api/v2/app/preferences", ct);
                if (prefsResp.IsSuccessStatusCode)
                {
                    var prefsJson = await prefsResp.Content.ReadAsStringAsync(ct);
                    if (!string.IsNullOrWhiteSpace(prefsJson))
                    {
                        var prefs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(prefsJson);
                        if (prefs != null)
                        {
                            globalMaxRatioEnabled = prefs.TryGetValue("max_ratio_enabled", out var mre) && mre.GetBoolean();
                            globalMaxRatio = prefs.TryGetValue("max_ratio", out var mr) ? (float)mr.GetDouble() : -1f;
                            globalMaxSeedingTimeEnabled = prefs.TryGetValue("max_seeding_time_enabled", out var mste) && mste.GetBoolean();
                            globalMaxSeedingTime = prefs.TryGetValue("max_seeding_time", out var mst) ? mst.GetInt64() : -1;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogDebug(ex, "Failed to fetch qBittorrent preferences for seed limit evaluation, will use conservative defaults");
            }

            return (globalMaxRatioEnabled, globalMaxRatio, globalMaxSeedingTimeEnabled, globalMaxSeedingTime);
        }
    }
}
