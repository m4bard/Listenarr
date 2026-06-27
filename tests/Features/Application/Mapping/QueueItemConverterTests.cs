using Listenarr.Application.Mapping;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Mapping
{
    [Trait("Name", "QueueItemConverterTests")]
    [Trait("Category", "QueueItemConverter")]
    public class QueueItemConverterTests : BaseTests
    {
        [Fact]
        [Trait("Method", "UpdateFromQueueItem")]
        public void UpdateFromQueueItem_DoesNotCompleteDownload_WhenClientReportsUnknownSizeWithPartialProgress()
        {
            var download = new Download
            {
                Status = DownloadStatus.Downloading,
                TotalSize = 0,
                DownloadedSize = 0,
                Progress = 10m
            };
            var item = new QueueItem
            {
                Status = "downloading",
                Progress = 10,
                Size = 0,
                Downloaded = 0
            };

            var updated = QueueItemConverter.UpdateFromQueueItem(download, item);

            Assert.Equal(DownloadStatus.Downloading, updated.Status);
            Assert.Equal(10m, updated.Progress);
            Assert.Equal(0L, updated.Metadata["AmountLeft"]);
            Assert.False((bool)updated.Metadata["HasReliableSize"]);
        }

        [Fact]
        [Trait("Method", "UpdateFromQueueItem")]
        public void UpdateFromQueueItem_PreservesDownloadedSize_WhenClientReportsUnknownSize()
        {
            var download = new Download
            {
                Status = DownloadStatus.Downloading,
                TotalSize = 1_000,
                DownloadedSize = 100,
                Progress = 10m
            };
            var item = new QueueItem
            {
                Status = "downloading",
                Progress = 10,
                Size = 0,
                Downloaded = 0
            };

            var updated = QueueItemConverter.UpdateFromQueueItem(download, item);

            Assert.Equal(DownloadStatus.Downloading, updated.Status);
            Assert.Equal(10m, updated.Progress);
            Assert.Equal(1_000L, updated.TotalSize);
            Assert.Equal(100L, updated.DownloadedSize);
            Assert.Equal(0L, updated.Metadata["AmountLeft"]);
            Assert.False((bool)updated.Metadata["HasReliableSize"]);
        }

        [Fact]
        [Trait("Method", "UpdateFromQueueItem")]
        public void UpdateFromQueueItem_DoesNotCompleteDownload_WhenUnknownSizeHasOneHundredProgressButActiveStatus()
        {
            var download = new Download
            {
                Status = DownloadStatus.Downloading,
                TotalSize = 1_000,
                DownloadedSize = 900,
                Progress = 90m
            };
            var item = new QueueItem
            {
                Status = "downloading",
                Progress = 100,
                Size = 0,
                Downloaded = 0
            };

            var updated = QueueItemConverter.UpdateFromQueueItem(download, item);

            Assert.Equal(DownloadStatus.Downloading, updated.Status);
            Assert.Equal(100m, updated.Progress);
            Assert.Equal(900L, updated.DownloadedSize);
            Assert.False((bool)updated.Metadata["HasReliableSize"]);
        }

        [Theory]
        [Trait("Method", "UpdateFromQueueItem")]
        [InlineData("completed")]
        [InlineData("success")]
        public void UpdateFromQueueItem_CompletesDownload_WhenClientReportsExplicitCompletedStateWithUnknownSize(string status)
        {
            var download = new Download
            {
                Status = DownloadStatus.Downloading,
                TotalSize = 1_000,
                DownloadedSize = 900,
                Progress = 90m
            };
            var item = new QueueItem
            {
                Status = status,
                Progress = 100,
                Size = 0,
                Downloaded = 0
            };

            var updated = QueueItemConverter.UpdateFromQueueItem(download, item);

            Assert.Equal(DownloadStatus.Completed, updated.Status);
            Assert.Equal(100m, updated.Progress);
            Assert.Equal(900L, updated.DownloadedSize);
            Assert.False((bool)updated.Metadata["HasReliableSize"]);
        }

        [Fact]
        [Trait("Method", "UpdateFromQueueItem")]
        public void UpdateFromQueueItem_IgnoresInvalidProgress()
        {
            var download = new Download
            {
                Status = DownloadStatus.Downloading,
                TotalSize = 1_000,
                DownloadedSize = 100,
                Progress = 10m
            };
            var item = new QueueItem
            {
                Status = "downloading",
                Progress = double.NaN,
                Size = 0,
                Downloaded = 0
            };

            var updated = QueueItemConverter.UpdateFromQueueItem(download, item);

            Assert.Equal(DownloadStatus.Downloading, updated.Status);
            Assert.Equal(10m, updated.Progress);
            Assert.Equal(100L, updated.DownloadedSize);
            Assert.False((bool)updated.Metadata["HasReliableSize"]);
        }

        [Fact]
        [Trait("Method", "UpdateFromQueueItem")]
        public void UpdateFromQueueItem_CompletesDownload_WhenAmountLeftIsZeroEvenIfProgressIsBelowOneHundred()
        {
            var download = new Download
            {
                Status = DownloadStatus.Downloading,
                TotalSize = 1_000,
                DownloadedSize = 999,
                Progress = 99.9m
            };
            var item = new QueueItem
            {
                Status = "downloading",
                Progress = 99.9,
                Size = 1_000,
                Downloaded = 1_000
            };

            var updated = QueueItemConverter.UpdateFromQueueItem(download, item);

            Assert.Equal(DownloadStatus.Completed, updated.Status);
            Assert.Equal(0L, updated.Metadata["AmountLeft"]);
        }

        [Fact]
        [Trait("Method", "UpdateFromQueueItem")]
        public void UpdateFromQueueItem_ClampsNegativeAmountLeft_WhenClientReportsDownloadedGreaterThanSize()
        {
            var download = new Download
            {
                Status = DownloadStatus.Downloading,
                TotalSize = 1_000,
                DownloadedSize = 999,
                Progress = 99.9m
            };
            var item = new QueueItem
            {
                Status = "downloading",
                Progress = 99.9,
                Size = 1_000,
                Downloaded = 1_001
            };

            var updated = QueueItemConverter.UpdateFromQueueItem(download, item);

            Assert.Equal(DownloadStatus.Completed, updated.Status);
            Assert.Equal(0L, updated.Metadata["AmountLeft"]);
        }
    }
}
