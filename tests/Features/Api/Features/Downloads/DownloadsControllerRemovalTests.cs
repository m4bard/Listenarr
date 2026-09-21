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
using Listenarr.Tests.Mocks;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Tests.Features.Api.Features.Downloads
{
    /// <summary>
    /// The three delete endpoints on DownloadsController used to touch the database only, so a
    /// delete in the UI left the item running in the download client. They now route through
    /// IDownloadService.RemoveFromQueueAsync, with removeFromClient defaulting to true the way
    /// Readarr, Sonarr and Radarr do it.
    /// </summary>
    [Trait("Area", "DownloadsApi")]
    [Trait("Name", "DownloadsControllerRemovalTests")]
    [Trait("Category", "DownloadsController")]
    public class DownloadsControllerRemovalTests : BaseTests
    {
        private const string FirstHash = "1111111111111111111111111111111111111111";
        private const string SecondHash = "2222222222222222222222222222222222222222";
        private const string ThirdHash = "3333333333333333333333333333333333333333";

        private readonly DownloadClientGatewayMock _gateway = new();
        private DownloadClientConfiguration _client = new DownloadClientConfigurationBuilder().Build();
        private DownloadClientConfiguration _disabledClient = new DownloadClientConfigurationBuilder().Build();

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

            _disabledClient = await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfigurationBuilder()
                .WithId("client-qbit-off")
                .WithName("Switched Off Client")
                .WithType("qbittorrent")
                .Disabled()
                .Build());
        }

        [Fact]
        [Trait("Method", "DeleteDownload")]
        [Trait("Scenario", "DefaultDeleteAlsoRemovesTheItemFromTheDownloadClient")]
        public async Task DeleteDownload_ByDefault_RemovesFromClientAndDropsTheRecord()
        {
            await AddDownloadAsync("d-one", FirstHash, DownloadStatus.Downloading);
            _gateway.RemoveResult = true;

            var controller = MockUtils.CreateDownloadsController(_provider);
            var action = await controller.DeleteDownload("d-one");

            Assert.IsType<OkObjectResult>(action);
            Assert.Equal(FirstHash, Assert.Single(_gateway.RemovedIds));
            Assert.Null(await _downloadRepository.GetByIdAsync("d-one"));

            // The invariant with the worst failure mode in this change. Deleting a record must cancel
            // the download, never delete what has already landed on disk. deleteFiles reaches the
            // client as false, and LastRemoveDeleteFiles is a bool? that starts null, so this also
            // fails if no call was made at all rather than passing on an absent one.
            Assert.False(_gateway.LastRemoveDeleteFiles);
        }

        [Fact]
        [Trait("Method", "DeleteDownload")]
        [Trait("Scenario", "RemoveFromClientFalseLeavesTheClientAloneAndStillDropsTheRecord")]
        public async Task DeleteDownload_RemoveFromClientFalse_DoesNotContactTheClient_AndDropsTheRecord()
        {
            // Control for the test above. Same download, same endpoint, and the only difference is
            // the flag. If the flag were ignored the gateway would have been called here too, so a
            // gateway with no calls recorded is what proves the flag is read rather than accepted.
            await AddDownloadAsync("d-one", FirstHash, DownloadStatus.Downloading);
            _gateway.RemoveResult = true;

            var controller = MockUtils.CreateDownloadsController(_provider);
            var action = await controller.DeleteDownload("d-one", removeFromClient: false);

            Assert.IsType<OkObjectResult>(action);
            Assert.Empty(_gateway.RemovedIds);
            Assert.Null(await _downloadRepository.GetByIdAsync("d-one"));

            // Control for the deleteFiles guard above as well as for the flag. Nothing was asked of
            // the client here, so the recorded value is absent rather than false, which is what makes
            // the false in the other test evidence of a real call.
            Assert.Null(_gateway.LastRemoveDeleteFiles);
        }

        [Fact]
        [Trait("Method", "DeleteDownload")]
        [Trait("Scenario", "ClientRefusalKeepsTheRecordAndAnswers409")]
        public async Task DeleteDownload_ClientRefusesAndItemIsStillThere_KeepsTheRecord_AndReturnsConflict()
        {
            // The chosen behaviour, stated rather than implied: when the client will not confirm the
            // removal, the record stays. Deleting it is the orphan this change exists to prevent, so
            // the caller is told to retry with removeFromClient=false if the record is what they want
            // gone.
            await AddDownloadAsync("d-one", FirstHash, DownloadStatus.Downloading);
            _gateway.RemoveResult = false;
            _gateway.QueueItems.Add(BuildClientQueueItem(FirstHash));

            var controller = MockUtils.CreateDownloadsController(_provider);
            var action = await controller.DeleteDownload("d-one");

            var conflict = Assert.IsType<ConflictObjectResult>(action);
            Assert.Contains("removeFromClient=false", GetStringProperty(conflict.Value, "message"));
            Assert.NotNull(await _downloadRepository.GetByIdAsync("d-one"));
        }

        [Fact]
        [Trait("Method", "ClearCompletedDownloads")]
        [Trait("Scenario", "BulkClearReachesTheClientForEveryItemNotOnlyTheFirst")]
        public async Task ClearCompletedDownloads_ByDefault_RemovesEveryItemFromTheClient()
        {
            await AddDownloadAsync("d-one", FirstHash, DownloadStatus.Completed);
            await AddDownloadAsync("d-two", SecondHash, DownloadStatus.Completed);
            await AddDownloadAsync("d-three", ThirdHash, DownloadStatus.Completed);
            _gateway.RemoveResult = true;

            var controller = MockUtils.CreateDownloadsController(_provider);
            var action = await controller.ClearCompletedDownloads();

            var ok = Assert.IsType<OkObjectResult>(action);
            Assert.Equal(3, GetIntProperty(ok.Value, "count"));
            Assert.Equal(0, GetIntProperty(ok.Value, "kept"));

            Assert.Equal(
                new[] { FirstHash, SecondHash, ThirdHash }.OrderBy(hash => hash),
                _gateway.RemovedIds.OrderBy(hash => hash));

            Assert.False(_gateway.LastRemoveDeleteFiles);

            var remaining = (await _downloadRepository.GetAllAsync()).Select(download => download.Id).ToList();
            Assert.Empty(remaining);
        }

        [Fact]
        [Trait("Method", "ClearCompletedDownloads")]
        [Trait("Scenario", "BulkClearWithoutClientRemovalTouchesNoClientAtAll")]
        public async Task ClearCompletedDownloads_RemoveFromClientFalse_DoesNotContactTheClient()
        {
            // Control for the bulk default, matching the single-item control.
            await AddDownloadAsync("d-one", FirstHash, DownloadStatus.Completed);
            await AddDownloadAsync("d-two", SecondHash, DownloadStatus.Completed);
            _gateway.RemoveResult = true;

            var controller = MockUtils.CreateDownloadsController(_provider);
            var action = await controller.ClearCompletedDownloads(removeFromClient: false);

            var ok = Assert.IsType<OkObjectResult>(action);
            Assert.Equal(2, GetIntProperty(ok.Value, "count"));
            Assert.Empty(_gateway.RemovedIds);
            Assert.Empty(await _downloadRepository.GetAllAsync());
        }

        [Fact]
        [Trait("Method", "ClearFailedDownloads")]
        [Trait("Scenario", "OneClientRefusalDoesNotAbortTheSweepAndDoesNotDropThatRecord")]
        public async Task ClearFailedDownloads_OneItemRefusedByTheClient_ClearsTheRestAndKeepsThatRecord()
        {
            await AddDownloadAsync("d-one", FirstHash, DownloadStatus.Failed);
            await AddDownloadAsync("d-two", SecondHash, DownloadStatus.Failed);
            await AddDownloadAsync("d-three", ThirdHash, DownloadStatus.ImportBlocked);

            // The client refuses every delete. Only the second item is genuinely still in its queue,
            // so the other two are correctly treated as already gone and the second one is not.
            _gateway.RemoveResult = false;
            _gateway.QueueItems.Add(BuildClientQueueItem(SecondHash));

            var controller = MockUtils.CreateDownloadsController(_provider);
            var action = await controller.ClearFailedDownloads();

            var ok = Assert.IsType<OkObjectResult>(action);
            Assert.Equal(2, GetIntProperty(ok.Value, "count"));
            Assert.Equal(1, GetIntProperty(ok.Value, "kept"));

            var keptIds = ok.Value!.GetType().GetProperty("keptIds")?.GetValue(ok.Value) as IEnumerable<string>;
            Assert.NotNull(keptIds);
            Assert.Equal("d-two", Assert.Single(keptIds!));

            // Every item was attempted. A sweep that stopped at the first refusal would show fewer.
            Assert.Equal(3, _gateway.RemovedIds.Count);

            var remaining = (await _downloadRepository.GetAllAsync()).Select(download => download.Id).ToList();
            Assert.Equal("d-two", Assert.Single(remaining));
        }

        [Fact]
        [Trait("Method", "DeleteDownload")]
        [Trait("Scenario", "ARecordOnADisabledClientIsStillDeletable")]
        public async Task DeleteDownload_ClientIsDisabled_DropsTheRecord_WithoutContactingTheClient()
        {
            // A disabled client cannot refuse a removal, because nothing is asked of it. Answering 409
            // here made the record undeletable: the message names removeFromClient=false and the UI has
            // no way to send it, and every later bulk clear would skip the record for the same reason.
            await AddDownloadAsync("d-off", FirstHash, DownloadStatus.Failed, client: _disabledClient);
            _gateway.RemoveResult = false;

            var controller = MockUtils.CreateDownloadsController(_provider);
            var action = await controller.DeleteDownload("d-off");

            Assert.IsType<OkObjectResult>(action);
            Assert.Null(await _downloadRepository.GetByIdAsync("d-off"));
            Assert.Empty(_gateway.RemovedIds);
        }

        [Fact]
        [Trait("Method", "ClearFailedDownloads")]
        [Trait("Scenario", "ABulkClearDoesNotStrandRecordsOnDisabledClients")]
        public async Task ClearFailedDownloads_RecordOnADisabledClient_IsClearedLikeTheRest()
        {
            // The same case in the shape that keeps costing something. A record the sweep will not
            // take is not skipped once, it is skipped every time the user clears, forever.
            await AddDownloadAsync("d-on", FirstHash, DownloadStatus.Failed);
            await AddDownloadAsync("d-off", SecondHash, DownloadStatus.Failed, client: _disabledClient);
            _gateway.RemoveResult = true;

            var controller = MockUtils.CreateDownloadsController(_provider);
            var action = await controller.ClearFailedDownloads();

            var ok = Assert.IsType<OkObjectResult>(action);
            Assert.Equal(2, GetIntProperty(ok.Value, "count"));
            Assert.Equal(0, GetIntProperty(ok.Value, "kept"));
            Assert.Empty(await _downloadRepository.GetAllAsync());

            // Only the enabled client was contacted. The disabled one is skipped, not called and
            // ignored, so the record going is a decision rather than a lucky answer.
            Assert.Equal(FirstHash, Assert.Single(_gateway.RemovedIds));
        }

        [Fact]
        [Trait("Method", "DeleteDownload")]
        [Trait("Scenario", "ARecordWithNoClientRecordedSweepsEveryEnabledClient")]
        public async Task DeleteDownload_BlankDownloadClientId_SweepsTheEnabledClients_AndDropsTheRecord()
        {
            // A record that never captured which client holds it. The controller passes null rather
            // than the empty string so the workflow sweeps, and the torrent hash still resolves, so
            // the client receives the identifier it knows the item by.
            await AddDownloadAsync("d-noclient", FirstHash, DownloadStatus.Failed, withoutClientId: true);
            _gateway.RemoveResult = true;

            var controller = MockUtils.CreateDownloadsController(_provider);
            var action = await controller.DeleteDownload("d-noclient");

            Assert.IsType<OkObjectResult>(action);
            Assert.Equal(FirstHash, Assert.Single(_gateway.RemovedIds));
            Assert.False(_gateway.LastRemoveDeleteFiles);
            Assert.Null(await _downloadRepository.GetByIdAsync("d-noclient"));
        }

        [Fact]
        [Trait("Method", "DeleteDownload")]
        [Trait("Scenario", "ARecordWithNeitherAClientNorAMappingCannotBeVerifiedAndIsKept")]
        public async Task DeleteDownload_BlankDownloadClientId_AndNoTorrentHash_KeepsTheRecord()
        {
            // The worst-mapped record there is: no client recorded and no client identifier either.
            // The sweep sends the Listenarr id to every enabled client, nothing in any queue can equal
            // it, and reading that as "already gone" deleted the row while the item kept running. It
            // is kept instead, and the caller is told the client did not confirm.
            await AddDownloadAsync("d-nometa", torrentHash: null, DownloadStatus.Failed, withoutClientId: true);
            _gateway.RemoveResult = false;
            _gateway.QueueItems.Add(BuildClientQueueItem(FirstHash));

            var controller = MockUtils.CreateDownloadsController(_provider);
            var action = await controller.DeleteDownload("d-nometa");

            Assert.IsType<ConflictObjectResult>(action);
            Assert.NotNull(await _downloadRepository.GetByIdAsync("d-nometa"));
            Assert.Equal("d-nometa", Assert.Single(_gateway.RemovedIds));
        }

        private async Task<Download> AddDownloadAsync(
            string id,
            string? torrentHash,
            DownloadStatus status,
            DownloadClientConfiguration? client = null,
            bool withoutClientId = false)
        {
            var builder = new DownloadBuilder()
                .WithId(id)
                .WithTitle($"Download {id}")
                .WithStartDate(DateTime.UtcNow.AddMinutes(-5));

            if (!withoutClientId)
            {
                builder = builder.WithDownloadClientConfiguration(client ?? _client);
            }

            if (torrentHash != null)
            {
                builder = builder.WithTorrentHash(torrentHash);
            }

            builder = status == DownloadStatus.Completed
                ? builder.WithCompletedStatus(DateTime.UtcNow)
                : builder.WithStatus(status);

            return await _downloadRepository.AddAsync(builder.Build());
        }

        private QueueItem BuildClientQueueItem(string clientItemId)
        {
            // A gateway queue read is keyed by the client's own identifier, never by the Listenarr
            // download id, so these stand in under their torrent hash.
            return new QueueItem
            {
                Id = clientItemId,
                Title = "Still in the client",
                Status = "downloading",
                DownloadClient = _client.Name,
                DownloadClientId = _client.Id,
                DownloadClientType = _client.Type
            };
        }

        private static int GetIntProperty(object? payload, string name)
        {
            Assert.NotNull(payload);
            var value = payload!.GetType().GetProperty(name)?.GetValue(payload);
            return value is int number ? number : Convert.ToInt32(value);
        }

        private static string GetStringProperty(object? payload, string name)
        {
            Assert.NotNull(payload);
            return payload!.GetType().GetProperty(name)?.GetValue(payload)?.ToString() ?? string.Empty;
        }
    }
}
