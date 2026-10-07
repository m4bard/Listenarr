using Listenarr.Application.Downloads.Cleanup;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Downloads.Cleanup
{
    [Trait("Name", "DownloadRemovalWorkflowTests")]
    [Trait("Category", "DownloadRemovalWorkflow")]
    public sealed class DownloadRemovalWorkflowTests : BaseTests
    {
        public override async Task InitializeAsync()
        {
            // The default test DI setup registers IDownloadHistoryService as a bare
            // Mock<IDownloadHistoryService>() (see ServiceCollectionBuilder), which silently
            // no-ops any Record*Async call. This suite asserts on real persisted History rows,
            // so it needs the real implementation wired to the same (in-memory) DbContext as
            // _historyRepository. Same swap item #325 made for DownloadOrphanCleanupServiceTests.
            Init(services => services
                .WithScoped<IDownloadHistoryService, DownloadHistoryService>());

            await base.InitializeAsync();
        }

        [Fact]
        [Trait("Method", "RemoveAsync")]
        public async Task RemoveAsync_RecordsHistoryEntry_WhenForceRemovingDownload()
        {
            var download = await AddDownloadAsync(
                id: "force-removed-download",
                clientId: "some-client",
                audiobookId: 42);
            var workflow = _provider.GetRequiredService<DownloadRemovalWorkflow>();

            var removed = await workflow.RemoveAsync(download.Id, downloadClientId: null, force: true);

            Assert.True(removed);
            Assert.Null(await _downloadRepository.GetByIdAsync(download.Id));

            var historyEntries = await GetHistoryForDownloadAsync(download.Id);
            var removedEntry = Assert.Single(historyEntries);
            Assert.Equal(HistoryEvents.Removed, removedEntry.EventType);
            Assert.Equal(42, removedEntry.AudiobookId);
            Assert.False(string.IsNullOrWhiteSpace(removedEntry.Message));
        }

        [Fact]
        [Trait("Method", "RemoveAsync")]
        public async Task RemoveAsync_RecordsHistoryEntry_WhenRemovingDdlDownloadWithoutForce()
        {
            // Mirrors the real DELETE api/v{version}/download/queue/{downloadId}?downloadClientId=DDL
            // call for a direct-download item: no external client to contact, force is not set,
            // and removal still succeeds via the DDL short-circuit in RemoveAsync.
            var download = await AddDownloadAsync(
                id: "ddl-removed-download",
                clientId: DirectDownloadMetadataKeys.ClientId,
                audiobookId: 7);
            var workflow = _provider.GetRequiredService<DownloadRemovalWorkflow>();

            var removed = await workflow.RemoveAsync(
                download.Id,
                downloadClientId: DirectDownloadMetadataKeys.ClientId,
                force: false);

            Assert.True(removed);
            Assert.Null(await _downloadRepository.GetByIdAsync(download.Id));

            var historyEntries = await GetHistoryForDownloadAsync(download.Id);
            var removedEntry = Assert.Single(historyEntries);
            Assert.Equal(HistoryEvents.Removed, removedEntry.EventType);
            Assert.Equal(7, removedEntry.AudiobookId);
            Assert.False(string.IsNullOrWhiteSpace(removedEntry.Message));
        }

        [Fact]
        [Trait("Method", "RemoveAsync")]
        public async Task RemoveAsync_DoesNotRecordHistory_WhenClientRemovalFails()
        {
            // Control: the download's external ID ("1") matches one of
            // DownloadCLientAdapterMock's fixed queue items, so its RemoveAsync throws
            // NotImplementedException, the queue-reconciliation fallback finds the item is
            // still present, and RemoveFromClientAsync reports failure. No DB row should be
            // removed and no History row should appear -- the write in RemoveAsync must stay
            // conditioned on an actual removal, not fire unconditionally up front.
            var client = await _downloadClientConfigurationRepository.SaveAsync(
                new DownloadClientConfigurationBuilder()
                    .WithType("mock")
                    .Enabled()
                    .Build());
            var download = await AddDownloadAsync(
                id: "1",
                clientId: client.Id,
                audiobookId: 99);
            var workflow = _provider.GetRequiredService<DownloadRemovalWorkflow>();

            var removed = await workflow.RemoveAsync(download.Id, downloadClientId: client.Id, force: false);

            Assert.False(removed);
            Assert.NotNull(await _downloadRepository.GetByIdAsync(download.Id));
            Assert.Empty(await GetHistoryForDownloadAsync(download.Id));
        }

        private async Task<Download> AddDownloadAsync(
            string id,
            string clientId,
            int? audiobookId = null)
        {
            var builder = new DownloadBuilder()
                .WithId(id)
                .WithStatus(DownloadStatus.Downloading)
                .WithTitle(id);

            if (audiobookId.HasValue)
            {
                builder.WithAudiobookId(audiobookId.Value);
            }

            var download = builder.Build();
            download.DownloadClientId = clientId;

            return await _downloadRepository.AddAsync(download);
        }

        private async Task<List<History>> GetHistoryForDownloadAsync(string downloadId) =>
            (await _historyRepository.QueryAsync(new HistoryQuery
            {
                DownloadId = downloadId.ToUpperInvariant(),
                Limit = 50
            })).Records;
    }
}
