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
    /// as removable to the deferred client-removal path.
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

        private static Dictionary<string, JsonElement> ParseTorrent() =>
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(StillSeedingTorrentJson)!;

        private static DownloadClientConfiguration BuildClient() => new()
        {
            Id = "qbit-1",
            Name = "qBittorrent",
            Type = "qbittorrent",
            IsEnabled = true,
            RemoveCompletedDownloads = "remove"
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

            var queueItem = QbittorrentResponseMapper.MapQueueItem(torrent, client, files: []);
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
    }
}
