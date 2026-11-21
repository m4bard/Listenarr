using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Downloads.Queue
{
    [Trait("Name", "DownloadOrphanCleanupHistoryFailureTests")]
    [Trait("Category", "DownloadOrphanCleanupService")]
    public sealed class DownloadOrphanCleanupHistoryFailureTests : BaseTests
    {
        [Fact]
        [Trait("Method", "RemoveOrphansAsync")]
        public async Task RemoveOrphansAsync_ContinuesWithRemainingOrphans_WhenHistoryWriteFails()
        {
            var client = CreateClient();
            var first = await AddOrphanAsync("orphan-one");
            var second = await AddOrphanAsync("orphan-two");
            var history = new Mock<IDownloadHistoryService>();
            history
                .Setup(h => h.RecordRemovedAsync(first.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>()))
                .ThrowsAsync(new InvalidOperationException("history store unavailable"));
            var metrics = new Mock<IAppMetricsService>();
            var service = new DownloadOrphanCleanupService(
                _downloadRepository,
                history.Object,
                metrics.Object,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<DownloadOrphanCleanupService>.Instance);

            await service.RemoveOrphansAsync(client, CreateLiveSnapshot(client), [], [first, second]);

            Assert.Null(await _downloadRepository.GetByIdAsync(first.Id));
            Assert.Null(await _downloadRepository.GetByIdAsync(second.Id));
            history.Verify(
                h => h.RecordRemovedAsync(second.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>()),
                Times.Once);
            metrics.Verify(m => m.Increment("download.orphan.unlinked_removed", 2), Times.Once);
        }

        private async Task<Download> AddOrphanAsync(string id)
        {
            var download = new DownloadBuilder()
                .WithId(id)
                .WithStatus(DownloadStatus.Downloading)
                .WithStartDate(DateTime.UtcNow.AddMinutes(-10))
                .WithTitle(id)
                .Build();
            download.DownloadClientId = "client-1";
            download.Metadata = new Dictionary<string, object>();
            return await _downloadRepository.AddAsync(download);
        }

        private static DownloadClientConfiguration CreateClient() => new()
        {
            Id = "client-1",
            Name = "Mock Client",
            Type = "mock",
            IsEnabled = true
        };

        private static ClientQueueFetchResult CreateLiveSnapshot(DownloadClientConfiguration client) => new(
            client,
            [],
            usedCachedSnapshot: false,
            isUnavailable: false,
            snapshotAge: TimeSpan.Zero,
            failureReason: null,
            snapshotState: "live",
            snapshotRefreshedAtUtc: DateTimeOffset.UtcNow);
    }
}
