namespace Listenarr.Tests.Features.Infrastructure.Library.Moving;

public partial class MoveJobProcessorTests
{
    [Fact]
    public async Task ProcessJobAsync_MarkerlessVerifiedCopy_Completes()
    {
        var state = await CreateMarkerlessFinalizedCopyStateAsync();
        var processor = _provider.GetRequiredService<IMoveJobProcessor>();

        await processor.ProcessJobAsync(state.Job, CancellationToken.None);

        Assert.Equal(
            MoveJobStatus.Completed,
            (await state.Queue.GetJobAsync(state.Job.Id))?.Status);
        Assert.True(File.Exists(Path.Join(state.Target, "book.m4b")));
    }

    [Fact]
    public async Task ProcessJobAsync_MarkerlessCorruptedCopy_RequiresAttention()
    {
        var state = await CreateMarkerlessFinalizedCopyStateAsync();
        await File.WriteAllTextAsync(Path.Join(state.Target, "book.m4b"), "corrupted audio");
        var processor = _provider.GetRequiredService<IMoveJobProcessor>();

        await processor.ProcessJobAsync(state.Job, CancellationToken.None);

        Assert.Equal(
            MoveJobStatus.NeedsAttention,
            (await state.Queue.GetJobAsync(state.Job.Id))?.Status);
        Assert.Equal(
            "corrupted audio",
            await File.ReadAllTextAsync(Path.Join(state.Target, "book.m4b")));
    }

    [Fact]
    public async Task ProcessJobAsync_MarkerlessCopyWithUnownedFile_RequiresAttention()
    {
        var state = await CreateMarkerlessFinalizedCopyStateAsync();
        var unownedFile = await FileService.GetFileAsync(
            state.Target,
            "operator-note.txt",
            "preserve me");
        var processor = _provider.GetRequiredService<IMoveJobProcessor>();

        await processor.ProcessJobAsync(state.Job, CancellationToken.None);

        Assert.Equal(
            MoveJobStatus.NeedsAttention,
            (await state.Queue.GetJobAsync(state.Job.Id))?.Status);
        Assert.True(File.Exists(unownedFile));
        Assert.Equal("preserve me", await File.ReadAllTextAsync(unownedFile));
    }

    [Fact]
    public async Task ProcessJobAsync_MarkerlessCopyWithLinkedTarget_RequiresAttention()
    {
        var state = await CreateMarkerlessFinalizedCopyStateAsync();
        var externalTarget = FileService.GetTempDirectory(
            "move-processor-markerless-linked-external");
        var externalFile = await FileService.GetFileAsync(
            externalTarget,
            "book.m4b",
            "verified audio");
        Directory.Delete(state.Target, recursive: true);
        if (!TryCreateProcessorDirectoryLink(state.Target, externalTarget))
        {
            return;
        }

        try
        {
            var processor = _provider.GetRequiredService<IMoveJobProcessor>();
            await processor.ProcessJobAsync(state.Job, CancellationToken.None);

            Assert.Equal(
                MoveJobStatus.NeedsAttention,
                (await state.Queue.GetJobAsync(state.Job.Id))?.Status);
            Assert.True(File.Exists(externalFile));
            Assert.Equal("verified audio", await File.ReadAllTextAsync(externalFile));
        }
        finally
        {
            TryRemoveProcessorDirectoryLink(state.Target);
        }
    }

    [Fact]
    public async Task ProcessJobAsync_MarkerlessCopyWithPartialArtifact_RequiresAttention()
    {
        var state = await CreateMarkerlessFinalizedCopyStateAsync();
        var partialPath = Path.Join(
            state.Target,
            $"book.m4b.listenarr-{state.Job.Id:N}.partial");
        await File.WriteAllTextAsync(partialPath, "verified audio");
        var processor = _provider.GetRequiredService<IMoveJobProcessor>();

        await processor.ProcessJobAsync(state.Job, CancellationToken.None);

        Assert.Equal(
            MoveJobStatus.NeedsAttention,
            (await state.Queue.GetJobAsync(state.Job.Id))?.Status);
        Assert.True(File.Exists(partialPath));
    }

    [Fact]
    public async Task ProcessJobAsync_MarkerlessMissingTarget_RequiresAttention()
    {
        var state = await CreateMarkerlessFinalizedCopyStateAsync();
        Directory.Delete(state.Target, recursive: true);
        var processor = _provider.GetRequiredService<IMoveJobProcessor>();

        await processor.ProcessJobAsync(state.Job, CancellationToken.None);

        Assert.Equal(
            MoveJobStatus.NeedsAttention,
            (await state.Queue.GetJobAsync(state.Job.Id))?.Status);
        Assert.False(Directory.Exists(state.Target));
    }

