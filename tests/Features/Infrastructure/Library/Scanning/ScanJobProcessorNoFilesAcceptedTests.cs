using System.Text.Json;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.AspNetCore.SignalR;

namespace Listenarr.Tests.Features.Infrastructure.Library.Scanning
{
    [Trait("Name", "ScanJobProcessorNoFilesAcceptedTests")]
    [Trait("Category", "BackgroundWorkers")]
    public class ScanJobProcessorNoFilesAcceptedTests : BaseTests
    {
        private readonly List<(string Method, JsonElement Payload)> _broadcasts = [];

        public override async Task InitializeAsync()
        {
            // Metadata is unavailable (null), the case where nothing per-file is
            // recorded and only the job-level status can say why files are missing.
            _services.AddSingleton(new Mock<IMetadataService>().Object);
            var proxy = new Mock<IClientProxy>();
            proxy.Setup(p => p.SendCoreAsync(
                    It.IsAny<string>(),
                    It.IsAny<object?[]>(),
                    It.IsAny<CancellationToken>()))
                .Callback((string method, object?[] args, CancellationToken _) =>
                {
                    lock (_broadcasts)
                    {
                        _broadcasts.Add((
                            method,
                            JsonSerializer.SerializeToElement(args.FirstOrDefault())));
                    }
                })
                .Returns(Task.CompletedTask);
            var hubClients = new Mock<IHubClients>();
            hubClients.Setup(c => c.All).Returns(proxy.Object);
            var hubContext = new Mock<IHubContext<DownloadHub>>();
            hubContext.Setup(h => h.Clients).Returns(hubClients.Object);
            _services.AddSingleton(hubContext.Object);
            Init();
            await _applicationSettingsRepository.SaveAsync(
                new ApplicationSettingsBuilder()
                    .WithOutputPath(FileService.GetTempPath())
                    .Build());
        }

        [Fact]
        public async Task ProcessJobAsync_CandidatesFoundButNoneAccepted_ReportsCompletedNoFilesAccepted()
        {
            var basePath = FileService.GetTempDirectory("scan-processor-none-accepted");
            await FileService.GetFileAsync(basePath, "Track 01.m4b", "audio");
            await FileService.GetFileAsync(basePath, "Track 02.m4b", "audio");
            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Expected Title Nowhere On Disk")
                .WithBasePath(basePath)
                .Build());
            var (queue, job) = await CreateQueuedScanJobAsync(audiobook, "scan:none-accepted");

            await _provider.GetRequiredService<IScanJobProcessor>()
                .ProcessJobAsync(job, CancellationToken.None);

            var updatedJob = GetRequiredJob(queue, job.Id);
            Assert.Equal("CompletedNoFilesAccepted", updatedJob.Status);
            Assert.False(string.IsNullOrWhiteSpace(updatedJob.Error));
            Assert.Contains("2", updatedJob.Error, StringComparison.Ordinal);
            Assert.DoesNotContain(basePath, updatedJob.Error, StringComparison.Ordinal);
            Assert.Empty(await _audiobookFileRepository.GetByAudiobookIdAsync(audiobook.Id));

            var history = Assert.Single(
                await _historyRepository.GetByCorrelationIdAsync("scan:none-accepted"),
                entry => entry.EventType == HistoryEvents.ScanCompleted);
            Assert.Equal(HistoryOutcome.Skipped, history.Outcome);
            Assert.Contains("none", history.Message, StringComparison.OrdinalIgnoreCase);

            var update = Assert.Single(_broadcasts, b =>
                b.Method == "ScanJobUpdate"
                && b.Payload.GetProperty("jobId").GetString() == job.Id.ToString()
                && b.Payload.GetProperty("status").GetString() != "Processing");
            Assert.Equal(
                "CompletedNoFilesAccepted",
                update.Payload.GetProperty("status").GetString());
            Assert.False(string.IsNullOrWhiteSpace(
                update.Payload.GetProperty("error").GetString()));
            Assert.Contains(_broadcasts, b => b.Method == "AudiobookUpdate");

