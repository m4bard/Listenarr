using Microsoft.AspNetCore.Mvc;
using Listenarr.Application.Common.Exceptions;
using Listenarr.Domain.Audiobooks.Enumerations;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks.DownloadClients;

namespace Listenarr.Tests.Features.Infrastructure.Downloads.Processing
{
    [Trait("Name", "DownloadProcessingJobProcessorImportConflictRetryTests")]
    [Trait("Category", "DownloadProcessingJob")]
    public class DownloadProcessingJobProcessorImportConflictRetryTests : BaseTests
    {
        private const string MoveActiveMessage =
            "A move is already in progress for this audiobook. Wait for it to finish before changing its files.";
        private const string RepairRequiredMessage =
            "A previously committed file import (00000000-0000-0000-0000-000000000001) requires repair before this audiobook's files can be changed.";

        private readonly Mock<IDownloadImportService> importServiceMock = new();
        private readonly DownloadClientGatewayMock downloadClientGatewayMock = new();
        private ApplicationConflictException? blocker;
        private int importCalls;

        public override async Task InitializeAsync()
        {
            _services.AddSingleton<IDownloadClientGateway>(downloadClientGatewayMock);
            Init(builder => builder.WithSingleton<IDownloadImportService>(importServiceMock.Object));
            await AddAuthorizedRootAsync(FileService.GetTempPath());
        }

        [Fact]
        public async Task TemporaryConflict_RetriesWithinTheBoundAndImportsOnceTheMoveFinishes()
        {
            blocker = new ApplicationConflictException("move_already_active", MoveActiveMessage);
            var (download, job) = await QueueImportAsync();

            await ProcessQueueAsync();

            var retrying = (await _downloadProcessingJobRepository.GetByIdAsync(job.Id))!;
            Assert.Equal(ProcessingJobStatus.Pending, retrying.Status);
            Assert.Equal(1, retrying.RetryCount);
            Assert.NotNull(retrying.NextRetryAt);
            Assert.Equal(DownloadStatus.ImportPending, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);

            blocker = null;
            await MakeRetryDueAsync(job.Id);
            await ProcessQueueAsync();

            var imported = (await _downloadProcessingJobRepository.GetByIdAsync(job.Id))!;
            Assert.Equal(ProcessingJobStatus.Completed, imported.Status);
            Assert.Equal(DownloadStatus.Moved, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);
            Assert.Equal(2, importCalls);
        }

        [Fact]
        public async Task TemporaryConflict_ThatNeverEnds_FailsAfterTheRetryBoundAndBlocksTheDownload()
        {
            blocker = new ApplicationConflictException("move_already_active", MoveActiveMessage);
            var (download, job) = await QueueImportAsync();

            for (var pass = 0; pass <= job.MaxRetries; pass++)
            {
                await ProcessQueueAsync();
                await MakeRetryDueAsync(job.Id);
            }

            var savedJob = (await _downloadProcessingJobRepository.GetByIdAsync(job.Id))!;
            Assert.Equal(ProcessingJobStatus.Failed, savedJob.Status);
            Assert.Equal(job.MaxRetries, savedJob.RetryCount);
            Assert.Equal(job.MaxRetries + 1, importCalls);
            Assert.Equal(DownloadStatus.ImportBlocked, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);
        }

