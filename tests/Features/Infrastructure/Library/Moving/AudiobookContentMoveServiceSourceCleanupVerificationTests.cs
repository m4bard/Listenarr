namespace Listenarr.Tests.Features.Infrastructure.Library.Moving;

public partial class AudiobookContentMoveServiceTests
{
    [Fact]
    public async Task GetRecoverableMoveAsync_SourceCleanupCompleteWithRecreatedFile_RequiresAttention()
    {
        var state = await CreateSourceCleanupCompletedStateAsync(deleteEmptySource: true);
        Directory.CreateDirectory(state.Source);
        await FileService.GetFileAsync(state.Source, "recreated.txt", "do not delete");
        var service = _provider.GetRequiredService<AudiobookContentMoveService>();

        var exception = await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
            service.GetRecoverableMoveAsync(state.Request));

        Assert.Contains("recreated or uncleared content", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "do not delete",
            await File.ReadAllTextAsync(Path.Join(state.Source, "recreated.txt")));
        Assert.True(File.Exists(state.MarkerPath));
    }

    [Fact]
    public async Task GetRecoverableMoveAsync_SourceCleanupCompleteWithRecreatedEmptySource_RequiresAttention()
    {
        var state = await CreateSourceCleanupCompletedStateAsync(deleteEmptySource: true);
        Directory.CreateDirectory(state.Source);
        var service = _provider.GetRequiredService<AudiobookContentMoveService>();

        var exception = await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
            service.GetRecoverableMoveAsync(state.Request));

        Assert.Contains("recreated after cleanup", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(state.Source));
        Assert.True(File.Exists(state.MarkerPath));
    }

    [Fact]
    public async Task GetRecoverableMoveAsync_SourceCleanupCompleteRetainedEmptySource_IsValidWhenConfigured()
    {
        var state = await CreateSourceCleanupCompletedStateAsync(deleteEmptySource: false);
        Directory.CreateDirectory(state.Source);
        var service = _provider.GetRequiredService<AudiobookContentMoveService>();

        var result = await service.GetRecoverableMoveAsync(state.Request);

        Assert.NotNull(result);
        Assert.True(result.SourceCleanupCompleted);
        Assert.True(Directory.Exists(state.Source));
        Assert.True(File.Exists(state.MarkerPath));
    }

    private async Task<SourceCleanupCompletedState> CreateSourceCleanupCompletedStateAsync(
        bool deleteEmptySource)
    {
        var source = FileService.GetTempDirectory("content-move-source-cleanup-state-src");
        var sourceFile = await FileService.GetFileAsync(source, "book.m4b", "verified audio");
        var target = FileService.GetTempDirectory("content-move-source-cleanup-state-dst");
        await FileService.GetFileAsync(target, "book.m4b", "verified audio");
        var jobId = Guid.NewGuid();
        var request = await CreateLeasedMoveRequestAsync(
            source,
            target,
            jobId,
            deleteEmptySource);
        await PersistFileManifestAsync(jobId, "book.m4b", sourceFile);
        Directory.Delete(source, recursive: true);
        await WriteRecoveryMarkerAsync(
            target,
            jobId,
            source,
            target,
            "source-cleanup-complete");
        return new SourceCleanupCompletedState(
            source,
            target,
            Path.Join(target, $".listenarr-move-{jobId:N}.pending"),
            request);
    }

    private sealed record SourceCleanupCompletedState(
        string Source,
        string Target,
        string MarkerPath,
        AudiobookContentMoveRequest Request);
}
