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

using System.Text.Json;
using Listenarr.Application.Mapping;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.DownloadClients
{
    /// <summary>
    /// A completed torrent that has not reached its seed limit must not be reported
    /// as removable to the deferred client-removal path. Covers both qBittorrent and
    /// Transmission, since the queue-poll path for each (MapQueueItem) is what
    /// MovedDownloadCleanupService actually consumes via QueueItemConverter.
    /// </summary>
    [Trait("Name", "SeedLimitRemovalGateTests")]
    [Trait("Category", "DownloadClients")]
    public class SeedLimitRemovalGateTests : BaseTests
    {
        private const string StillSeedingTorrentJson = """
        {
            "hash": "0123456789abcdef0123456789abcdef01234567",
            "name": "Some Public Domain Book",
            "progress": 1.0,
            "size": 100000000,
            "downloaded": 100000000,
            "dlspeed": 0,
            "eta": 8640000,
            "state": "uploading",
            "added_on": 1700000000,
            "num_seeds": 3,
            "num_leechs": 1,
            "ratio": 0.10,
            "ratio_limit": 2.0,
            "seeding_time": 600,
            "seeding_time_limit": 86400,
            "save_path": "/downloads/complete",
            "content_path": "/downloads/complete/Some Public Domain Book",
            "category": "audiobooks"
        }
        """;

        private const string MetRatioLimitTorrentJson = """
        {
            "hash": "f0123456789abcdef0123456789abcdef012345",
            "name": "Some Other Public Domain Book",
            "progress": 1.0,
            "size": 100000000,
            "downloaded": 100000000,
            "dlspeed": 0,
            "eta": 8640000,
            "state": "pausedUP",
            "added_on": 1700000000,
            "num_seeds": 3,
            "num_leechs": 1,
            "ratio": 2.50,
            "ratio_limit": 2.0,
            "seeding_time": 90000,
            "seeding_time_limit": 86400,
            "save_path": "/downloads/complete",
            "content_path": "/downloads/complete/Some Other Public Domain Book",
            "category": "audiobooks"
        }
        """;

        private const string ExactRatioLimitTorrentJson = """
        {
            "hash": "e0123456789abcdef0123456789abcdef012345",
            "name": "A Fourth Public Domain Book",
            "progress": 1.0,
            "size": 100000000,
            "downloaded": 100000000,
            "dlspeed": 0,
            "eta": 8640000,
            "state": "pausedUP",
            "added_on": 1700000000,
            "num_seeds": 3,
            "num_leechs": 1,
            "ratio": 2.0,
            "ratio_limit": 2.0,
            "seeding_time": 90000,
            "seeding_time_limit": 86400,
            "save_path": "/downloads/complete",
            "content_path": "/downloads/complete/A Fourth Public Domain Book",
            "category": "audiobooks"
        }
        """;

        private const string NoSeedPolicyTorrentJson = """
        {
            "hash": "a0123456789abcdef0123456789abcdef012345",
            "name": "A Third Public Domain Book",
            "progress": 1.0,
            "size": 100000000,
            "downloaded": 100000000,
            "dlspeed": 0,
            "eta": 8640000,
            "state": "pausedUP",
            "added_on": 1700000000,
            "num_seeds": 3,
            "num_leechs": 1,
            "ratio": 0.05,
            "save_path": "/downloads/complete",
            "content_path": "/downloads/complete/A Third Public Domain Book",
            "category": "audiobooks"
        }
        """;

        private static Dictionary<string, JsonElement> ParseTorrent(string json) =>
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

        private static Dictionary<string, JsonElement> ParseTorrent() => ParseTorrent(StillSeedingTorrentJson);

        private static DownloadClientConfiguration BuildClient(string removeCompletedDownloads = "remove") => new()
        {
            Id = "qbit-1",
            Name = "qBittorrent",
            Type = "qbittorrent",
            IsEnabled = true,
            RemoveCompletedDownloads = removeCompletedDownloads
        };

        [Fact]
        [Trait("Method", "MapDownloadClientItem")]
        public void MapDownloadClientItem_ReportsNotRemovable_WhenSeedRatioLimitNotReached()
        {
            var item = QbittorrentResponseMapper.MapDownloadClientItem(
                ParseTorrent(),
                BuildClient(),
                removeCompletedDownloads: true,
                globalMaxRatioEnabled: false,
                globalMaxRatio: -1f,
                globalMaxSeedingTimeEnabled: false,
                globalMaxSeedingTime: -1);

            Assert.False(
                item.CanBeRemoved,
                "A torrent at ratio 0.10 against a per-torrent ratio limit of 2.0 has not met its seed limit.");
        }

        [Fact]
        [Trait("Method", "UpdateFromQueueItem")]
        public void PolledQueueItem_DoesNotMarkStillSeedingTorrentRemovable()
        {
            var torrent = ParseTorrent();
            var client = BuildClient();

            var seedAware = QbittorrentResponseMapper.MapDownloadClientItem(
                torrent,
                client,
                removeCompletedDownloads: true,
                globalMaxRatioEnabled: false,
                globalMaxRatio: -1f,
                globalMaxSeedingTimeEnabled: false,
                globalMaxSeedingTime: -1);
            Assert.False(seedAware.CanBeRemoved);

            var queueItem = QbittorrentResponseMapper.MapQueueItem(
                torrent,
                client,
                files: [],
                removeCompletedDownloads: true,
                globalMaxRatioEnabled: false,
                globalMaxRatio: -1f,
                globalMaxSeedingTimeEnabled: false,
                globalMaxSeedingTime: -1);
            var download = new Download
            {
                Id = "download-1",
                Status = DownloadStatus.Moved,
                DownloadClientId = client.Id
            };

            var updated = QueueItemConverter.UpdateFromQueueItem(download, queueItem);

            Assert.False(
                (bool)updated.Metadata["CanBeRemoved"],
                "MovedDownloadCleanupService removes the torrent from the client when this metadata is true, " +
                "so a torrent that has not met its seed limit must not be reported as removable.");
        }

        [Fact]
        [Trait("Method", "MapQueueItem")]
        public void MapQueueItem_MarksMetRatioLimitTorrentRemovable_WhenRemoveCompletedDownloadsOn()
        {
            var torrent = ParseTorrent(MetRatioLimitTorrentJson);
            var client = BuildClient("remove");

            var queueItem = QbittorrentResponseMapper.MapQueueItem(
                torrent,
                client,
                files: [],
                removeCompletedDownloads: true,
                globalMaxRatioEnabled: false,
                globalMaxRatio: -1f,
                globalMaxSeedingTimeEnabled: false,
                globalMaxSeedingTime: -1);

            Assert.True(
                queueItem.CanRemove,
                "A torrent at ratio 2.50 against a per-torrent ratio limit of 2.0 has met its seed limit, " +
                "and removeCompletedDownloads is on, so it should be reported removable.");
        }

        [Fact]
        [Trait("Method", "MapQueueItem")]
        public void MapQueueItem_MarksExactRatioLimitTorrentRemovable_WhenRemoveCompletedDownloadsOn()
        {
            // Exact-boundary case: ratio == ratio_limit (2.0 == 2.0). HasReachedSeedLimit's own
            // comparison is `ratioLimit - ratio <= 0.001`, so an exact match must count as reached,
            // not just the over-met case.
            var torrent = ParseTorrent(ExactRatioLimitTorrentJson);
            var client = BuildClient("remove");

            var queueItem = QbittorrentResponseMapper.MapQueueItem(
                torrent,
                client,
                files: [],
                removeCompletedDownloads: true,
                globalMaxRatioEnabled: false,
                globalMaxRatio: -1f,
                globalMaxSeedingTimeEnabled: false,
                globalMaxSeedingTime: -1);

            Assert.True(
                queueItem.CanRemove,
                "A torrent at ratio exactly 2.0 against a per-torrent ratio limit of 2.0 has reached its seed limit.");
        }

        [Fact]
        [Trait("Method", "MapQueueItem")]
        public void MapQueueItem_DoesNotMarkRemovable_WhenRemoveCompletedDownloadsOff()
        {
            // Same torrent as the "met ratio limit" case above: seed limit is reached, but
            // removeCompletedDownloads is off, so CanRemove must still be false.
            var torrent = ParseTorrent(MetRatioLimitTorrentJson);
            var client = BuildClient("none");

            var queueItem = QbittorrentResponseMapper.MapQueueItem(
                torrent,
                client,
                files: [],
                removeCompletedDownloads: false,
                globalMaxRatioEnabled: false,
                globalMaxRatio: -1f,
                globalMaxSeedingTimeEnabled: false,
                globalMaxSeedingTime: -1);

            Assert.False(
                queueItem.CanRemove,
                "removeCompletedDownloads is off, so CanRemove must be false regardless of seed state.");
        }

        [Fact]
        [Trait("Method", "MapQueueItem")]
        public void MapQueueItem_MarksRemovable_WhenNoSeedPolicyIsConfigured()
        {
            // No per-torrent ratio_limit/seeding_time_limit in the JSON, and no global
            // ratio/seeding-time preferences enabled either: HasReachedSeedLimit's documented
            // "no limit configured" default is to consider the seed limit already met.
            var torrent = ParseTorrent(NoSeedPolicyTorrentJson);
            var client = BuildClient("remove");

            var queueItem = QbittorrentResponseMapper.MapQueueItem(
                torrent,
                client,
                files: [],
                removeCompletedDownloads: true,
                globalMaxRatioEnabled: false,
                globalMaxRatio: -1f,
                globalMaxSeedingTimeEnabled: false,
                globalMaxSeedingTime: -1);

            Assert.True(
                queueItem.CanRemove,
                "With no seed policy configured at all, CanRemove should follow removeCompletedDownloads alone.");
        }
    }

    /// <summary>
    /// Transmission counterpart of <see cref="SeedLimitRemovalGateTests"/>: the queue-poll path
    /// (TransmissionQueueFetchWorkflow.GetQueueAsync -> MapQueueItem) must reach the same
    /// seed-limit-aware CanRemove computation that MapDownloadClientItem already uses.
    /// </summary>
    [Trait("Name", "TransmissionSeedLimitRemovalGateTests")]
    [Trait("Category", "DownloadClients")]
    public class TransmissionSeedLimitRemovalGateTests : BaseTests
    {
        private static readonly (bool SeedRatioLimited, double SeedRatioLimit, bool IdleSeedingLimitEnabled, int IdleSeedingLimit) NoSessionLimits =
            (false, 0, false, 0);

        private static DownloadClientConfiguration BuildClient(bool? removeCompletedDownloads) => new()
        {
            Id = "transmission-1",
            Name = "Transmission",
            Type = "transmission",
            IsEnabled = true,
            Settings = removeCompletedDownloads.HasValue
                ? new Dictionary<string, object> { ["removeCompletedDownloads"] = removeCompletedDownloads.Value }
                : new Dictionary<string, object>()
        };

        private static JsonElement BuildTorrent(
            int statusCode,
            double uploadRatio,
            int seedRatioMode,
            double seedRatioLimit,
            int seedIdleMode = 0,
            int seedIdleLimit = 0,
            long secondsSeeding = 0)
        {
            var json = $$"""
            {
                "id": 1,
                "hashString": "0123456789ABCDEF0123456789ABCDEF01234567",
                "name": "Some Public Domain Book",
                "percentDone": 1.0,
                "status": {{statusCode}},
                "totalSize": 100000000,
                "rateDownload": 0,
                "rateUpload": 0,
                "leftUntilDone": 0,
                "eta": -1,
                "downloadDir": "/downloads/complete",
                "addedDate": 1700000000,
                "uploadedEver": 1000,
                "uploadRatio": {{uploadRatio}},
                "labels": [],
                "seedRatioMode": {{seedRatioMode}},
                "seedRatioLimit": {{seedRatioLimit}},
                "seedIdleMode": {{seedIdleMode}},
                "seedIdleLimit": {{seedIdleLimit}},
                "secondsSeeding": {{secondsSeeding}}
            }
            """;

            return JsonSerializer.Deserialize<JsonElement>(json);
        }

        [Fact]
        [Trait("Method", "MapQueueItem")]
        public void MapQueueItem_DoesNotMarkStillSeedingTorrentRemovable_WhenRatioLimitNotReached()
        {
            // statusCode 6 = seeding (not stopped): ratio 0.10 against a per-torrent limit of
            // 2.0 has not met its seed limit.
            var torrent = BuildTorrent(statusCode: 6, uploadRatio: 0.10, seedRatioMode: 1, seedRatioLimit: 2.0);
            var client = BuildClient(removeCompletedDownloads: true);

            var queueItem = TransmissionResponseMapper.MapQueueItem(client, torrent, NoSessionLimits);

            Assert.False(
                queueItem.CanRemove,
                "A torrent at ratio 0.10 against a per-torrent ratio limit of 2.0 has not met its seed limit.");
        }

        [Fact]
        [Trait("Method", "MapQueueItem")]
        public void MapQueueItem_MarksMetRatioLimitTorrentRemovable_WhenRemoveCompletedDownloadsOn()
        {
            // statusCode 0 = stopped: Transmission auto-stops a torrent once its per-torrent
            // seed-ratio limit is reached, and HasReachedSeedLimit's ratio check is gated on
            // isStopped, matching that behavior.
            var torrent = BuildTorrent(statusCode: 0, uploadRatio: 2.50, seedRatioMode: 1, seedRatioLimit: 2.0);
            var client = BuildClient(removeCompletedDownloads: true);

            var queueItem = TransmissionResponseMapper.MapQueueItem(client, torrent, NoSessionLimits);

            Assert.True(
                queueItem.CanRemove,
                "A stopped torrent at ratio 2.50 against a per-torrent ratio limit of 2.0 has met its seed limit, " +
                "and removeCompletedDownloads is on, so it should be reported removable.");
        }

        [Fact]
        [Trait("Method", "MapQueueItem")]
        public void MapQueueItem_MarksExactRatioLimitTorrentRemovable_WhenRemoveCompletedDownloadsOn()
        {
            // Exact-boundary case: ratio == seedRatioLimit (2.0 == 2.0), stopped. The evaluator's
            // own comparison is `ratio >= seedRatioLimit`, so an exact match must count as
            // reached too, not just the over-met case.
            var torrent = BuildTorrent(statusCode: 0, uploadRatio: 2.0, seedRatioMode: 1, seedRatioLimit: 2.0);
            var client = BuildClient(removeCompletedDownloads: true);

            var queueItem = TransmissionResponseMapper.MapQueueItem(client, torrent, NoSessionLimits);

            Assert.True(
                queueItem.CanRemove,
                "A stopped torrent at ratio exactly 2.0 against a per-torrent ratio limit of 2.0 has reached its seed limit.");
        }

        [Fact]
        [Trait("Method", "MapQueueItem")]
        public void MapQueueItem_DoesNotMarkRemovable_WhenRemoveCompletedDownloadsOff()
        {
            // Same seed state as the "met ratio limit" case above, but removeCompletedDownloads
            // is off: CanRemove must still be false.
            var torrent = BuildTorrent(statusCode: 0, uploadRatio: 2.50, seedRatioMode: 1, seedRatioLimit: 2.0);
            var client = BuildClient(removeCompletedDownloads: false);

            var queueItem = TransmissionResponseMapper.MapQueueItem(client, torrent, NoSessionLimits);

            Assert.False(
                queueItem.CanRemove,
                "removeCompletedDownloads is off, so CanRemove must be false regardless of seed state.");
        }

        [Fact]
        [Trait("Method", "MapQueueItem")]
        public void MapQueueItem_MarksRemovable_WhenNoSeedPolicyIsConfigured()
        {
            // seedRatioMode/seedIdleMode 0 both mean "inherit session", and the session itself
            // has no limits enabled (NoSessionLimits): HasReachedSeedLimit's documented
            // "no limit configured" default is to consider the seed limit already met.
            var torrent = BuildTorrent(statusCode: 6, uploadRatio: 0.05, seedRatioMode: 0, seedRatioLimit: 0, seedIdleMode: 0, seedIdleLimit: 0);
            var client = BuildClient(removeCompletedDownloads: true);

            var queueItem = TransmissionResponseMapper.MapQueueItem(client, torrent, NoSessionLimits);

            Assert.True(
                queueItem.CanRemove,
                "With no seed policy configured at all, CanRemove should follow removeCompletedDownloads alone.");
        }
    }
}
