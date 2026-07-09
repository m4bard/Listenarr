using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Library.Moving
{
    [Trait("Name", "MoveJobProcessor_FileReferenceRewriteTests")]
    [Trait("Category", "BackgroundWorkers")]
    public class MoveJobProcessor_FileReferenceRewriteTests : BaseTests
    {
        private const string LeaseOwner = "test-worker";

        [Fact]
        public async Task ProcessJobAsync_PhysicalMove_RewritesTrackedAudiobookFilePaths()
        {
            var source = FileService.GetTempDirectory("move-processor-file-reference-src");
            var bookPath = await FileService.GetFileAsync(source, "book.m4b", "audio");
            var extras = Path.Join(source, "extras");
            Directory.CreateDirectory(extras);
            var chapterPath = await FileService.GetFileAsync(extras, "chapter2.mp3", "chapter audio");
            var target = Path.Join(
                FileService.GetTempPath(),
                $"move-processor-file-reference-dst-{Guid.NewGuid():N}");

            var audiobook = await _audiobookRepository.AddAsync(new Audiobook
            {
                Title = "Move Processor File References",
                BasePath = source,
                FilePath = source,
                Files =
                [
                    new AudiobookFile { Path = bookPath },
                    new AudiobookFile { Path = chapterPath }
                ]
            });
            var queue = _provider.GetRequiredService<IMoveQueueService>();
            var jobId = await queue.EnqueueMoveAsync(audiobook.Id, target, source);
            var job = await queue.GetJobAsync(jobId);
            Assert.NotNull(job);
            await PrepareJobForProcessingAsync(queue, job!);

            var processor = _provider.GetRequiredService<IMoveJobProcessor>();
            await processor.ProcessJobAsync(job!, CancellationToken.None);

            var completedJob = await queue.GetJobAsync(jobId);
            Assert.NotNull(completedJob);
            Assert.Equal(MoveJobStatus.Completed, completedJob!.Status);
            Assert.True(File.Exists(Path.Join(target, "book.m4b")));
            Assert.True(File.Exists(Path.Join(target, "extras", "chapter2.mp3")));

            using var verificationScope = _provider.CreateScope();
            var repository = verificationScope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
            var updatedAudiobook = await repository.GetByIdAsync(audiobook.Id);
            Assert.NotNull(updatedAudiobook);
            Assert.Equal(Path.GetFullPath(target), updatedAudiobook!.BasePath);
            Assert.Equal(Path.GetFullPath(target), updatedAudiobook.FilePath);
            Assert.NotNull(updatedAudiobook.Files);
            Assert.Contains(updatedAudiobook.Files!, file => file.Path == Path.Join(target, "book.m4b"));
            Assert.Contains(updatedAudiobook.Files!, file => file.Path == Path.Join(target, "extras", "chapter2.mp3"));
            Assert.DoesNotContain(
                updatedAudiobook.Files!,
                file => file.Path?.StartsWith(source, StringComparison.Ordinal) == true);
        }

        private static async Task PrepareJobForProcessingAsync(IMoveQueueService queue, MoveJob job)
        {
            var leaseGeneration = await queue.TryClaimJobAsync(job.Id, LeaseOwner);
            Assert.NotNull(leaseGeneration);
            job.LeaseOwner = LeaseOwner;
            job.LeaseGeneration = leaseGeneration.Value;
        }
    }
}
