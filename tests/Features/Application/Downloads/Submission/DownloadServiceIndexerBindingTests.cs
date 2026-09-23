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
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Downloads.Submission
{
    /// <summary>
    /// The selector only honours a binding if the grab path hands it the indexer. These go
    /// through DownloadService with no client chosen, which is how a grab from the search
    /// page arrives, and check which client the gateway was actually given.
    /// </summary>
    [Trait("Area", "DownloadClientSelection")]
    [Trait("Name", "DownloadServiceIndexerBindingTests")]
    [Trait("Category", "Application")]
    public class DownloadServiceIndexerBindingTests : BaseTests
    {
        private readonly List<string> _clientsGivenToGateway = new();
        private Mock<IDownloadClientGateway> _gateway = new();

        public override async Task InitializeAsync()
        {
            _gateway = new Mock<IDownloadClientGateway>();
            _gateway
                .Setup(g => g.AddAsync(
                    It.IsAny<DownloadClientConfiguration>(),
                    It.IsAny<PreparedDownloadSubmission>(),
                    It.IsAny<CancellationToken>()))
                .Callback<DownloadClientConfiguration, PreparedDownloadSubmission, CancellationToken>(
                    (client, _, _) => _clientsGivenToGateway.Add(client.Id))
                .ReturnsAsync(new DownloadClientSubmissionResult("ABCDEF1234567890ABCDEF1234567890ABCDEF12"));
            _services.AddSingleton(_gateway.Object);

            Init();

            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithOutputPath(Path.GetTempPath())
                .Build());

            await SaveClientAsync("qb-local", priority: 1, enabled: true);
        }

        private Task<DownloadClientConfiguration> SaveClientAsync(string id, int priority, bool enabled) =>
            _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfiguration
            {
                Id = id,
                Name = id,
                Type = "qbittorrent",
                Host = "localhost",
                Port = 8080,
                Priority = priority,
                IsEnabled = enabled
            });

        private Task<Indexer> AddTrackerAsync(string? downloadClientId) =>
            _indexerRepository.AddAsync(new Indexer
            {
                Name = "Private Tracker",
                Type = "Torrent",
                Implementation = "Torznab",
                Url = "https://tracker.example.test",
                IsEnabled = true,
                DownloadClientId = downloadClientId
            });

        private static SearchResult ReleaseFrom(Indexer indexer) =>
            new()
            {
                Title = "The Time Machine",
                Artist = "H. G. Wells",
                DownloadType = "Torrent",
                MagnetLink = "magnet:?xt=urn:btih:ABCDEF1234567890ABCDEF1234567890ABCDEF12&dn=The+Time+Machine",
                Size = 123456789,
                IndexerId = indexer.Id
            };

        [Fact]
        [Trait("Scenario", "ManualGrabHonoursBinding")]
        public async Task AGrabWithNoClientChosen_GoesToTheIndexersBoundClient()
        {
            await SaveClientAsync("qb-seedbox", priority: 10, enabled: true);
            var tracker = await AddTrackerAsync("qb-seedbox");
            var downloadService = _provider.GetRequiredService<DownloadService>();

            await downloadService.SendToDownloadClientAsync(ReleaseFrom(tracker));

            Assert.Equal(new[] { "qb-seedbox" }, _clientsGivenToGateway);
        }

        [Fact]
        [Trait("Scenario", "ManualGrabHonoursBinding")]
        public async Task AGrabFromAnUnboundIndexer_GoesWhereItAlwaysDid()
        {
            // Control: same two clients, no binding, so priority decides as before.
            await SaveClientAsync("qb-seedbox", priority: 10, enabled: true);
            var tracker = await AddTrackerAsync(null);
            var downloadService = _provider.GetRequiredService<DownloadService>();

            await downloadService.SendToDownloadClientAsync(ReleaseFrom(tracker));

            Assert.Equal(new[] { "qb-local" }, _clientsGivenToGateway);
        }

        [Fact]
        [Trait("Scenario", "ManualGrabHonoursBinding")]
        public async Task AGrabFromAnIndexerBoundToADisabledClient_SendsNothing()
        {
            await SaveClientAsync("qb-seedbox", priority: 10, enabled: false);
            var tracker = await AddTrackerAsync("qb-seedbox");
            var downloadService = _provider.GetRequiredService<DownloadService>();

            await Assert.ThrowsAsync<DownloadClientUnavailableException>(
                () => downloadService.SendToDownloadClientAsync(ReleaseFrom(tracker)));

            // Nothing went to qb-local behind the operator's back.
            Assert.Empty(_clientsGivenToGateway);
        }

        [Fact]
        [Trait("Scenario", "ExplicitClientBeatsBinding")]
        public async Task AClientChosenForTheGrab_OverridesTheBinding()
        {
            // Readarr's DownloadService.DownloadReport takes an explicitly chosen client over
            // the indexer's binding (src/NzbDrone.Core/Download/DownloadService.cs:59-61).
            await SaveClientAsync("qb-seedbox", priority: 10, enabled: true);
            var tracker = await AddTrackerAsync("qb-seedbox");
            var downloadService = _provider.GetRequiredService<DownloadService>();

            await downloadService.SendToDownloadClientAsync(ReleaseFrom(tracker), "qb-local");

            Assert.Equal(new[] { "qb-local" }, _clientsGivenToGateway);
        }
    }
}
