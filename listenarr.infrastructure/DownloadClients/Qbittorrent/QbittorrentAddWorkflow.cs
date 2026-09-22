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

            var seedConfiguration = await seedCriteriaResolver.ResolveAsync(torrent.IndexerId, ct);
            var addPlan = QbittorrentTorrentAddPlanner.Create(client, torrent, seedConfiguration);

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

            // A 200 is not by itself an acceptance. qBittorrent's /torrents/add answers 200 with
            // the body "Fails." when it will not take the torrent, and on Web API below 2.14.0
            // (qBittorrent 5.2.0) that is the only signal there is: the 409 that newer builds
            // return for a duplicate info-hash did not exist yet. Without reading the body, the
            // workflow returns the info-hash it computed locally and the caller records a grab
            // for a torrent the client never accepted, so the download is tracked, never
            // progresses, and nothing explains why.
            //
            // Readarr reads the same body at both of its add sites, src/NzbDrone.Core/Download/
            // Clients/QBittorrent/QBittorrentProxyV2.cs:161 and :183, with the same note that
            // older versions returned nothing, so an equality test against "Ok." would be wrong
            // where a test against "Fails." is not.
            var addBody = await addResponse.Content.ReadAsStringAsync(ct);
            if (string.Equals(addBody.Trim(), "Fails.", StringComparison.Ordinal))
            {
                logger.LogError(
                    "qBittorrent answered HTTP {Status} but refused the torrent in the response body. Response: {Response}",
                    (int)addResponse.StatusCode,
                    LogRedaction.SanitizeText(addBody));
                throw new DownloadClientSubmissionException(
                    "qBittorrent accepted the request but refused the torrent, answering \"Fails.\".");
            }

            logger.LogInformation("Successfully sent torrent to qBittorrent");

            await Task.Delay(1000, ct);

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
                    using var forceResponse = await httpClient.PostAsync($"{baseUrl}/api/v2/torrents/setForceStart", forceContent, ct);
                    if (!forceResponse.IsSuccessStatusCode)
                    {
                        logger.LogWarning(
                            "qBittorrent accepted the torrent but refused force start with HTTP {StatusCode}; it will download at normal priority",
                            (int)forceResponse.StatusCode);
                    }
                }
                catch (TaskCanceledException exception) when (!ct.IsCancellationRequested)
                {
                    // An HttpClient timeout arrives as TaskCanceledException even though nothing
                    // was cancelled, so it has to be told apart from a real cancellation the way
                    // DownloadMonitorService already does. Letting it out would fail a submission
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
