using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Library.Moving
{
    [Trait("Name", "MoveJobProcessorTests")]
    [Trait("Category", "BackgroundWorkers")]
    public class MoveJobProcessorTests : BaseTests
    {
        [Fact]
        public async Task ProcessJobAsync_HappyPath_MovesFilesAndCompletesJob()
        {
            var src = FileService.GetTempDirectory("move-processor-src");
            await FileService.GetFileAsync(src, "book.m4b", "audio");
            var dst = Path.Join(FileService.GetTempPath(), "move-processor-dst");
            var audiobook = await _audiobookRepository.AddAsync(new Audiobook { Title = "Move Processor", BasePath = src });
            var (queue, job) = await CreateQueuedMoveJobAsync(audiobook, dst, src);

            var processor = _provider.GetRequiredService<IMoveJobProcessor>();
            await processor.ProcessJobAsync(job, CancellationToken.None);

            var updatedJob = await queue.GetJobAsync(job.Id);
            Assert.NotNull(updatedJob);
            Assert.Equal("Completed", updatedJob!.Status);
            Assert.False(Directory.Exists(src));
            Assert.True(File.Exists(Path.Join(dst, "book.m4b")));

            var history = await _historyRepository.GetByAudiobookIdAsync(audiobook.Id);
            var moveEvent = Assert.Single(history, entry => entry.EventType == "Moved");
            Assert.True(moveEvent.NotificationSent);

            var metricsMock = _provider.GetRequiredService<Mock<IAppMetricsService>>();
            metricsMock.Verify(m => m.Increment("worker.move.job.started", It.IsAny<double>()), Times.Once);
            metricsMock.Verify(m => m.Increment("worker.move.job.completed", It.IsAny<double>()), Times.Once);
        }

        [Fact]
        public async Task ProcessJobAsync_TargetInsideSource_MovesSourceContentsIntoTarget()
        {
            var src = FileService.GetTempDirectory("move-processor-nested-src");
            await FileService.GetFileAsync(src, "book.m4b", "audio");
            var extras = Path.Join(src, "extras");
            Directory.CreateDirectory(extras);
            await FileService.GetFileAsync(extras, "cover.jpg", "image");
            var dst = Path.Join(src, " test");
            var audiobook = await _audiobookRepository.AddAsync(new Audiobook { Title = "Move Processor Nested", BasePath = src });
            var (queue, job) = await CreateQueuedMoveJobAsync(audiobook, dst, src);

            var processor = _provider.GetRequiredService<IMoveJobProcessor>();
            await processor.ProcessJobAsync(job, CancellationToken.None);

            var updatedJob = await queue.GetJobAsync(job.Id);
            Assert.NotNull(updatedJob);
            Assert.Equal("Completed", updatedJob!.Status);
            Assert.True(Directory.Exists(src));
            Assert.True(Directory.Exists(dst));
            Assert.False(File.Exists(Path.Join(src, "book.m4b")));
            Assert.False(Directory.Exists(extras));
            Assert.True(File.Exists(Path.Join(dst, "book.m4b")));
            Assert.True(File.Exists(Path.Join(dst, "extras", "cover.jpg")));

            using var verificationScope = _provider.CreateScope();
            var verificationRepository = verificationScope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
            var updatedAudiobook = await verificationRepository.GetByIdAsync(audiobook.Id);
            Assert.NotNull(updatedAudiobook);
            Assert.Equal(dst, updatedAudiobook!.BasePath);
        }

        [Fact]
        public async Task ProcessJobAsync_SourceInsideEmptyParent_DeletesEmptyParentAfterMove()
        {
            var sourceParent = FileService.GetTempDirectory("move-processor-empty-parent");
            var src = Path.Join(sourceParent, " test");
            Directory.CreateDirectory(src);
            await FileService.GetFileAsync(src, "book.m4b", "audio");
            var dst = Path.Join(FileService.GetTempPath(), "move-processor-cleaned-dst");
            var audiobook = await _audiobookRepository.AddAsync(new Audiobook { Title = "Move Processor Empty Parent", BasePath = src });
            var (queue, job) = await CreateQueuedMoveJobAsync(audiobook, dst, src);

            var processor = _provider.GetRequiredService<IMoveJobProcessor>();
            await processor.ProcessJobAsync(job, CancellationToken.None);

            var updatedJob = await queue.GetJobAsync(job.Id);
            Assert.NotNull(updatedJob);
            Assert.Equal("Completed", updatedJob!.Status);
            Assert.False(Directory.Exists(src));
            Assert.False(Directory.Exists(sourceParent));
            Assert.True(File.Exists(Path.Join(dst, "book.m4b")));
        }

        [Fact]
        public async Task ProcessJobAsync_SourceInsideDestination_DoesNotDeleteDestinationAncestor()
        {
            var dst = FileService.GetTempDirectory("move-processor-parent-target");
            var src = Path.Join(dst, " test");
            Directory.CreateDirectory(src);
            await FileService.GetFileAsync(src, "book.m4b", "audio");
            var audiobook = await _audiobookRepository.AddAsync(new Audiobook { Title = "Move Processor Parent Target", BasePath = src });
            var (queue, job) = await CreateQueuedMoveJobAsync(audiobook, dst, src);

            var processor = _provider.GetRequiredService<IMoveJobProcessor>();
            await processor.ProcessJobAsync(job, CancellationToken.None);

            var updatedJob = await queue.GetJobAsync(job.Id);
            Assert.NotNull(updatedJob);
            Assert.Equal("Completed", updatedJob!.Status);
            Assert.False(Directory.Exists(src));
            Assert.True(Directory.Exists(dst));
            Assert.True(File.Exists(Path.Join(dst, "book.m4b")));
        }

        [Fact]
        public async Task ProcessJobAsync_TargetContainsFiles_MarksJobFailed()
        {
            var src = FileService.GetTempDirectory("move-processor-fail-src");
            await FileService.GetFileAsync(src, "book.m4b", "audio");
            var dst = FileService.GetTempDirectory("move-processor-fail-dst");
            await FileService.GetFileAsync(dst, "existing.txt", "blocked");
            var audiobook = await _audiobookRepository.AddAsync(new Audiobook { Title = "Move Processor Fail", BasePath = src });
            var (queue, job) = await CreateQueuedMoveJobAsync(audiobook, dst, src);

            var processor = _provider.GetRequiredService<IMoveJobProcessor>();
            await processor.ProcessJobAsync(job, CancellationToken.None);

            var updatedJob = await queue.GetJobAsync(job.Id);
            Assert.NotNull(updatedJob);
            Assert.Equal("Failed", updatedJob!.Status);
            Assert.True(Directory.Exists(src));

            var metricsMock = _provider.GetRequiredService<Mock<IAppMetricsService>>();
            metricsMock.Verify(m => m.Increment("worker.move.job.failed", It.IsAny<double>()), Times.Once);
        }

        [Fact]
        public async Task ProcessJobAsync_CanceledToken_ThrowsBeforeStateChange()
        {
            var src = FileService.GetTempDirectory("move-processor-cancel");
            var audiobook = await _audiobookRepository.AddAsync(new Audiobook { Title = "Move Processor Cancel", BasePath = src });
            var (queue, job) = await CreateQueuedMoveJobAsync(audiobook, src, src);
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            var processor = _provider.GetRequiredService<IMoveJobProcessor>();
            await Assert.ThrowsAsync<OperationCanceledException>(() => processor.ProcessJobAsync(job, cts.Token));

            var updatedJob = await queue.GetJobAsync(job.Id);
            Assert.NotNull(updatedJob);
            Assert.Equal("Queued", updatedJob!.Status);
        }

        [Fact]
        public async Task ProcessJobAsync_ReplayedNoOpJob_RemainsCompleted()
        {
            var src = FileService.GetTempDirectory("move-processor-replay");
            await FileService.GetFileAsync(src, "book.m4b", "audio");
            var audiobook = await _audiobookRepository.AddAsync(new Audiobook { Title = "Move Processor Replay", BasePath = src });
            var (queue, job) = await CreateQueuedMoveJobAsync(audiobook, src, src);

            var processor = _provider.GetRequiredService<IMoveJobProcessor>();
            await processor.ProcessJobAsync(job, CancellationToken.None);
            await processor.ProcessJobAsync(job, CancellationToken.None);

            var updatedJob = await queue.GetJobAsync(job.Id);
            Assert.NotNull(updatedJob);
            Assert.Equal("Completed", updatedJob!.Status);
            Assert.True(File.Exists(Path.Join(src, "book.m4b")));
        }

        private async Task<(IMoveQueueService Queue, MoveJob Job)> CreateQueuedMoveJobAsync(
            Audiobook audiobook,
            string requestedPath,
            string sourcePath)
        {
            var queue = _provider.GetRequiredService<IMoveQueueService>();
            var jobId = await queue.EnqueueMoveAsync(audiobook.Id, requestedPath, sourcePath);
            var job = await queue.GetJobAsync(jobId);
            Assert.NotNull(job);
            return (queue, job!);
        }
    }
}