        [Theory]
        [InlineData("registration_recovery_repair_required", RepairRequiredMessage)]
        [InlineData("move_repair_required", "A previous move left unresolved filesystem state that requires repair before this audiobook can be changed.")]
        public async Task TerminalConflict_FailsTheJob_AndRetryImportAfterRepairMakesItRunnable(string code, string message)
        {
            blocker = new ApplicationConflictException(code, message);
            var (download, job) = await QueueImportAsync();

            await ProcessQueueAsync();

            Assert.Equal(ProcessingJobStatus.Failed, (await _downloadProcessingJobRepository.GetByIdAsync(job.Id))!.Status);
            var blocked = (await _downloadRepository.GetByIdAsync(download.Id))!;
            Assert.Equal(DownloadStatus.ImportBlocked, blocked.Status);
            Assert.Contains(message, blocked.ImportBlockMessages!);

            blocker = null;
            var result = await MockUtils.CreateDownloadsController(_provider).RetryBlockedImport(download.Id);
            Assert.IsType<OkObjectResult>(result);

            await _provider.GetRequiredService<IDownloadProcessingJobService>().ResetStuckJobsAsync(CancellationToken.None);
            var runnable = (await _downloadProcessingJobRepository.GetByIdAsync(job.Id))!;
            Assert.Equal(ProcessingJobStatus.Pending, runnable.Status);
            Assert.Equal(DownloadStatus.ImportPending, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);

            await ProcessQueueAsync();

            Assert.Equal(ProcessingJobStatus.Completed, (await _downloadProcessingJobRepository.GetByIdAsync(job.Id))!.Status);
            Assert.Equal(DownloadStatus.Moved, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);
            Assert.Equal(2, importCalls);
        }

        [Fact]
        public async Task RetryImport_AfterTheRetryBoundWasExhausted_GivesTheJobAFreshBound()
        {
            blocker = new ApplicationConflictException("move_already_active", MoveActiveMessage);
            var (download, job) = await QueueImportAsync();
            for (var pass = 0; pass <= job.MaxRetries; pass++)
            {
                await ProcessQueueAsync();
                await MakeRetryDueAsync(job.Id);
            }

            blocker = null;
            await MockUtils.CreateDownloadsController(_provider).RetryBlockedImport(download.Id);

            var requeued = (await _downloadProcessingJobRepository.GetByIdAsync(job.Id))!;
            Assert.Equal(ProcessingJobStatus.Pending, requeued.Status);
            Assert.Equal(0, requeued.RetryCount);
            Assert.Null(requeued.NextRetryAt);
        }

        [Fact]
        public async Task RetryImport_CalledTwice_LeavesASingleActiveJob()
        {
            blocker = new ApplicationConflictException("move_repair_required", "repair needed");
            var (download, job) = await QueueImportAsync();
            await ProcessQueueAsync();
            blocker = null;

            var controller = MockUtils.CreateDownloadsController(_provider);
            await controller.RetryBlockedImport(download.Id);
            var second = await controller.RetryBlockedImport(download.Id);

            Assert.IsType<BadRequestObjectResult>(second);
            var jobs = await _downloadProcessingJobRepository.GetByDownloadIdAsync(download.Id);
            Assert.Equal(job.Id, Assert.Single(jobs).Id);
        }

        private async Task ProcessQueueAsync() =>
            await _provider.GetRequiredService<DownloadProcessingJobProcessor>().ProcessQueueAsync(CancellationToken.None);

        private async Task MakeRetryDueAsync(string jobId)
        {
            var job = (await _downloadProcessingJobRepository.GetByIdAsync(jobId))!;
            if (job.NextRetryAt == null)
            {
                return;
            }

            job.NextRetryAt = DateTime.UtcNow.AddMinutes(-1);
            await _downloadProcessingJobRepository.UpdateAsync(job);
        }

        private async Task<(Download Download, DownloadProcessingJob Job)> QueueImportAsync()
        {
            var sourceDirectory = FileService.GetTempDirectory("import-conflict-retry-source");
            var source = await FileService.GetFileAsync(sourceDirectory, "book.mp3", "audio");
            downloadClientGatewayMock.SourceFiles = [source];
            downloadClientGatewayMock.CanMoveFiles = true;
            importServiceMock
                .Setup(service => service.ImportDownloadFilesAsync(
                    It.IsAny<Audiobook>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<DownloadImportOptions?>()))
                .Returns(() =>
                {
                    importCalls++;
                    return blocker != null
                        ? Task.FromException<List<ImportResult>>(blocker)
                        : Task.FromResult(new List<ImportResult>
                        {
                            ImportResult.ImportSuccess(FileAction.Move, source, source + ".final", wasRegisteredToAudiobook: true)
                        });
                });

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(await CreateAudiobook())
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithPath(sourceDirectory)
                .WithCompletedStatus(DateTime.UtcNow)
                .Build());
            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());
            return (download, job);
        }
    }
}
