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
using Listenarr.Tests.Mocks.Api;

namespace Listenarr.Tests.Features.Infrastructure.DownloadClients.Qbittorrent
{
    /// <summary>
    /// qBittorrent's /torrents/add answers 200 with the body "Fails." when it will not take the
    /// torrent. The status alone is not an acceptance, and on Web API below 2.14.0 the body is
    /// the only signal there is.
    /// </summary>
    [Trait("Area", "DownloadClients")]
    [Trait("Name", "QbittorrentAddFailsBodyTests")]
    [Trait("Category", "Qbittorrent")]
    public sealed class QbittorrentAddFailsBodyTests : BaseTests
    {
        private async Task<DownloadClientConfiguration> QbittorrentClient()
        {
            return await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfigurationBuilder()
                .WithHost("localhost")
                .WithPort(8080)
                .WithUsername("admin")
                .WithPassword("admin")
                .WithType("qbittorrent")
                .Build());
        }

        private static PreparedTorrentSubmission Submission() =>
            PreparedSubmissionTestFactory.Torrent(new SearchResult
            {
                Title = "Book",
                MagnetLink = "magnet:?xt=urn:btih:ABCDEF1234567890ABCDEF1234567890ABCDEF12"
            });

        [Fact]
        [Trait("Scenario", "A 200 carrying Fails. is a refusal, not a grab")]
        public async Task AddAsync_WhenTheClientAnswersFails_RaisesASubmissionFailure()
        {
            // Without this, the workflow returns the info-hash it computed locally, the caller
            // records a successful grab, and Listenarr tracks a download the client never took.
            // It never progresses and nothing in the log says why.
            var apiMock = _provider.GetRequiredService<QbittorrentApiMock>();
            apiMock.AddSuccessResponseBody = "Fails.";
            var client = await QbittorrentClient();
            var gateway = _provider.GetRequiredService<IDownloadClientGateway>();

            var exception = await Assert.ThrowsAsync<DownloadClientSubmissionException>(
                () => gateway.AddAsync(client, Submission()));

            Assert.Contains("Fails.", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Scenario", "Trailing whitespace does not hide the refusal")]
        public async Task AddAsync_WhenTheFailsBodyCarriesWhitespace_StillRaisesASubmissionFailure()
        {
            var apiMock = _provider.GetRequiredService<QbittorrentApiMock>();
            apiMock.AddSuccessResponseBody = "Fails.\n";
            var client = await QbittorrentClient();
            var gateway = _provider.GetRequiredService<IDownloadClientGateway>();

            await Assert.ThrowsAsync<DownloadClientSubmissionException>(
                () => gateway.AddAsync(client, Submission()));
        }

        [Fact]
        [Trait("Scenario", "An acceptance is still an acceptance")]
        public async Task AddAsync_WhenTheClientAnswersOk_StillSucceeds()
        {
            // The first control. A check written against the wrong string, or one that treats any
            // non-empty body as a refusal, would fail every grab rather than the refused ones.
            var apiMock = _provider.GetRequiredService<QbittorrentApiMock>();
            apiMock.AddSuccessResponseBody = "Ok.";
            var client = await QbittorrentClient();
            var gateway = _provider.GetRequiredService<IDownloadClientGateway>();

            var result = await gateway.AddAsync(client, Submission());

            Assert.Equal("ABCDEF1234567890ABCDEF1234567890ABCDEF12", result.ExternalId);
        }

        [Fact]
        [Trait("Scenario", "An empty body is an acceptance on older Web API versions")]
        public async Task AddAsync_WhenTheClientAnswersNothing_StillSucceeds()
        {
            // The second control, and the reason the test is against "Fails." rather than for
            // "Ok.". Older qBittorrent builds answer 200 with an empty body on success, so a
            // positive test for "Ok." would break every grab against them.
            var apiMock = _provider.GetRequiredService<QbittorrentApiMock>();
            apiMock.AddSuccessResponseBody = string.Empty;
            var client = await QbittorrentClient();
            var gateway = _provider.GetRequiredService<IDownloadClientGateway>();

            var result = await gateway.AddAsync(client, Submission());

            Assert.Equal("ABCDEF1234567890ABCDEF1234567890ABCDEF12", result.ExternalId);
        }
    }
}
