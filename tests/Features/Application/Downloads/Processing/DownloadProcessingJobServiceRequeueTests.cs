using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Downloads.Processing
{
    [Trait("Name", "DownloadProcessingJobServiceRequeueTests")]
    [Trait("Category", "DownloadProcessingJob")]
    public class DownloadProcessingJobServiceRequeueTests : BaseTests
    {
        [Fact]
        public async Task Requeue_RevivesTheFailedJobWithAFreshRetryBoundAndKeepsItsCheckpoints()
        {
            var download = await AddDownloadAsync();
            var job = new DownloadProcessingJobBuilder().WithDownload(download).WithFailed(DateTime.UtcNow).Build();
            job.RetryCount = job.MaxRetries;
            job.ErrorMessage = "Max retries (3) exceeded";
            job.NextRetryAt = DateTime.UtcNow.AddMinutes(8);
            job.SetCheckpoint("FilesImported", 1);
            await _downloadProcessingJobRepository.AddAsync(job);

            var id = await _provider.GetRequiredService<IDownloadProcessingJobService>().RequeueAsync(download);

            Assert.Equal(job.Id, id);
            var saved = (await _downloadProcessingJobRepository.GetByIdAsync(job.Id))!;
            Assert.Equal(ProcessingJobStatus.Pending, saved.Status);
            Assert.Equal(0, saved.RetryCount);
            Assert.Null(saved.NextRetryAt);
            Assert.Null(saved.ErrorMessage);
            Assert.Null(saved.CompletedAt);
            Assert.True(saved.HasCheckpoint("FilesImported"));
        }

        [Fact]
        public async Task Requeue_WithAnActiveJob_ReturnsItWithoutCreatingAnother()
        {
            var download = await AddDownloadAsync();
            var active = await _downloadProcessingJobRepository.AddAsync(
                new DownloadProcessingJobBuilder().WithDownload(download).Build());

            var id = await _provider.GetRequiredService<IDownloadProcessingJobService>().RequeueAsync(download);

            Assert.Equal(active.Id, id);
            Assert.Single(await _downloadProcessingJobRepository.GetByDownloadIdAsync(download.Id));
        }

        [Fact]
        public async Task Requeue_WithoutAnyJob_CreatesAPendingOne()
        {
            var download = await AddDownloadAsync();

            var id = await _provider.GetRequiredService<IDownloadProcessingJobService>().RequeueAsync(download);

            var saved = (await _downloadProcessingJobRepository.GetByIdAsync(id))!;
            Assert.Equal(ProcessingJobStatus.Pending, saved.Status);
            Assert.Equal(download.Id, saved.DownloadId);
        }

        private async Task<Download> AddDownloadAsync() =>
            await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(await CreateAudiobook())
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithStatus(DownloadStatus.ImportPending)
                .Build());
    }
}
