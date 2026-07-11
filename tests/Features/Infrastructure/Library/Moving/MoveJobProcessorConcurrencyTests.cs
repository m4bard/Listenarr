using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Library.Moving;

public partial class MoveJobProcessorTests
{
    [Fact]
    public async Task ProcessJobAsync_ConcurrentMetadataSave_PreservesLatestMetadata()
    {
        var source = FileService.GetTempDirectory("move-processor-concurrent-metadata-src");
        var sourceFile = await FileService.GetFileAsync(source, "book.m4b", "audio");
        var target = Path.Join(
            FileService.GetTempPath(),
            $"move-processor-concurrent-metadata-dst-{Guid.NewGuid():N}");
        var audiobook = await _audiobookRepository.AddAsync(new Audiobook
        {
            Title = "Original title",
            BasePath = source,
            FilePath = sourceFile,
            Tags = ["original"],
            Monitored = true
        });
        var (queue, job) = await CreateQueuedMoveJobAsync(audiobook, target, source);
        var factory = _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
        var contentMoveService = new AudiobookContentMoveService(
            _provider.GetRequiredService<ILogger<AudiobookContentMoveService>>(),
            factory,
            TimeProvider.System,
            new SaveConcurrentMetadataAfterPublish(
                _provider.GetRequiredService<IServiceScopeFactory>(),
                audiobook.Id));
        var processor = ActivatorUtilities.CreateInstance<MoveJobProcessor>(
            _provider,
            contentMoveService);

        await processor.ProcessJobAsync(job, CancellationToken.None);

        Assert.Equal(MoveJobStatus.Completed, (await queue.GetJobAsync(job.Id))?.Status);
        using var verificationScope = _provider.CreateScope();
        var verificationRepository = verificationScope.ServiceProvider
            .GetRequiredService<IAudiobookRepository>();
        var updated = await verificationRepository.GetByIdAsync(audiobook.Id);
        Assert.NotNull(updated);
        Assert.Equal("Concurrent title", updated!.Title);
        Assert.Equal(["concurrent"], updated.Tags);
        Assert.False(updated.Monitored);
        Assert.Equal(Path.GetFullPath(target), Path.GetFullPath(updated.BasePath!));
        Assert.Equal(
            Path.GetFullPath(Path.Join(target, "book.m4b")),
            Path.GetFullPath(updated.FilePath!));
    }

    private sealed class SaveConcurrentMetadataAfterPublish(
        IServiceScopeFactory scopeFactory,
        int audiobookId) : IMoveFaultInjector
    {
        private bool _saved;

        public async Task AfterPublishedAsync(
            Guid jobId,
            CancellationToken cancellationToken)
        {
            if (_saved)
            {
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
            var audiobook = await repository.GetByIdAsync(audiobookId)
                ?? throw new InvalidOperationException("Audiobook not found during concurrency test.");
            audiobook.Title = "Concurrent title";
            audiobook.Tags = ["concurrent"];
            audiobook.Monitored = false;
            await repository.UpdateAsync(audiobook);
            _saved = true;
        }
    }
}
