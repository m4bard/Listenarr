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
    /// Covers the single selection policy that replaced the two hardcoded vendor chains.
    /// Several of these tests are paired: one asserts that a mechanism moves the answer,
    /// and its partner asserts a case where the same mechanism must leave the answer alone,
    /// so an inert selector cannot satisfy both.
    /// </summary>
    [Trait("Area", "DownloadClientSelection")]
    [Trait("Name", "DownloadClientSelectionTests")]
    [Trait("Category", "Application")]
    public class DownloadClientSelectionTests : BaseTests
    {
        private static readonly DateTime FirstAdded = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static DownloadClientConfiguration Client(
            string id,
            string type,
            int priority = 1,
            int addedOrder = 0,
            bool enabled = true) =>
            new()
            {
                Id = id,
                Name = id,
                Type = type,
                IsEnabled = enabled,
                Priority = priority,
                CreatedAt = FirstAdded.AddMinutes(addedOrder)
            };

        [Fact]
        [Trait("Scenario", "RotationOrderSurvivesAnEdit")]
        public async Task RotationOrder_DoesNotDependOnCreatedAt()
        {
            // The save path copies the posted object over the stored one and the form sends no
            // CreatedAt, so an ordinary rename rewrites it. Ordering on it would move the
            // renamed client to the back of the rotation. Here the CreatedAt order is the
            // reverse of the Id order, so a selector keyed on CreatedAt picks the other one.
            var selector = CreateSelector(
                Client("qb-alpha", "qbittorrent", addedOrder: 10),
                Client("qb-beta", "qbittorrent", addedOrder: 0));

            var first = await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent);

            Assert.Equal("qb-alpha", first);
        }

        private static IDownloadClientStatusService NoBlockedClients()
        {
            var status = new Mock<IDownloadClientStatusService>();
            status
                .Setup(s => s.GetBlockedClientIdsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HashSet<string>());
            return status.Object;
        }

        private static DownloadClientSelector CreateSelector(
            params DownloadClientConfiguration[] clients)
        {
            var configurationService = new Mock<IConfigurationService>();
            configurationService
                .Setup(c => c.GetDownloadClientConfigurationsAsync())
                .ReturnsAsync(clients.ToList());

            return new DownloadClientSelector(
                configurationService.Object,
                new Mock<IIndexerRepository>().Object,
                new DownloadClientRoundRobinState(),
                NoBlockedClients(),
                NullLogger<DownloadClientSelector>.Instance);
        }

        [Fact]
        [Trait("Scenario", "LowestPriorityWinsRegardlessOfVendor")]
        public async Task LowerPriorityTransmission_BeatsDefaultPriorityQbittorrent()
        {
            var selector = CreateSelector(
                Client("qb", "qbittorrent", priority: 5, addedOrder: 0),
                Client("tr", "transmission", priority: 1, addedOrder: 1));

            var selected = await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent);

            // The old chain took qBittorrent unconditionally, so this is the assertion that
            // fails outright if the priority ordering is removed.
            Assert.Equal("tr", selected);
        }

        [Fact]
        [Trait("Scenario", "HigherPriorityNumberIsSkipped")]
        public async Task HigherPriorityNumber_IsNotChosenWhenALowerOneExists()
        {
            // Control for the test above, with the vendor order reversed. If the selector had
            // simply swapped one hardcoded preference for another, exactly one of the two
            // would pass.
            var selector = CreateSelector(
                Client("tr", "transmission", priority: 9, addedOrder: 0),
                Client("qb", "qbittorrent", priority: 2, addedOrder: 1));

            var selected = await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent);

            Assert.Equal("qb", selected);
        }

        [Fact]
        [Trait("Scenario", "RoundRobinWithinPriorityGroup")]
        public async Task TwoClientsAtTheSamePriority_TakeTurns()
        {
            var selector = CreateSelector(
                Client("qb-one", "qbittorrent", priority: 1, addedOrder: 0),
                Client("qb-two", "qbittorrent", priority: 1, addedOrder: 1));

            var picks = new[]
            {
                await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent),
                await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent),
                await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent)
            };

            // The second qBittorrent was previously unreachable for ever. Three grabs, so the
            // wrap back to the head of the group is observed too.
            Assert.Equal(new string?[] { "qb-one", "qb-two", "qb-one" }, picks);
        }

        [Fact]
        [Trait("Scenario", "RoundRobinDoesNotCrossPriorityGroups")]
        public async Task ASingleClientAtTheLowestPriority_TakesEveryGrab()
        {
            // Control for the rotation test: a selector that rotated over everything enabled,
            // rather than over the lowest priority group, would hand the second grab to "qb-two".
            var selector = CreateSelector(
                Client("qb-one", "qbittorrent", priority: 1, addedOrder: 0),
                Client("qb-two", "qbittorrent", priority: 2, addedOrder: 1));

            var picks = new[]
            {
                await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent),
                await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent)
            };

            Assert.Equal(new string?[] { "qb-one", "qb-one" }, picks);
        }

        [Fact]
        [Trait("Scenario", "RotationIsPerProtocol")]
        public async Task TorrentRotation_DoesNotAdvanceTheUsenetCursor()
        {
            var selector = CreateSelector(
                Client("qb-one", "qbittorrent", addedOrder: 0),
                Client("qb-two", "qbittorrent", addedOrder: 1),
                Client("sab-one", "sabnzbd", addedOrder: 2),
                Client("sab-two", "sabnzbd", addedOrder: 3));

            await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent);
            var firstUsenet = await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Usenet);
            var secondTorrent = await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent);

            Assert.Equal("sab-one", firstUsenet);
            Assert.Equal("qb-two", secondTorrent);
        }

        [Fact]
        [Trait("Scenario", "DisabledClientsAreNeverSelected")]
        public async Task ADisabledClientAtTheLowestPriority_IsIgnored()
        {
            var selector = CreateSelector(
                Client("qb-disabled", "qbittorrent", priority: 1, addedOrder: 0, enabled: false),
                Client("tr-enabled", "transmission", priority: 4, addedOrder: 1));

            var selected = await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent);

            Assert.Equal("tr-enabled", selected);
        }

        [Fact]
        [Trait("Scenario", "ProtocolSeparation")]
        public async Task AUsenetClient_IsNeverOfferedForATorrent()
        {
            var selector = CreateSelector(
                Client("sab", "sabnzbd", priority: 1, addedOrder: 0));

            var torrent = await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent);
            var usenet = await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Usenet);

            Assert.Null(torrent);
            Assert.Equal("sab", usenet);
        }

        [Fact]
        [Trait("Scenario", "NotFoundIsNull")]
        public async Task NoEnabledClientOfTheRightProtocol_ReturnsNull()
        {
            var selector = CreateSelector(
                Client("qb", "qbittorrent", enabled: false));

            var selected = await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent);

            // One not-found answer. The automatic-search copy used to return an empty string,
            // which is why the two callers guarded it differently.
            Assert.Null(selected);
        }

        [Fact]
        [Trait("Scenario", "DirectDownloadNeedsNoConfiguredClient")]
        public async Task ADirectDownload_ResolvesToTheInternalClientWithNothingConfigured()
        {
            var selector = CreateSelector();

            var selected = await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.DirectDownload);

            // The application copy did not know about DDL, so a direct download reaching
            // SearchAndDownloadAsync went looking for an NZB client and failed when none existed.
            Assert.Equal(DirectDownloadMetadataKeys.ClientId, selected);
        }

        [Fact]
        [Trait("Scenario", "UnknownProtocolKeepsLegacyUsenetFallback")]
        public async Task AnUnknownProtocol_StillFallsBackToAUsenetClient()
        {
            var selector = CreateSelector(
                Client("sab", "sabnzbd", addedOrder: 0));

            var selected = await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Unknown);

            // Unchanged from the old bool-based selector, where anything that was not a torrent
            // was treated as NZB.
            Assert.Equal("sab", selected);
        }

        [Fact]
        [Trait("Scenario", "PriorityBoundsMatchTheArrFamily")]
        public void PriorityBounds_MatchReadarrAndSonarr()
        {
            Assert.Equal(1, DownloadClientSelector.MinimumPriority);
            Assert.Equal(50, DownloadClientSelector.MaximumPriority);
            Assert.Equal(1, new DownloadClientConfiguration().Priority);
        }
    }
}
