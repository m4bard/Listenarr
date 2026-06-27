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
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Downloads.Queue
{
    public sealed class DownloadOrphanCleanupService(
        IDownloadRepository downloadRepository,
        IAppMetricsService metrics,
        ILogger<DownloadOrphanCleanupService> logger)
    {
        private static readonly TimeSpan OrphanedDownloadGracePeriod = TimeSpan.FromMinutes(5);

        public async Task RemoveOrphansAsync(
            DownloadClientConfiguration client,
            ClientQueueFetchResult clientQueueResult,
            List<QueueItem> mappedQueueItems,
            List<Download> clientDownloads)
        {
            if (clientQueueResult.UsedCachedSnapshot)
            {
                logger.LogInformation(
                    "Skipping orphan cleanup for client {ClientName}: using cached queue snapshot",
                    client.Name);
                return;
            }

            if (clientQueueResult.IsUnavailable)
            {
                logger.LogWarning(
                    "Skipping orphan cleanup for client {ClientName}: no live queue snapshot was available",
                    client.Name);
                return;
            }

            var clientQueue = clientQueueResult.QueueItems;
            if (clientQueue.Count == 0 && clientDownloads.Any())
            {
                logger.LogWarning(
                    "Skipping orphan cleanup for client {ClientName}: client returned 0 queue items but {Count} downloads are tracked. Client may be temporarily unreachable.",
                    client.Name,
                    clientDownloads.Count);
                return;
            }

            var liveClientItemIds = BuildLiveClientItemIds(clientQueue, mappedQueueItems);
            var now = DateTime.UtcNow;
            var orphanedDownloads = clientDownloads
                .Select(d => new
                {
                    Download = d,
                    ExternalIds = DownloadQueueMetadataMatcher.GetKnownClientItemIds(d.Metadata)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .Where(candidate => IsOrphanedActiveDownload(
                    candidate.Download,
                    candidate.ExternalIds,
                    liveClientItemIds,
                    now))
                .ToList();

            if (!orphanedDownloads.Any())
            {
                return;
            }

            foreach (var orphan in orphanedDownloads)
            {
                var download = orphan.Download;
                var externalId = orphan.ExternalIds.First();

                logger.LogInformation(
                    "Removing orphaned download {DownloadId} '{Title}' for client {ClientName}: external id {ExternalId} was not found in live queue snapshot",
                    download.Id,
                    download.Title,
                    client.Name,
                    externalId);

                await downloadRepository.RemoveAsync(download.Id);
            }

            DownloadQueueDiagnostics.TryIncrementMetric(metrics, "download.orphan.removed", orphanedDownloads.Count);
        }

        private static HashSet<string> BuildLiveClientItemIds(
            List<QueueItem> clientQueue,
            List<QueueItem> mappedQueueItems)
        {
            var liveClientItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var mapped in mappedQueueItems.Where(item => !string.IsNullOrWhiteSpace(item.Id)))
            {
                liveClientItemIds.Add(mapped.Id!);
            }

            foreach (var itemId in clientQueue.Where(item => !string.IsNullOrWhiteSpace(item.Id)).Select(item => item.Id!))
            {
                liveClientItemIds.Add(itemId);
            }

            return liveClientItemIds;
        }

        private bool IsOrphanedActiveDownload(
            Download download,
            List<string> externalIds,
            HashSet<string> liveClientItemIds,
            DateTime now)
        {
            if (download.Status is not (DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Paused))
            {
                return false;
            }

            if (externalIds.Count == 0)
            {
                return false;
            }

            if (liveClientItemIds.Contains(download.Id) || externalIds.Any(liveClientItemIds.Contains))
            {
                return false;
            }

            var age = now - download.StartedAt;
            if (age < OrphanedDownloadGracePeriod)
            {
                logger.LogDebug(
                    "Skipping orphan cleanup for recent download {DownloadId} '{Title}' (age: {Age:F1} min)",
                    download.Id,
                    download.Title,
                    age.TotalMinutes);
                return false;
            }

            return true;
        }
    }
}
