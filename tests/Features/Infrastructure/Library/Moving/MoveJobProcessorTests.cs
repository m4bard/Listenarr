using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Library.Moving
{
    [Trait("Name", "MoveJobProcessorTests")]
    [Trait("Category", "BackgroundWorkers")]
    public class MoveJobProcessorTests : BaseTests
    {
        private const string LeaseOwner = "test-worker";
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
            Assert.Equal(MoveJobStatus.Completed, updatedJob!.Status);
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
        public async Task ProcessJobAsync_CompletedStatusPersistenceFailure_PropagatesWithoutCompletedMetric()
        {
            var source = FileService.GetTempDirectory("move-processor-status-failure-src");
            await FileService.GetFileAsync(source, "book.m4b", "audio");
            var target = Path.Join(FileService.GetTempPath(), "move-processor-status-failure-dst");
            var audiobook = await _audiobookRepository.AddAsync(new Audiobook
            {
                Title = "Move Processor Status Failure",
                BasePath = source
            });
            var (durableQueue, job) = await CreateQueuedMoveJobAsync(audiobook, target, source);
            var queue = new Mock<IMoveQueueService>();
            queue.Setup(service => service.UpdateJobStatusAsync(
                    job.Id,
                    LeaseOwner,
                    job.LeaseGeneration,
                    MoveJobStatus.Completed,
                    null,
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new PersistenceException(
                    "Status write failed.",
                    new InvalidOperationException("Database unavailable.")));
            queue.Setup(service => service.UpdateJobStatusAsync(
                    job.Id,
                    LeaseOwner,
                    job.LeaseGeneration,
                    It.Is<MoveJobStatus>(status => status != MoveJobStatus.Completed),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var processor = ActivatorUtilities.CreateInstance<MoveJobProcessor>(
                _provider,
                queue.Object);

            await Assert.ThrowsAsync<PersistenceException>(() => processor.ProcessJobAsync(
                job,
                CancellationToken.None));

            var metrics = _provider.GetRequiredService<Mock<IAppMetricsService>>();
            Assert.Equal(
                MoveJobStatus.Running,
                (await durableQueue.GetJobAsync(job.Id))?.Status);
            metrics.Verify(
                service => service.Increment("worker.move.job.completed", It.IsAny<double>()),
                Times.Never);
            queue.Verify(service => service.UpdateJobStatusAsync(
                job.Id,
                LeaseOwner,
                job.LeaseGeneration,
                MoveJobStatus.Failed,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()), Times.Never);
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
            Assert.Equal(MoveJobStatus.Completed, updatedJob!.Status);
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
        public async Task ProcessJobAsync_SourceInsideEmptyParent_DoesNotDeleteParentAfterMove()
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
            Assert.Equal(MoveJobStatus.Completed, updatedJob!.Status);
            Assert.False(Directory.Exists(src));
            Assert.True(Directory.Exists(sourceParent));
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
            Assert.Equal(MoveJobStatus.Completed, updatedJob!.Status);
            Assert.False(Directory.Exists(src));
            Assert.True(Directory.Exists(dst));
            Assert.True(File.Exists(Path.Join(dst, "book.m4b")));
        }

        [Fact]
        public async Task ProcessJobAsync_CaseOnlyMove_OnCaseSensitiveHost_MovesFiles()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            var root = FileService.GetTempDirectory("move-processor-case-only-root");
            var src = Path.Join(root, "Title");
            Directory.CreateDirectory(src);
            await FileService.GetFileAsync(src, "book.m4b", "audio");
            var dst = Path.Join(root, "title");
            var audiobook = await _audiobookRepository.AddAsync(new Audiobook { Title = "Move Processor Case", BasePath = src });
            var (queue, job) = await CreateQueuedMoveJobAsync(audiobook, dst, src);

            var processor = _provider.GetRequiredService<IMoveJobProcessor>();
            await processor.ProcessJobAsync(job, CancellationToken.None);

            var updatedJob = await queue.GetJobAsync(job.Id);
            Assert.NotNull(updatedJob);
            Assert.Equal(MoveJobStatus.Completed, updatedJob!.Status);
            Assert.False(Directory.Exists(src));
            Assert.True(File.Exists(Path.Join(dst, "book.m4b")));
        }

        [Fact]
        public async Task ProcessJobAsync_DeleteEmptySourceFalse_KeepsEmptySourceDirectory()
        {
            var src = FileService.GetTempDirectory("move-processor-keep-source");
            await FileService.GetFileAsync(src, "book.m4b", "audio");
            var dst = Path.Join(FileService.GetTempPath(), $"move-processor-keep-source-dst-{Guid.NewGuid():N}");
            var audiobook = await _audiobookRepository.AddAsync(new Audiobook
            {
                Title = "Move Processor Keep Source",
                BasePath = src
            });
            var (queue, job) = await CreateQueuedMoveJobAsync(
                audiobook,
                dst,
                src,
                deleteEmptySource: false);

            var processor = _provider.GetRequiredService<IMoveJobProcessor>();
            await processor.ProcessJobAsync(job, CancellationToken.None);

            var updatedJob = await queue.GetJobAsync(job.Id);
            Assert.NotNull(updatedJob);
            Assert.Equal(MoveJobStatus.Completed, updatedJob!.Status);
            Assert.True(Directory.Exists(src));
            Assert.Empty(Directory.EnumerateFileSystemEntries(src));
            Assert.True(File.Exists(Path.Join(dst, "book.m4b")));
        }

        [Fact]
        public async Task ProcessJobAsync_RequeuedCompletedMoveWithRetainedSource_RemainsCompleted()
        {
            var src = FileService.GetTempDirectory("move-processor-requeue-retained-source");
            await FileService.GetFileAsync(src, "book.m4b", "audio");
            var dst = Path.Join(FileService.GetTempPath(), $"move-processor-requeue-retained-dst-{Guid.NewGuid():N}");
            var audiobook = await _audiobookRepository.AddAsync(new Audiobook
            {
                Title = "Move Processor Requeue Retained",
                BasePath = src
            });
            var (queue, job) = await CreateQueuedMoveJobAsync(
                audiobook,
                dst,
                src,
                deleteEmptySource: false);
            var processor = _provider.GetRequiredService<IMoveJobProcessor>();
            await processor.ProcessJobAsync(job, CancellationToken.None);

            var requeuedJobId = await queue.RequeueMoveAsync(job.Id);
            Assert.NotNull(requeuedJobId);
            var requeuedJob = await queue.GetJobAsync(requeuedJobId!.Value);
            Assert.NotNull(requeuedJob);
            await PrepareJobForProcessingAsync(queue, requeuedJob!);
            await processor.ProcessJobAsync(requeuedJob!, CancellationToken.None);

            var completedRequeue = await queue.GetJobAsync(requeuedJob.Id);
            Assert.NotNull(completedRequeue);
            Assert.Equal(MoveJobStatus.Completed, completedRequeue!.Status);
            Assert.True(Directory.Exists(src));
            Assert.Empty(Directory.EnumerateFileSystemEntries(src));
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
            Assert.Equal(MoveJobStatus.Failed, updatedJob!.Status);
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
            Assert.Equal(MoveJobStatus.Running, updatedJob!.Status);
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
            var replayedJobId = await queue.RequeueMoveAsync(job.Id);
            Assert.NotNull(replayedJobId);
            var replayedJob = await queue.GetJobAsync(replayedJobId.Value);
            Assert.NotNull(replayedJob);
            await PrepareJobForProcessingAsync(queue, replayedJob!);
            await processor.ProcessJobAsync(replayedJob!, CancellationToken.None);

            var updatedJob = await queue.GetJobAsync(job.Id);
            Assert.NotNull(updatedJob);
            Assert.Equal(MoveJobStatus.Completed, updatedJob!.Status);
            Assert.True(File.Exists(Path.Join(src, "book.m4b")));
        }

        [Fact]
        public async Task ProcessJobAsync_FilesystemMoveAlreadyCompleted_FinalizesDatabaseState()
        {
            var src = FileService.GetTempDirectory("move-processor-recovery-src");
            await FileService.GetFileAsync(src, "book.m4b", "audio");
            var dst = Path.Join(FileService.GetTempPath(), $"move-processor-recovery-dst-{Guid.NewGuid():N}");
            var audiobook = await _audiobookRepository.AddAsync(new Audiobook
            {
                Title = "Move Processor Recovery",
                BasePath = src
            });
            var (queue, job) = await CreateQueuedMoveJobAsync(audiobook, dst, src);
            var contentMoveService = _provider.GetRequiredService<AudiobookContentMoveService>();
            await contentMoveService.MoveContentsAsync(
                new AudiobookContentMoveRequest(src, dst, job.Id, LeaseGeneration: job.LeaseGeneration),
                CancellationToken.None);

            Assert.False(Directory.Exists(src));
            Assert.Single(Directory.EnumerateFiles(dst, ".listenarr-move-*.pending"));
            Directory.CreateDirectory(src);
            await FileService.GetFileAsync(src, "new-content.txt", "do not delete");

            var processor = _provider.GetRequiredService<IMoveJobProcessor>();
            await processor.ProcessJobAsync(job, CancellationToken.None);

            var updatedJob = await queue.GetJobAsync(job.Id);
            Assert.NotNull(updatedJob);
            Assert.Equal(MoveJobStatus.Completed, updatedJob!.Status);

            using var verificationScope = _provider.CreateScope();
            var verificationRepository = verificationScope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
            var updatedAudiobook = await verificationRepository.GetByIdAsync(audiobook.Id);
            Assert.NotNull(updatedAudiobook);
            Assert.Equal(dst, updatedAudiobook!.BasePath);
            Assert.True(File.Exists(Path.Join(dst, "book.m4b")));
            Assert.Equal("do not delete", await File.ReadAllTextAsync(Path.Join(src, "new-content.txt")));
            Assert.Empty(Directory.EnumerateFiles(dst, ".listenarr-move-*.pending"));
        }

        [Fact]
        public async Task ProcessJobAsync_CopyCompletedMarkerWithoutManifest_BlocksSourceCleanup()
        {
            var src = FileService.GetTempDirectory("move-processor-copy-complete-src");
            await FileService.GetFileAsync(src, "book.m4b", "audio");
            var dst = FileService.GetTempDirectory("move-processor-copy-complete-dst");
            await FileService.GetFileAsync(dst, "book.m4b", "audio");
            var audiobook = await _audiobookRepository.AddAsync(new Audiobook
            {
                Title = "Move Processor Copy Complete",
                BasePath = src
            });
            var (queue, job) = await CreateQueuedMoveJobAsync(audiobook, dst, src);
            await File.WriteAllTextAsync(
                Path.Join(dst, $".listenarr-move-{job.Id:N}.pending"),
                "copy-complete");

            var processor = _provider.GetRequiredService<IMoveJobProcessor>();
            await processor.ProcessJobAsync(job, CancellationToken.None);

            var completedJob = await queue.GetJobAsync(job.Id);
            Assert.NotNull(completedJob);
            Assert.Equal(MoveJobStatus.NeedsAttention, completedJob!.Status);
            Assert.True(Directory.Exists(src));
            Assert.True(File.Exists(Path.Join(dst, "book.m4b")));
        }

        [Fact]
        public async Task ProcessJobAsync_MissingSourceAndTarget_MarksJobFailed()
        {
            var src = Path.Join(FileService.GetTempPath(), $"move-processor-missing-src-{Guid.NewGuid():N}");
            var dst = Path.Join(FileService.GetTempPath(), $"move-processor-missing-dst-{Guid.NewGuid():N}");
            var audiobook = await _audiobookRepository.AddAsync(new Audiobook
            {
                Title = "Move Processor Missing Paths",
                BasePath = dst
            });
            var (queue, job) = await CreateQueuedMoveJobAsync(audiobook, dst, src);

            var processor = _provider.GetRequiredService<IMoveJobProcessor>();
            await processor.ProcessJobAsync(job, CancellationToken.None);

            var failedJob = await queue.GetJobAsync(job.Id);
            Assert.NotNull(failedJob);
            Assert.Equal(MoveJobStatus.Failed, failedJob!.Status);
        }

        private static async Task PrepareJobForProcessingAsync(IMoveQueueService queue, MoveJob job)
        {
            var leaseGeneration = await queue.TryClaimJobAsync(job.Id, LeaseOwner);
            Assert.NotNull(leaseGeneration);
            job.LeaseOwner = LeaseOwner;
            job.LeaseGeneration = leaseGeneration.Value;
        }

        private async Task<(IMoveQueueService Queue, MoveJob Job)> CreateQueuedMoveJobAsync(
            Audiobook audiobook,
            string requestedPath,
            string sourcePath,
            bool deleteEmptySource = true)
        {
            var queue = _provider.GetRequiredService<IMoveQueueService>();
            var jobId = await queue.EnqueueMoveAsync(
                audiobook.Id,
                requestedPath,
                sourcePath,
                deleteEmptySource);
            var job = await queue.GetJobAsync(jobId);
            Assert.NotNull(job);
            await PrepareJobForProcessingAsync(queue, job!);
            return (queue, job!);
        }
    }
}