            var metricsMock = _provider.GetRequiredService<Mock<IAppMetricsService>>();
            metricsMock.Verify(m => m.Increment(
                "worker.scan.job.completed_no_files_accepted",
                It.IsAny<double>()), Times.Once);
            metricsMock.Verify(m => m.Increment(
                "worker.scan.job.skipped",
                It.IsAny<double>()), Times.Never);
        }

        [Fact]
        public async Task ProcessJobAsync_ReplayedNoFilesAcceptedJob_KeepsStatusWithoutDuplicateHistory()
        {
            var basePath = FileService.GetTempDirectory("scan-processor-none-accepted-replay");
            await FileService.GetFileAsync(basePath, "Track 01.m4b", "audio");
            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Another Missing Title")
                .WithBasePath(basePath)
                .Build());
            var (queue, job) = await CreateQueuedScanJobAsync(audiobook, "scan:none-accepted-replay");

            var processor = _provider.GetRequiredService<IScanJobProcessor>();
            await processor.ProcessJobAsync(job, CancellationToken.None);
            await processor.ProcessJobAsync(job, CancellationToken.None);

            Assert.Equal("CompletedNoFilesAccepted", GetRequiredJob(queue, job.Id).Status);
            Assert.Single(
                await _historyRepository.GetByCorrelationIdAsync("scan:none-accepted-replay"),
                entry => entry.EventType == HistoryEvents.ScanCompleted);
        }

        [Fact]
        public async Task ProcessJobAsync_FolderWithNoAudioFiles_ReportsPlainCompleted()
        {
            var basePath = FileService.GetTempDirectory("scan-processor-empty-folder");
            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Empty Folder Book")
                .WithBasePath(basePath)
                .Build());
            var (queue, job) = await CreateQueuedScanJobAsync(audiobook);

            await _provider.GetRequiredService<IScanJobProcessor>()
                .ProcessJobAsync(job, CancellationToken.None);

            var updatedJob = GetRequiredJob(queue, job.Id);
            Assert.Equal("Completed", updatedJob.Status);
            Assert.Null(updatedJob.Error);
        }

        [Fact]
        public async Task ProcessJobAsync_RescanOfFullyRegisteredBook_ReportsPlainCompleted()
        {
            var basePath = FileService.GetTempDirectory("scan-processor-registered-rescan");
            await FileService.GetFileAsync(basePath, "Registered Book.m4b", "audio");
            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Registered Book")
                .WithBasePath(basePath)
                .Build());
            var processor = _provider.GetRequiredService<IScanJobProcessor>();
            var (firstQueue, firstJob) = await CreateQueuedScanJobAsync(audiobook);
            await processor.ProcessJobAsync(firstJob, CancellationToken.None);
            Assert.Equal("Completed", GetRequiredJob(firstQueue, firstJob.Id).Status);
            Assert.Single(await _audiobookFileRepository.GetByAudiobookIdAsync(audiobook.Id));

            var (queue, job) = await CreateQueuedScanJobAsync(audiobook);
            Assert.NotEqual(firstJob.Id, job.Id);
            await processor.ProcessJobAsync(job, CancellationToken.None);

            var updatedJob = GetRequiredJob(queue, job.Id);
            Assert.Equal("Completed", updatedJob.Status);
            Assert.Null(updatedJob.Error);
            Assert.Single(await _audiobookFileRepository.GetByAudiobookIdAsync(audiobook.Id));
        }

        private static ScanJob GetRequiredJob(ScanQueueService queue, Guid jobId)
        {
            Assert.True(queue.TryGetJob(jobId, out var job));
            return Assert.IsType<ScanJob>(job);
        }

        private async Task<(ScanQueueService Queue, ScanJob Job)> CreateQueuedScanJobAsync(
            Audiobook audiobook,
            string? correlationId = null)
        {
            var queue = Assert.IsType<ScanQueueService>(_provider.GetRequiredService<IScanQueueService>());
            var jobId = await queue.EnqueueScanAsync(audiobook, correlationId: correlationId);
            Assert.True(queue.Reader.TryRead(out var job));
            Assert.Equal(jobId, job.Id);
            return (queue, job);
        }
    }
}
