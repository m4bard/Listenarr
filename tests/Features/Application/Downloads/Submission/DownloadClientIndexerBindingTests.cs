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
    /// An indexer may name the download client its grabs go to. Readarr and Sonarr resolve this
    /// in DownloadClientProvider.GetDownloadClient: a bound indexer uses its client, and a bound
    /// client that is missing, disabled or of the wrong protocol is an error rather than a
    /// silent fallback to some other client. An unbound indexer is left to the ordinary
    /// priority and rotation policy.
    /// </summary>
    [Trait("Area", "DownloadClientSelection")]
    [Trait("Name", "DownloadClientIndexerBindingTests")]
    [Trait("Category", "Application")]
    public class DownloadClientIndexerBindingTests : BaseTests
    {
        private const int TrackerIndexerId = 7;
        private const string TrackerIndexerName = "Private Tracker";

        private static DownloadClientConfiguration Client(
            string id,
            string type,
            int priority = 1,
            bool enabled = true) =>
            new()
            {
                Id = id,
                Name = id,
                Type = type,
                IsEnabled = enabled,
                Priority = priority
            };

        private static Indexer TrackerBoundTo(string? downloadClientId) =>
            new()
            {
                Id = TrackerIndexerId,
                Name = TrackerIndexerName,
                Type = "Torrent",
                Implementation = "Torznab",
                IsEnabled = true,
                DownloadClientId = downloadClientId
            };

        private static DownloadClientSelector CreateSelector(
            Indexer? indexer,
            params DownloadClientConfiguration[] clients)
        {
            var configurationService = new Mock<IConfigurationService>();
            configurationService
                .Setup(c => c.GetDownloadClientConfigurationsAsync())
                .ReturnsAsync(clients.ToList());

            var indexerRepository = new Mock<IIndexerRepository>();
            indexerRepository
                .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int id, CancellationToken _) => indexer != null && indexer.Id == id ? indexer : null);

            return new DownloadClientSelector(
                configurationService.Object,
                indexerRepository.Object,
                new DownloadClientRoundRobinState(),
                NullLogger<DownloadClientSelector>.Instance);
        }

        [Fact]
        [Trait("Scenario", "BoundIndexerBeatsPriority")]
        public async Task ABoundIndexer_UsesItsClient_EvenWhenAHigherPriorityClientExists()
        {
            // The seedbox has the worse (higher) priority number, so the ordinary policy would
            // never pick it. Only the binding can.
            var selector = CreateSelector(
                TrackerBoundTo("qb-seedbox"),
                Client("qb-local", "qbittorrent", priority: 1),
                Client("qb-seedbox", "qbittorrent", priority: 10));

            var picks = new[]
            {
                await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent, TrackerIndexerId),
                await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent, TrackerIndexerId)
            };

            // Twice, so a selector that happened to rotate onto the seedbox once would fail.
            Assert.Equal(new string?[] { "qb-seedbox", "qb-seedbox" }, picks);
        }

        [Fact]
        [Trait("Scenario", "UnboundIndexerIsUnchanged")]
        public async Task AnUnboundIndexer_GetsTheOrdinaryPolicy()
        {
            // Control for the test above: same clients, no binding, so the lower priority
            // number wins exactly as it did before bindings existed.
            var selector = CreateSelector(
                TrackerBoundTo(null),
                Client("qb-local", "qbittorrent", priority: 1),
                Client("qb-seedbox", "qbittorrent", priority: 10));

            var selected = await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent, TrackerIndexerId);

            Assert.Equal("qb-local", selected);
        }

        [Fact]
        [Trait("Scenario", "UnboundIndexerIsUnchanged")]
        public async Task AnUnknownIndexerId_GetsTheOrdinaryPolicy()
        {
            // A release from an indexer that has since been deleted still carries its id.
            // That is not a binding, and must not be treated as one.
            var selector = CreateSelector(
                TrackerBoundTo("qb-seedbox"),
                Client("qb-local", "qbittorrent", priority: 1),
                Client("qb-seedbox", "qbittorrent", priority: 10));

            var selected = await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent, TrackerIndexerId + 1);

            Assert.Equal("qb-local", selected);
        }

        [Fact]
        [Trait("Scenario", "BoundClientMissing")]
        public async Task ABindingToADeletedClient_FailsLoudly_AndDoesNotFallBack()
        {
            var selector = CreateSelector(
                TrackerBoundTo("qb-deleted"),
                Client("qb-local", "qbittorrent", priority: 1));

            var ex = await Assert.ThrowsAsync<DownloadClientUnavailableException>(
                () => selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent, TrackerIndexerId));

            Assert.Contains(TrackerIndexerName, ex.Message);
            Assert.Contains("does not exist", ex.Message);
        }

        [Fact]
        [Trait("Scenario", "BoundClientDisabled")]
        public async Task ABindingToADisabledClient_FailsLoudly_AndDoesNotFallBack()
        {
            var selector = CreateSelector(
                TrackerBoundTo("qb-seedbox"),
                Client("qb-local", "qbittorrent", priority: 1),
                Client("qb-seedbox", "qbittorrent", priority: 10, enabled: false));

            var ex = await Assert.ThrowsAsync<DownloadClientUnavailableException>(
                () => selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent, TrackerIndexerId));

            Assert.Contains(TrackerIndexerName, ex.Message);
            Assert.Contains("disabled", ex.Message);
        }

        [Fact]
        [Trait("Scenario", "BoundClientDisabled")]
        public async Task ABindingToADisabledClient_FailsEvenWhenItIsTheOnlyClientOfItsProtocol()
        {
            // Readarr checks for an empty candidate list before it looks at the binding, so in
            // this arrangement it reports "no client configured" and never names the indexer.
            // Here the binding is checked first so the message says which setting to fix.
            var selector = CreateSelector(
                TrackerBoundTo("qb-seedbox"),
                Client("qb-seedbox", "qbittorrent", enabled: false));

            var ex = await Assert.ThrowsAsync<DownloadClientUnavailableException>(
                () => selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent, TrackerIndexerId));

            Assert.Contains(TrackerIndexerName, ex.Message);
        }

        [Fact]
        [Trait("Scenario", "BoundClientWrongProtocol")]
        public async Task ABindingToAClientOfTheWrongProtocol_FailsLoudly()
        {
            // A torrent cannot be handed to SABnzbd, however the indexer is configured.
            var selector = CreateSelector(
                TrackerBoundTo("sab"),
                Client("qb-local", "qbittorrent", priority: 1),
                Client("sab", "sabnzbd", priority: 1));

            var ex = await Assert.ThrowsAsync<DownloadClientUnavailableException>(
                () => selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent, TrackerIndexerId));

            Assert.Contains(TrackerIndexerName, ex.Message);
            Assert.Contains("torrent", ex.Message);
        }

        [Fact]
        [Trait("Scenario", "BindingLeavesRotationAlone")]
        public async Task ABoundGrab_DoesNotAdvanceTheRotationForEveryoneElse()
        {
            // Two clients share the lowest priority. The tracker is bound to the first. If the
            // bound grab moved the rotation cursor, the next unbound grab would go to the
            // second; Readarr returns the bound client before touching its cursor, so the next
            // unbound grab still starts at the head of the group.
            var configurationClients = new[]
            {
                Client("qb-a", "qbittorrent", priority: 1),
                Client("qb-b", "qbittorrent", priority: 1)
            };
            var selector = CreateSelector(TrackerBoundTo("qb-a"), configurationClients);

            var bound = await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent, TrackerIndexerId);
            var unbound = await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.Torrent);

            Assert.Equal("qb-a", bound);
            Assert.Equal("qb-a", unbound);
        }

        [Fact]
        [Trait("Scenario", "DirectDownloadIgnoresBinding")]
        public async Task ADirectDownload_StillGoesToTheInternalClient()
        {
            // Direct downloads are carried by the internal pipeline, which no configured client
            // can replace, so a binding has nothing to say about them.
            var selector = CreateSelector(TrackerBoundTo("qb-seedbox"), Client("qb-seedbox", "qbittorrent"));

            var selected = await selector.GetAppropriateDownloadClientAsync(DownloadProtocol.DirectDownload, TrackerIndexerId);

            Assert.Equal(DirectDownloadMetadataKeys.ClientId, selected);
        }
    }
}
