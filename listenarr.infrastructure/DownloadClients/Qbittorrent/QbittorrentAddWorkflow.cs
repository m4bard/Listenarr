/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.DownloadClients.Qbittorrent
{
    internal sealed class QbittorrentAddWorkflow(
        IHttpClientFactory httpClientFactory,
        QbittorrentAuthSession authSession,
        ILogger<QbittorrentAdapter> logger,
        string clientType,
        ISeedCriteriaResolver seedCriteriaResolver)
    {
        public async Task<DownloadClientSubmissionResult> AddAsync(
            DownloadClientConfiguration client,
            PreparedDownloadSubmission submission,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            if (submission is not PreparedTorrentSubmission torrent)
            {
                throw new DownloadClientSubmissionException("qBittorrent requires a prepared torrent submission.");
            }

            var baseUrl = DownloadClientUriBuilder.BuildAuthority(client);
            using var httpClient = httpClientFactory.CreateClient(clientType);

            try
            {
                await authSession.LoginAsync(httpClient, client, ct);
            }
            catch (QbittorrentException exception)
            {
                logger.LogError(exception, "qBittorrent authentication failed for client {ClientId}", LogRedaction.SanitizeText(client.Id));
                throw new DownloadClientSubmissionException("qBittorrent authentication failed.", exception);
            }

            // Non-fatal by design: a failure resolving the indexer's seed criteria (for
            // example a transient database error) must never block the grab itself. Falling
            // back to no seed configuration is the same "leave the client's own configuration
            // alone" behavior as an indexer that genuinely has none configured.
            TorrentSeedConfiguration? seedConfiguration = null;
            try
            {
                seedConfiguration = await seedCriteriaResolver.ResolveAsync(torrent.IndexerId, ct);
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
            {
                logger.LogDebug(exception, "Non-fatal failure resolving indexer seed criteria; proceeding without them");
            }

            var addPlan = QbittorrentTorrentAddPlanner.Create(client, torrent, seedConfiguration);

            using var addContent = QbittorrentAddRequestContentBuilder.Build(addPlan);
            using var addResponse = await httpClient.PostAsync($"{baseUrl}/api/v2/torrents/add", addContent, ct);

            if (!addResponse.IsSuccessStatusCode)
            {
                var responseContent = await addResponse.Content.ReadAsStringAsync(ct);
                var redacted = LogRedaction.RedactText(responseContent, LogRedaction.GetSensitiveValuesFromEnvironment().Concat([client.Password ?? string.Empty]));

                logger.LogError($"Failed to add torrent to qBittorrent. Status: {addResponse.StatusCode}, Response: {redacted}");
                throw new DownloadClientSubmissionException($"qBittorrent rejected the torrent with HTTP {(int)addResponse.StatusCode}.");
            }

            logger.LogInformation("Successfully sent torrent to qBittorrent");

            await Task.Delay(1000, ct);

            // qBittorrent can accept a torrent while failing to register private tracker
            // URLs from the file. Keep this explicit fallback in the add workflow so the
            // facade adapter stays thin without hiding this client-specific behavior.
            if (addPlan.TorrentFileData != null)
            {
                try
                {
                    var trackerAnnounces = torrent.TrackerUrls.Where(a =>
                        a.Contains("/announce", StringComparison.OrdinalIgnoreCase) ||
                        a.Contains("/tracker", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (trackerAnnounces != null && trackerAnnounces.Count > 0)
                    {
                        var trackerUrls = string.Join("\n", trackerAnnounces.Distinct());
                        using var addTrackersData = new FormUrlEncodedContent(new[]
                        {
                            new KeyValuePair<string, string>("hash", addPlan.Hash),
                            new KeyValuePair<string, string>("urls", trackerUrls)
                        });
                        using var trackersResp = await httpClient.PostAsync($"{baseUrl}/api/v2/torrents/addTrackers", addTrackersData, ct);
                        if (trackersResp.IsSuccessStatusCode)
                            logger.LogInformation($"Injected {trackerAnnounces.Count} tracker(s) for torrent {addPlan.Hash} via addTrackers API");
                        else
                            logger.LogDebug($"addTrackers API returned {trackersResp.StatusCode} for torrent {addPlan.Hash} (non-fatal)");
                    }
                }
                catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
                {
                    logger.LogDebug(exception, "Non-fatal failure injecting trackers via addTrackers API");
                }
            }

            // Only issued when the grabbing indexer actually has a seed ratio or seed time
            // configured (addPlan.SeedConfiguration is null otherwise), so an indexer with no
            // seed criteria never causes a setShareLimits call, let alone one with a default
            // or zero value that would silently override the operator's own qBittorrent
            // configuration.
            if (addPlan.SeedConfiguration != null)
            {
                try
                {
                    using var shareLimitsContent = QbittorrentAddRequestContentBuilder.BuildShareLimitsContent(
                        addPlan.Hash,
                        addPlan.SeedConfiguration);
                    using var shareLimitsResponse = await httpClient.PostAsync(
                        $"{baseUrl}/api/v2/torrents/setShareLimits", shareLimitsContent, ct);
                    if (shareLimitsResponse.IsSuccessStatusCode)
                        logger.LogInformation("Applied indexer seed criteria for torrent {Hash} via setShareLimits API", addPlan.Hash);
                    else
                        logger.LogDebug("setShareLimits API returned {StatusCode} for torrent {Hash} (non-fatal)", shareLimitsResponse.StatusCode, addPlan.Hash);
                }
                catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
                {
                    logger.LogDebug(exception, "Non-fatal failure applying indexer seed criteria via setShareLimits API");
                }
            }

            return new DownloadClientSubmissionResult(addPlan.Hash, addPlan.Hash);
        }
    }
}
