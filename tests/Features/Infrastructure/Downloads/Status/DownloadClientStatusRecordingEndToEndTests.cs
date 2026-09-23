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

using System.Net;
using Listenarr.Domain.Downloads.Exceptions;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks.Api;

namespace Listenarr.Tests.Features.Infrastructure.Downloads.Status
{
    /// <summary>
    /// The same distinction as StatusRecordingDownloadClientGatewayTests, through the real
    /// qBittorrent adapter and the gateway as the container composes it, so the exception shapes
    /// asserted there are the ones the adapter actually throws.
    /// </summary>
    [Trait("Area", "DownloadClientStatus")]
    [Trait("Name", "DownloadClientStatusRecordingEndToEndTests")]
    [Trait("Category", "Infrastructure")]
    public class DownloadClientStatusRecordingEndToEndTests : BaseTests
    {
        private async Task<DownloadClientConfiguration> SaveQbitAsync() =>
            await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfigurationBuilder()
                .WithHost("localhost")
                .WithPort(8080)
                .WithUsername("admin")
                .WithPassword("admin")
                .WithType("qbittorrent")
                .Build());

        [Fact]
        [Trait("Scenario", "ReleaseRejected")]
        public async Task A409FromQbittorrent_DoesNotEscalateTheClient()
        {
            var client = await SaveQbitAsync();
            var apiMock = _provider.GetRequiredService<QbittorrentApiMock>();
            apiMock.AddStatusCode = HttpStatusCode.Conflict;
            apiMock.AddResponseBody = "Torrent is already in the download list.";
            var gateway = _provider.GetRequiredService<IDownloadClientGateway>();
            var searchResult = new SearchResult
            {
                Title = "Book",
                MagnetLink = "magnet:?xt=urn:btih:ABCDEF1234567890ABCDEF1234567890ABCDEF12"
            };

            await Assert.ThrowsAnyAsync<DownloadClientSubmissionException>(
                () => gateway.AddAsync(client, PreparedSubmissionTestFactory.Torrent(searchResult)));

            var status = await _provider.GetRequiredService<IDownloadClientStatusRepository>().GetByClientIdAsync(client.Id);
            Assert.Null(status);
        }

        [Fact]
        [Trait("Scenario", "Polling")]
        public async Task AFailedMonitorPoll_EscalatesTheClient()
        {
            // Control for the test above: the same client, the same container, a failure that is
            // about the client. If recording were wired to nothing, both tests would see no row.
            var client = await SaveQbitAsync();
            var apiMock = _provider.GetRequiredService<QbittorrentApiMock>();
            apiMock.InfoStatusCode = HttpStatusCode.Forbidden;
            var gateway = _provider.GetRequiredService<IDownloadClientGateway>();
            var download = new Download { DownloadClientId = client.Id };
            download.Metadata[Download.METADATA_EXTERNAL_ID_KEY] = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";

            await Assert.ThrowsAsync<DownloadClientAdapterPollingException>(
                () => gateway.FetchDownloadsAsync(client, [download]));

            var status = await _provider.GetRequiredService<IDownloadClientStatusRepository>().GetByClientIdAsync(client.Id);
            Assert.NotNull(status);
            Assert.Equal(1, status!.EscalationLevel);
        }
    }
}
