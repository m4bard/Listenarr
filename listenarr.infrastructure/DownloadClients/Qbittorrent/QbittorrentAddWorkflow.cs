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
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.DownloadClients.Qbittorrent
{
    internal sealed class QbittorrentAddWorkflow(
        IHttpClientFactory httpClientFactory,
        QbittorrentAuthSession authSession,
        ILogger<QbittorrentAdapter> logger,
        string clientType)
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

            var addPlan = QbittorrentTorrentAddPlanner.Create(client, torrent);

            using var addContent = QbittorrentAddRequestContentBuilder.Build(addPlan);
            using var addResponse = await httpClient.PostAsync($"{baseUrl}/api/v2/torrents/add", addContent, ct);

            if (!addResponse.IsSuccessStatusCode)
            {
                var responseContent = await addResponse.Content.ReadAsStringAsync(ct);
                var redacted = LogRedaction.RedactText(responseContent, LogRedaction.GetSensitiveValuesFromEnvironment().Concat([client.Password ?? string.Empty]));

                // A 409 means qBittorrent already holds this info-hash. WebAPI 2.14.0
                // (qBittorrent 5.2.0) added it; below that a duplicate add answers 200
                // with the body "Fails.". It is not a client fault and not a bad release,
                // so it is raised as a rejection rather than a submission failure and
                // callers can skip instead of erroring.
                if (addResponse.StatusCode == HttpStatusCode.Conflict)
                {
                    logger.LogInformation(
                        "qBittorrent already holds this release, so the add was refused with HTTP 409. Response: {Response}",
                        LogRedaction.SanitizeText(redacted));
                    throw new DownloadClientRejectedReleaseException(
                        "qBittorrent already holds this release, so it refused the torrent with HTTP 409.");
                }

                // RedactText masks known secret values but leaves the rest of the body as the
                // client sent it, newlines included. SanitizeText is what strips CR, LF and tab
                // and caps the length, so a response body cannot forge a log line or flood the
                // log. Both call sites get it, because fixing only the new one would leave the
                // identical hole three lines away.
                logger.LogError($"Failed to add torrent to qBittorrent. Status: {addResponse.StatusCode}, Response: {LogRedaction.SanitizeText(redacted)}");
                throw new DownloadClientSubmissionException($"qBittorrent rejected the torrent with HTTP {(int)addResponse.StatusCode}.");
            }

            logger.LogInformation("Successfully sent torrent to qBittorrent");

            // qBittorrent holds the torrent from here on, so the steps below must not be cut short by
            // the caller's token. A shutdown that cancelled them would make the caller delete the
            // provisional download row while the torrent downloads on unnoticed.
            await Task.Delay(1000, CancellationToken.None);

            // Force start cannot be asked for on the add call. Sending forceStart there is
            // accepted and ignored, so it takes a second request once the torrent exists.
            // Verified against qBittorrent 5.2.3, Web API 2.15.1.
            if (addPlan.ForceStart)
            {
                try
                {
                    using var forceContent = new FormUrlEncodedContent(
                    [
                        new KeyValuePair<string, string>("hashes", addPlan.Hash),
                        new KeyValuePair<string, string>("value", "true")
                    ]);
                    using var forceResponse = await httpClient.PostAsync($"{baseUrl}/api/v2/torrents/setForceStart", forceContent, CancellationToken.None);
                    if (!forceResponse.IsSuccessStatusCode)
                    {
                        logger.LogWarning(
                            "qBittorrent accepted the torrent but refused force start with HTTP {StatusCode}; it will download at normal priority",
                            (int)forceResponse.StatusCode);
                    }
                }
                catch (TaskCanceledException exception)
                {
                    // An HttpClient timeout arrives as TaskCanceledException even though nothing
                    // was cancelled, and this request no longer takes the caller's token, so every
                    // TaskCanceledException here is a timeout. Letting it out would fail a submission
                    // the client has already accepted, and the caller answers that by deleting
                    // the provisional download row while the torrent downloads on unnoticed.
                    logger.LogWarning(exception, "qBittorrent force start request timed out; the torrent was added and will download at normal priority");
                }
                catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
                {
                    // The torrent is already added. Failing the whole submission because an
                    // optional priority tweak did not apply would lose a download that is fine.
                    logger.LogWarning(exception, "qBittorrent force start request failed; the torrent was added and will download at normal priority");
                }
            }

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
                        using var trackersResp = await httpClient.PostAsync($"{baseUrl}/api/v2/torrents/addTrackers", addTrackersData, CancellationToken.None);
                        if (trackersResp.IsSuccessStatusCode)
                            logger.LogInformation($"Injected {trackerAnnounces.Count} tracker(s) for torrent {addPlan.Hash} via addTrackers API");
                        else
                            logger.LogDebug($"addTrackers API returned {trackersResp.StatusCode} for torrent {addPlan.Hash} (non-fatal)");
                    }
                }
                catch (TaskCanceledException exception)
                {
                    // Like force start above, this request no longer takes the caller's token, so a
                    // TaskCanceledException here is an HttpClient timeout. The torrent is already
                    // added and must not be reported as a failed submission.
                    logger.LogWarning(exception, "qBittorrent addTrackers request timed out; the torrent was added without the injected trackers");
                }
                catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
                {
                    logger.LogDebug(exception, "Non-fatal failure injecting trackers via addTrackers API");
                }
            }

            return new DownloadClientSubmissionResult(addPlan.Hash, addPlan.Hash);
        }
    }
}
