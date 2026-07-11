using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Library.Moving;

public partial class MoveJobProcessorTests
{
    [Fact]
    public async Task ProcessJobAsync_ArtifactCleanupFailsOnce_SchedulesAndCompletesRetry()
    {
        var source = FileService.GetTempDirectory("move-processor-artifact-retry-src");
        await FileService.GetFileAsync(source, "book.m4b", "audio");
        var target = Path.Join(
            FileService.GetTempPath(),
            $"move-processor-artifact-retry-dst-{Guid.NewGuid():N}");
        var audiobook = await _audiobookRepository.AddAsync(new Audiobook
        {
            Title = "Artifact Cleanup Retry",
            BasePath = source
        });
        var (queue, job) = await CreateQueuedMoveJobAsync(audiobook, target, source);
        var faultingContentMoveService = new AudiobookContentMoveService(
            _provider.GetRequiredService<ILogger<AudiobookContentMoveService>>(),
            _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>(),
            TimeProvider.System,
            new FailCompletedArtifactCleanupOnce());
        var faultingProcessor = ActivatorUtilities.CreateInstance<MoveJobProcessor>(
            _provider,
            faultingContentMoveService);

        await faultingProcessor.ProcessJobAsync(job, CancellationToken.None);

        var retryJob = await queue.GetJobAsync(job.Id);
        Assert.NotNull(retryJob);
        Assert.Equal(MoveJobStatus.RetryScheduled, retryJob!.Status);
        Assert.Equal(MoveJobPhase.CleaningArtifacts, retryJob.Phase);
        var markerPath = Path.Join(target, $".listenarr-move-{job.Id:N}.pending");
        Assert.True(File.Exists(markerPath));
        Assert.Empty(await _historyRepository.GetByCorrelationIdAsync($"move:{job.Id:N}"));

        var retryGeneration = await queue.TryClaimJobAsync(job.Id, LeaseOwner);
        Assert.NotNull(retryGeneration);
        retryJob.LeaseOwner = LeaseOwner;
        retryJob.LeaseGeneration = retryGeneration.Value;
        var retryProcessor = _provider.GetRequiredService<IMoveJobProcessor>();

        await retryProcessor.ProcessJobAsync(retryJob, CancellationToken.None);

        Assert.Equal(MoveJobStatus.Completed, (await queue.GetJobAsync(job.Id))?.Status);
        Assert.False(File.Exists(markerPath));
        Assert.Single(
            await _historyRepository.GetByCorrelationIdAsync($"move:{job.Id:N}"),
            entry => entry.EventType == "Moved");
    }

    [Fact]
    public async Task ProcessJobAsync_FinalizationIoFailure_SchedulesAndCompletesRetry()
    {
        var sourceRoot = FileService.GetTempDirectory("move-processor-finalize-retry-root");
        var sourceParent = Path.Join(sourceRoot, "Author", "Old Title");
        var source = Path.Join(sourceParent, "test");
        Directory.CreateDirectory(source);
        await FileService.GetFileAsync(source, "book.m4b", "audio");
        var target = Path.Join(
            FileService.GetTempPath(),
            $"move-processor-finalize-retry-dst-{Guid.NewGuid():N}");
        await _rootFolderRepository.AddAsync(new RootFolder
        {
            Name = "Finalization Retry Root",
            Path = sourceRoot
        });
        var audiobook = await _audiobookRepository.AddAsync(new Audiobook
        {
            Title = "Finalization Retry",
            BasePath = source
        });
        var (queue, job) = await CreateQueuedMoveJobAsync(audiobook, target, source);
        var faultingContentMoveService = new AudiobookContentMoveService(
            _provider.GetRequiredService<ILogger<AudiobookContentMoveService>>(),
            _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>(),
            TimeProvider.System,
            new FailMoveFinalizationOnce());
        var faultingProcessor = ActivatorUtilities.CreateInstance<MoveJobProcessor>(
            _provider,
            faultingContentMoveService);

        await faultingProcessor.ProcessJobAsync(job, CancellationToken.None);

        var retryJob = await queue.GetJobAsync(job.Id);
        Assert.NotNull(retryJob);
        Assert.Equal(MoveJobStatus.RetryScheduled, retryJob!.Status);
        Assert.Equal(MoveJobPhase.Finalizing, retryJob.Phase);
        Assert.True(Directory.Exists(sourceParent));
        Assert.True(File.Exists(Path.Join(target, $".listenarr-move-{job.Id:N}.pending")));

        var retryGeneration = await queue.TryClaimJobAsync(job.Id, LeaseOwner);
        Assert.NotNull(retryGeneration);
        retryJob.LeaseOwner = LeaseOwner;
        retryJob.LeaseGeneration = retryGeneration.Value;
        await _provider.GetRequiredService<IMoveJobProcessor>()
            .ProcessJobAsync(retryJob, CancellationToken.None);

        Assert.Equal(MoveJobStatus.Completed, (await queue.GetJobAsync(job.Id))?.Status);
        Assert.False(Directory.Exists(sourceParent));
        Assert.True(Directory.Exists(sourceRoot));
        Assert.False(File.Exists(Path.Join(target, $".listenarr-move-{job.Id:N}.pending")));
    }

    private sealed class FailCompletedArtifactCleanupOnce : IMoveFaultInjector
    {
        private bool _failed;

        public void OnCompletedArtifactCleanup(
            Guid jobId,
            CompletedArtifactCleanupFaultPoint faultPoint)
        {
            if (_failed
                || faultPoint != CompletedArtifactCleanupFaultPoint.BeforeRecoveryMarkerDelete)
            {
                return;
            }

            _failed = true;
            throw new IOException("Simulated transient recovery marker lock.");
        }
    }

    private sealed class FailMoveFinalizationOnce : IMoveFaultInjector
    {
        private bool _failed;

        public void OnMoveFinalization(
            Guid jobId,
            MoveFinalizationFaultPoint faultPoint)
        {
            if (_failed
                || faultPoint != MoveFinalizationFaultPoint.BeforeSourceAncestorDelete)
            {
                return;
            }

            _failed = true;
            throw new IOException("Simulated transient source ancestor lock.");
        }
    }
}