    [Fact]
    public async Task ProcessJobAsync_MarkerlessRecreatedEmptyTarget_RequiresAttention()
    {
        var state = await CreateMarkerlessFinalizedCopyStateAsync();
        Directory.Delete(state.Target, recursive: true);
        Directory.CreateDirectory(state.Target);
        var processor = _provider.GetRequiredService<IMoveJobProcessor>();

        await processor.ProcessJobAsync(state.Job, CancellationToken.None);

        Assert.Equal(
            MoveJobStatus.NeedsAttention,
            (await state.Queue.GetJobAsync(state.Job.Id))?.Status);
        Assert.Empty(Directory.EnumerateFileSystemEntries(state.Target));
    }

    [Fact]
    public async Task ProcessJobAsync_MarkerlessAtomicMove_RequiresAttention()
    {
        var source = FileService.GetTempDirectory("move-processor-markerless-atomic-src");
        await FileService.GetFileAsync(source, "book.m4b", "audio");
        var target = Path.Join(
            Path.GetDirectoryName(source)!,
            $"move-processor-markerless-atomic-dst-{Guid.NewGuid():N}");
        var audiobook = await _audiobookRepository.AddAsync(new Audiobook
        {
            Title = "Markerless Atomic Recovery",
            BasePath = source
        });
        var (queue, job) = await CreateQueuedMoveJobAsync(audiobook, target, source);
        var service = _provider.GetRequiredService<AudiobookContentMoveService>();
        var request = CreateMoveRequest(source, target, job, deleteEmptySource: true);
        var result = await service.MoveContentsAsync(request, CancellationToken.None);
        audiobook.BasePath = target;
        await _audiobookRepository.UpdateAsync(audiobook);
        service.FinalizeMove(request, result);
        File.Delete(result.RecoveryMarkerPath);
        var processor = _provider.GetRequiredService<IMoveJobProcessor>();

        await processor.ProcessJobAsync(job, CancellationToken.None);

        Assert.Equal(
            MoveJobStatus.NeedsAttention,
            (await queue.GetJobAsync(job.Id))?.Status);
        Assert.True(File.Exists(Path.Join(target, "book.m4b")));
    }

    private async Task<MarkerlessFinalizedCopyState> CreateMarkerlessFinalizedCopyStateAsync()
    {
        var source = FileService.GetTempDirectory("move-processor-markerless-copy-src");
        await FileService.GetFileAsync(source, "book.m4b", "verified audio");
        var target = Path.Join(
            FileService.GetTempPath(),
            $"move-processor-markerless-copy-dst-{Guid.NewGuid():N}");
        var audiobook = await _audiobookRepository.AddAsync(new Audiobook
        {
            Title = "Markerless Copy Recovery",
            BasePath = source
        });
        var (queue, job) = await CreateQueuedMoveJobAsync(
            audiobook,
            target,
            source,
            deleteEmptySource: false);
        var service = _provider.GetRequiredService<AudiobookContentMoveService>();
        var request = CreateMoveRequest(source, target, job, deleteEmptySource: false);
        var result = await service.MoveContentsAsync(request, CancellationToken.None);
        audiobook.BasePath = target;
        await _audiobookRepository.UpdateAsync(audiobook);
        service.FinalizeMove(request, result);
        File.Delete(result.RecoveryMarkerPath);
        return new MarkerlessFinalizedCopyState(queue, job, source, target);
    }

    private static AudiobookContentMoveRequest CreateMoveRequest(
        string source,
        string target,
        MoveJob job,
        bool deleteEmptySource) =>
        new(
            source,
            target,
            job.Id,
            deleteEmptySource,
            FileSystemPathSemantics.CurrentHostDefault,
            FileSystemPathSemantics.CurrentHostDefault,
            new MoveLeaseToken(LeaseOwner, job.LeaseGeneration));

    private static bool TryCreateProcessorDirectoryLink(
        string linkPath,
        string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static void TryRemoveProcessorDirectoryLink(string linkPath)
    {
        try
        {
            if (Directory.Exists(linkPath))
            {
                Directory.Delete(linkPath, recursive: false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Failed to remove processor test directory link '{linkPath}': {exception.Message}");
        }
    }

    private sealed record MarkerlessFinalizedCopyState(
        IMoveQueueService Queue,
        MoveJob Job,
        string Source,
        string Target);
}
