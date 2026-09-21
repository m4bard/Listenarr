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
using Listenarr.Application.Downloads.Cleanup;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks;

namespace Listenarr.Tests.Features.Application.Downloads.Cleanup
{
    /// <summary>
    /// The workflow resolves a download's client-specific identifier (a torrent hash for the torrent
    /// clients) and sends that to the gateway. When the gateway reports failure it re-reads the
    /// client queue to see whether the item really is gone. That queue comes straight from the
    /// gateway, so it is keyed by the client's identifier and never by the Listenarr download id.
    /// These tests pin that the check is made against the identifier that was actually sent.
    /// </summary>
    [Trait("Area", "Downloads")]
    [Trait("Name", "DownloadRemovalWorkflowTests")]
    [Trait("Category", "DownloadRemovalWorkflow")]
    public class DownloadRemovalWorkflowTests : BaseTests
    {
        private const string TorrentHash = "0123456789abcdef0123456789abcdef01234567";
        private const string DownloadId = "d-still-in-client";

        private readonly DownloadClientGatewayMock _gateway = new();
        private DownloadClientConfiguration _client = new DownloadClientConfigurationBuilder().Build();

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();

            _services.AddSingleton<IDownloadClientGateway>(_gateway);
            Init();

            _client = await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfigurationBuilder()
                .WithId("client-qbit")
                .WithName("Torrent Client")
                .WithType("qbittorrent")
                .Enabled()
                .Build());
        }

        [Fact]
        [Trait("Method", "RemoveAsync")]
        [Trait("Scenario", "RefusedRemovalWhileItemStillInClientQueueKeepsTheRecord")]
        public async Task RemoveAsync_ClientRefuses_AndItemStillInQueueUnderItsHash_FailsAndKeepsRecord()
        {
            await AddDownloadAsync();

            // The client said no, and the item is demonstrably still there under its own hash.
            _gateway.RemoveResult = false;
            _gateway.QueueItems.Add(BuildClientQueueItem(TorrentHash));

            var removed = await ResolveWorkflow().RemoveAsync(DownloadId, _client.Id);

            Assert.False(removed);
            Assert.NotNull(await _downloadRepository.GetByIdAsync(DownloadId));
            Assert.Equal(TorrentHash, Assert.Single(_gateway.RemovedIds));
        }

        [Fact]
        [Trait("Method", "RemoveAsync")]
        [Trait("Scenario", "RefusedRemovalWhenItemIsGenuinelyGoneStillSucceeds")]
        public async Task RemoveAsync_ClientRefuses_ButItemIsGoneFromTheQueue_SucceedsAndRemovesRecord()
        {
            // Control for the test above. Same refusal, same code path, and the only difference is
            // that the client queue no longer holds the hash. If this one failed too, the test above
            // would be passing because removal never succeeds rather than because the check works.
            await AddDownloadAsync();

            _gateway.RemoveResult = false;
            _gateway.QueueItems.Add(BuildClientQueueItem("ffffffffffffffffffffffffffffffffffffffff"));

            var removed = await ResolveWorkflow().RemoveAsync(DownloadId, _client.Id);

            Assert.True(removed);
            Assert.Null(await _downloadRepository.GetByIdAsync(DownloadId));
        }

        [Fact]
        [Trait("Method", "RemoveAsync")]
        [Trait("Scenario", "DeleteCallErrorsWhileClientStillServesItsQueueAndItemIsStillThere")]
        public async Task RemoveAsync_DeleteCallThrows_ButQueueStillServes_AndItemStillThere_FailsAndKeepsRecord()
        {
            // A client that errors on the delete call while still answering its queue: a timeout or
            // an auth failure on that one request, or a client that errors on delete and serves its
            // list fine. That lands in the outer catch, which re-reads the queue the same way and
            // had the same identifier defect. This is narrower than a client that is simply down.
            await AddDownloadAsync();

            _gateway.RemoveException = new InvalidOperationException("delete request failed");
            _gateway.QueueItems.Add(BuildClientQueueItem(TorrentHash));

            var removed = await ResolveWorkflow().RemoveAsync(DownloadId, _client.Id);

            Assert.False(removed);
            Assert.NotNull(await _downloadRepository.GetByIdAsync(DownloadId));
        }

        [Fact]
        [Trait("Method", "RemoveAsync")]
        [Trait("Scenario", "DeleteCallErrorsWhileClientStillServesItsQueueAndItemIsGone")]
        public async Task RemoveAsync_DeleteCallThrows_ButQueueStillServes_AndItemIsGone_SucceedsAndRemovesRecord()
        {
            // Control for the exception path, for the same reason as the control above.
            await AddDownloadAsync();

            _gateway.RemoveException = new InvalidOperationException("delete request failed");
            _gateway.QueueItems.Add(BuildClientQueueItem("ffffffffffffffffffffffffffffffffffffffff"));

            var removed = await ResolveWorkflow().RemoveAsync(DownloadId, _client.Id);

            Assert.True(removed);
            Assert.Null(await _downloadRepository.GetByIdAsync(DownloadId));
        }

        [Fact]
        [Trait("Method", "RemoveAsync")]
        [Trait("Scenario", "ClientAnsweringNothingAtAllAlreadyFailedClosed")]
        public async Task RemoveAsync_ClientAnswersNothing_FailsAndKeepsRecord()
        {
            // Second control, and the one that bounds the claim. A client that is down throws on the
            // delete AND on the verification queue read. It does reach the outer catch, and the catch
            // around the read in there only logs, so control falls through to the return false that
            // follows it. The method already answered false before this change, and the identifier
            // work must not disturb that: this test has to come out the same on both trees.
            await AddDownloadAsync();

            _gateway.RemoveException = new InvalidOperationException("no route to client");
            _gateway.QueueException = new InvalidOperationException("no route to client");

            var removed = await ResolveWorkflow().RemoveAsync(DownloadId, _client.Id);

            Assert.False(removed);
            Assert.NotNull(await _downloadRepository.GetByIdAsync(DownloadId));
        }

        [Fact]
        [Trait("Method", "RemoveAsync")]
        [Trait("Scenario", "ForceSkipsTheClientEntirely")]
        public async Task RemoveAsync_Force_DoesNotContactTheClient_AndRemovesRecord()
        {
            await AddDownloadAsync();

            _gateway.RemoveResult = false;
            _gateway.QueueItems.Add(BuildClientQueueItem(TorrentHash));

            var removed = await ResolveWorkflow().RemoveAsync(DownloadId, _client.Id, force: true);

            Assert.True(removed);
            Assert.Null(await _downloadRepository.GetByIdAsync(DownloadId));
            Assert.Empty(_gateway.RemovedIds);
        }

        private DownloadRemovalWorkflow ResolveWorkflow()
        {
            return _provider.GetRequiredService<DownloadRemovalWorkflow>();
        }

        private async Task<Download> AddDownloadAsync()
        {
            return await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithId(DownloadId)
                .WithTitle("A Download Still Running In The Client")
                .WithStatus(DownloadStatus.Downloading)
                .WithStartDate(DateTime.UtcNow.AddMinutes(-5))
                .WithDownloadClientConfiguration(_client)
                .WithTorrentHash(TorrentHash)
                .Build());
        }

        private QueueItem BuildClientQueueItem(string clientItemId)
        {
            // Shaped like a gateway queue read, where Id is the client's own identifier. The
            // enrichment that swaps in the Listenarr download id happens in DownloadQueueService,
            // one layer above this, and never reaches the gateway queue the workflow reads here.
            return new QueueItem
            {
                Id = clientItemId,
                Title = "A Download Still Running In The Client",
                Status = "downloading",
                DownloadClient = _client.Name,
                DownloadClientId = _client.Id,
                DownloadClientType = _client.Type
            };
        }
    }
}
