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
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Downloads.Submission
{
    /// <summary>
    /// Selection consults the persisted failure status the way Readarr's
    /// DownloadClientProvider.GetDownloadClient does (src/NzbDrone.Core/Download/DownloadClientProvider.cs):
    /// a blocked client is dropped from the candidates, a blocked one is used anyway when nothing
    /// else is left (:85-101), and a client an indexer is bound to is returned even while blocked
    /// unless the grab is a retry of a release that was parked because its client was unavailable
    /// (:76-79), which Listenarr has no store for.
    /// </summary>
    [Trait("Area", "DownloadClientSelection")]
    [Trait("Name", "DownloadClientStatusSelectionTests")]
    [Trait("Category", "Application")]
    public class DownloadClientStatusSelectionTests : BaseTests
    {
        private const int TrackerIndexerId = 7;

        private static DownloadClientConfiguration Client(string id, string type, int priority = 1) =>
            new()
            {
                Id = id,
                Name = id,
                Type = type,
                IsEnabled = true,
                Priority = priority
            };

        private static DownloadClientSelector CreateSelector(
            IEnumerable<string> blocked,
            string? trackerBinding,
            params DownloadClientConfiguration[] clients)
        {
            var configurationService = new Mock<IConfigurationService>();
            configurationService
                .Setup(c => c.GetDownloadClientConfigurationsAsync())
                .ReturnsAsync(clients.ToList());

            var indexer = new Indexer
            {
                Id = TrackerIndexerId,
                Name = "Private Tracker",
                Type = "Torrent",
                Implementation = "Torznab",
                IsEnabled = true,
                DownloadClientId = trackerBinding
            };
            var indexerRepository = new Mock<IIndexerRepository>();
            indexerRepository
                .Setup(r => r.GetByIdAsync(TrackerIndexerId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(indexer);

            var blockedSet = new HashSet<string>(blocked, StringComparer.Ordinal);
            var status = new Mock<IDownloadClientStatusService>();
            status
                .Setup(s => s.GetBlockedClientIdsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(blockedSet);

            return new DownloadClientSelector(
                configurationService.Object,
                indexerRepository.Object,
                new DownloadClientRoundRobinState(),
                status.Object,
                NullLogger<DownloadClientSelector>.Instance);
        }

        [Fact]
        [Trait("Scenario", "BlockedClientSkipped")]
        public async Task ABlockedClient_IsSkippedInFavourOfAHealthyLowerPriorityOne()
        {
            // qb-main would win on priority alone; only its failure status can move the grab.
            var selector = CreateSelector(
                blocked: ["qb-main"],
                trackerBinding: null,
                Client("qb-main", "qbittorrent", priority: 1),
                Client("tr-spare", "transmission", priority: 10));

            var picks = new[]
            {
                await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent),
                await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent)
            };

            // Twice, so a rotation that happened to land on the spare once cannot pass.
            Assert.Equal(new string?[] { "tr-spare", "tr-spare" }, picks);
        }

        [Fact]
        [Trait("Scenario", "BlockedClientSkipped")]
        public async Task WithNothingBlocked_PriorityDecidesAsBefore()
        {
            // Control for the test above: same clients, no failure status.
            var selector = CreateSelector(
                blocked: [],
                trackerBinding: null,
                Client("qb-main", "qbittorrent", priority: 1),
                Client("tr-spare", "transmission", priority: 10));

            Assert.Equal("qb-main", await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent));
        }

        [Fact]
        [Trait("Scenario", "BlockedClientSkipped")]
        public async Task ABlockedClient_OfTheOtherProtocol_ChangesNothing()
        {
            // A blocked usenet client says nothing about which torrent client to use.
            var selector = CreateSelector(
                blocked: ["sab"],
                trackerBinding: null,
                Client("qb-main", "qbittorrent", priority: 1),
                Client("tr-spare", "transmission", priority: 10),
                Client("sab", "sabnzbd", priority: 1));

            Assert.Equal("qb-main", await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent));
        }

        [Fact]
        [Trait("Scenario", "AllBlockedFallsBack")]
        public async Task WhenEveryClientIsBlocked_OneIsUsedAnyway()
        {
            // Readarr retries a blocked client rather than refusing the grab outright, because a
            // block is a guess that the client is down and the grab is the thing the user wanted.
            var selector = CreateSelector(
                blocked: ["qb-main", "tr-spare"],
                trackerBinding: null,
                Client("qb-main", "qbittorrent", priority: 1),
                Client("tr-spare", "transmission", priority: 10));

            // With the block ignored, priority applies again.
            Assert.Equal("qb-main", await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent));
        }

        [Fact]
        [Trait("Scenario", "BoundClientBlocked")]
        public async Task ABoundClientThatIsBlocked_IsStillUsed()
        {
            // The binding exists to keep a tracker's torrents on the client configured for its
            // seeding rules. Sending them to another client because this one is in backoff would
            // break exactly that, and Readarr only refuses here when retrying a parked release.
            var selector = CreateSelector(
                blocked: ["qb-seedbox"],
                trackerBinding: "qb-seedbox",
                Client("qb-local", "qbittorrent", priority: 1),
                Client("qb-seedbox", "qbittorrent", priority: 10));

            Assert.Equal(
                "qb-seedbox",
                await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent, TrackerIndexerId));
        }
    }
}
