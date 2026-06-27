using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Downloads.Queue
{
    [Trait("Name", "DownloadOrphanCleanupServiceTests")]
    [Trait("Category", "DownloadOrphanCleanupService")]
    public sealed class DownloadOrphanCleanupServiceTests
    {
        [Fact]
        [Trait("Method", "RemoveOrphansAsync")]
        public async Task RemoveOrphansAsync_RemovesOldActiveNonDdlDownloadWithoutExternalId()
        {
            var client = CreateClient();
            var download = CreateDownload(
                id: "missing-external-id",
                clientId: client.Id,
                status: DownloadStatus.Downloading,
                startedAt: DateTime.UtcNow.AddMinutes(-10));
            var (service, repository, metrics) = CreateService();

            await service.RemoveOrphansAsync(
                client,
                CreateLiveSnapshot(client, [new QueueItem { Id = "other-live-item" }]),
                [new QueueItem { Id = "other-live-item" }],
                [download]);

            repository.Verify(r => r.RemoveAsync(download.Id), Times.Once);
            metrics.Verify(m => m.Increment("download.orphan.unlinked_removed", 1), Times.Once);
            metrics.Verify(m => m.Increment("download.orphan.removed", It.IsAny<double>()), Times.Never);
        }

        [Fact]
        [Trait("Method", "RemoveOrphansAsync")]
        public async Task RemoveOrphansAsync_DoesNotRemoveDdlDownloadWithoutExternalId()
        {
            var client = CreateClient();
            var download = CreateDownload(
                id: "ddl-download",
                clientId: "DDL",
                status: DownloadStatus.Downloading,
                startedAt: DateTime.UtcNow.AddMinutes(-10));
            var (service, repository, metrics) = CreateService();

            await service.RemoveOrphansAsync(
                client,
                CreateLiveSnapshot(client, [new QueueItem { Id = "other-live-item" }]),
                [new QueueItem { Id = "other-live-item" }],
                [download]);

            repository.Verify(r => r.RemoveAsync(It.IsAny<string>()), Times.Never);
            metrics.Verify(m => m.Increment(It.IsAny<string>(), It.IsAny<double>()), Times.Never);
        }

        [Fact]
        [Trait("Method", "RemoveOrphansAsync")]
        public async Task RemoveOrphansAsync_DoesNotRemoveRecentNonDdlDownloadWithoutExternalId()
        {
            var client = CreateClient();
            var download = CreateDownload(
                id: "recent-download",
                clientId: client.Id,
                status: DownloadStatus.Downloading,
                startedAt: DateTime.UtcNow.AddMinutes(-1));
            var (service, repository, metrics) = CreateService();

            await service.RemoveOrphansAsync(
                client,
                CreateLiveSnapshot(client, [new QueueItem { Id = "other-live-item" }]),
                [new QueueItem { Id = "other-live-item" }],
                [download]);

            repository.Verify(r => r.RemoveAsync(It.IsAny<string>()), Times.Never);
            metrics.Verify(m => m.Increment(It.IsAny<string>(), It.IsAny<double>()), Times.Never);
        }

        [Fact]
        [Trait("Method", "RemoveOrphansAsync")]
        public async Task RemoveOrphansAsync_DoesNotRemoveIdlessDownloadWhenLiveSnapshotIsEmpty()
        {
            var client = CreateClient();
            var download = CreateDownload(
                id: "empty-snapshot-download",
                clientId: client.Id,
                status: DownloadStatus.Downloading,
                startedAt: DateTime.UtcNow.AddMinutes(-10));
            var (service, repository, metrics) = CreateService();

            await service.RemoveOrphansAsync(
                client,
                CreateLiveSnapshot(client, []),
                [],
                [download]);

            repository.Verify(r => r.RemoveAsync(It.IsAny<string>()), Times.Never);
            metrics.Verify(m => m.Increment(It.IsAny<string>(), It.IsAny<double>()), Times.Never);
        }

        [Theory]
        [Trait("Method", "RemoveOrphansAsync")]
        [InlineData(DownloadStatus.Completed)]
        [InlineData(DownloadStatus.Processing)]
        [InlineData(DownloadStatus.ImportPending)]
        [InlineData(DownloadStatus.Moved)]
        [InlineData(DownloadStatus.Failed)]
        public async Task RemoveOrphansAsync_DoesNotRemoveProtectedStatusesWithoutExternalId(DownloadStatus status)
        {
            var client = CreateClient();
            var download = CreateDownload(
                id: $"protected-{status}",
                clientId: client.Id,
                status: status,
                startedAt: DateTime.UtcNow.AddMinutes(-10));
            var (service, repository, metrics) = CreateService();

            await service.RemoveOrphansAsync(
                client,
                CreateLiveSnapshot(client, [new QueueItem { Id = "other-live-item" }]),
                [new QueueItem { Id = "other-live-item" }],
                [download]);

            repository.Verify(r => r.RemoveAsync(It.IsAny<string>()), Times.Never);
            metrics.Verify(m => m.Increment(It.IsAny<string>(), It.IsAny<double>()), Times.Never);
        }

        [Fact]
        [Trait("Method", "RemoveOrphansAsync")]
        public async Task RemoveOrphansAsync_RemovesKnownExternalIdMissingFromLiveSnapshot()
        {
            var client = CreateClient();
            var download = CreateDownload(
                id: "known-orphan",
                clientId: client.Id,
                status: DownloadStatus.Downloading,
                startedAt: DateTime.UtcNow.AddMinutes(-10),
                metadata: new Dictionary<string, object>
                {
                    ["ClientDownloadId"] = "missing-client-id"
                });
            var (service, repository, metrics) = CreateService();

            await service.RemoveOrphansAsync(
                client,
                CreateLiveSnapshot(client, [new QueueItem { Id = "other-live-item" }]),
                [new QueueItem { Id = "other-live-item" }],
                [download]);

            repository.Verify(r => r.RemoveAsync(download.Id), Times.Once);
            metrics.Verify(m => m.Increment("download.orphan.removed", 1), Times.Once);
            metrics.Verify(m => m.Increment("download.orphan.unlinked_removed", It.IsAny<double>()), Times.Never);
        }

        [Theory]
        [Trait("Method", "RemoveOrphansAsync")]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task RemoveOrphansAsync_DoesNotRemoveWhenSnapshotIsNotTrusted(bool usedCachedSnapshot, bool isUnavailable)
        {
            var client = CreateClient();
            var download = CreateDownload(
                id: "untrusted-snapshot-download",
                clientId: client.Id,
                status: DownloadStatus.Downloading,
                startedAt: DateTime.UtcNow.AddMinutes(-10));
            var (service, repository, metrics) = CreateService();

            await service.RemoveOrphansAsync(
                client,
                new ClientQueueFetchResult(
                    client,
                    [new QueueItem { Id = "other-live-item" }],
                    usedCachedSnapshot,
                    isUnavailable,
                    snapshotAge: null,
                    failureReason: isUnavailable ? "unavailable" : null,
                    snapshotState: isUnavailable ? "unavailable" : "cached",
                    snapshotRefreshedAtUtc: DateTimeOffset.UtcNow),
                [new QueueItem { Id = "other-live-item" }],
                [download]);

            repository.Verify(r => r.RemoveAsync(It.IsAny<string>()), Times.Never);
            metrics.Verify(m => m.Increment(It.IsAny<string>(), It.IsAny<double>()), Times.Never);
        }

        private static (DownloadOrphanCleanupService Service, Mock<IDownloadRepository> Repository, Mock<IAppMetricsService> Metrics) CreateService()
        {
            var repository = new Mock<IDownloadRepository>();
            repository.Setup(r => r.RemoveAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
            var metrics = new Mock<IAppMetricsService>();
            var service = new DownloadOrphanCleanupService(
                repository.Object,
                metrics.Object,
                NullLogger<DownloadOrphanCleanupService>.Instance);

            return (service, repository, metrics);
        }

        private static DownloadClientConfiguration CreateClient() => new()
        {
            Id = "client-1",
            Name = "Mock Client",
            Type = "mock",
            IsEnabled = true
        };

        private static Download CreateDownload(
            string id,
            string clientId,
            DownloadStatus status,
            DateTime startedAt,
            Dictionary<string, object>? metadata = null) => new()
            {
                Id = id,
                DownloadClientId = clientId,
                Title = id,
                Status = status,
                StartedAt = startedAt,
                Metadata = metadata ?? new Dictionary<string, object>()
            };

        private static ClientQueueFetchResult CreateLiveSnapshot(
            DownloadClientConfiguration client,
            List<QueueItem> queueItems) => new(
                client,
                queueItems,
                usedCachedSnapshot: false,
                isUnavailable: false,
                snapshotAge: TimeSpan.Zero,
                failureReason: null,
                snapshotState: "live",
                snapshotRefreshedAtUtc: DateTimeOffset.UtcNow);
    }
}
