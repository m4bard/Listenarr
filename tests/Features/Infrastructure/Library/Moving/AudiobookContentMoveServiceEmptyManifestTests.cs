using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Library.Moving;

public partial class AudiobookContentMoveServiceTests
{
    [Fact]
    public async Task MoveContentsAsync_EmptyCopyMove_PersistsRootManifestAndRetainsConfiguredSource()
    {
        var source = FileService.GetTempDirectory("content-move-empty-copy-src");
        var target = Path.Join(
            FileService.GetTempPath(),
            $"content-move-empty-copy-dst-{Guid.NewGuid():N}");
        var request = await CreateLeasedMoveRequestAsync(
            source,
            target,
            deleteEmptySource: false);
        var service = _provider.GetRequiredService<AudiobookContentMoveService>();

        var result = await service.MoveContentsAsync(request, CancellationToken.None);

        Assert.True(result.SourceCleanupCompleted);
        Assert.True(Directory.Exists(source));
        Assert.Empty(Directory.EnumerateFileSystemEntries(source));
        Assert.True(Directory.Exists(target));
        var manifest = await LoadPersistedManifestAsync(request.JobId);
        var root = Assert.Single(manifest);
        Assert.Equal(MoveJobEntryType.Directory, root.EntryType);
        Assert.Equal(string.Empty, root.RelativePath);
    }

    private async Task<List<MoveJobEntry>> LoadPersistedManifestAsync(Guid jobId)
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        return await db.MoveJobEntries
            .AsNoTracking()
            .Where(entry => entry.MoveJobId == jobId)
            .OrderBy(entry => entry.Id)
            .ToListAsync();
    }

    [Fact]
    public async Task VerifyFinalizedMoveAsync_PhaseOnlyMarkerlessAtomicState_RequiresAttention()
    {
        var source = FileService.GetTempDirectory("content-move-phase-only-atomic-src");
        await FileService.GetFileAsync(source, "book.m4b", "audio");
        var target = Path.Join(
            Path.GetDirectoryName(source)!,
            $"content-move-phase-only-atomic-dst-{Guid.NewGuid():N}");
        var request = await CreateLeasedMoveRequestAsync(source, target);
        Directory.Move(source, target);
        var factory = _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var job = await db.MoveJobs.SingleAsync(candidate => candidate.Id == request.JobId);
            job.Phase = MoveJobPhase.CleaningArtifacts;
            await db.SaveChangesAsync();
        }

        var service = _provider.GetRequiredService<AudiobookContentMoveService>();
        var exception = await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
            service.VerifyFinalizedMoveAsync(request, CancellationToken.None));

        Assert.Contains("without a persisted manifest", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Join(target, "book.m4b")));
    }

    [Fact]
    public async Task VerifyFinalizedMoveAsync_MarkerlessEmptyAtomicTarget_RemainsVerifiable()
    {
        var source = FileService.GetTempDirectory("content-move-empty-atomic-src");
        var target = Path.Join(
            Path.GetDirectoryName(source)!,
            $"content-move-empty-atomic-dst-{Guid.NewGuid():N}");
        var request = await CreateLeasedMoveRequestAsync(source, target);
        var service = _provider.GetRequiredService<AudiobookContentMoveService>();
        var result = await service.MoveContentsAsync(request, CancellationToken.None);
        File.Delete(result.RecoveryMarkerPath);

        await service.VerifyFinalizedMoveAsync(request, CancellationToken.None);

        Assert.False(Directory.Exists(source));
        Assert.True(Directory.Exists(target));
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }

    [Fact]
    public async Task VerifyFinalizedMoveAsync_MarkerlessEmptyAtomicTargetReplacedWithContent_RequiresAttention()
    {
        var source = FileService.GetTempDirectory("content-move-empty-atomic-tampered-src");
        var target = Path.Join(
            Path.GetDirectoryName(source)!,
            $"content-move-empty-atomic-tampered-dst-{Guid.NewGuid():N}");
        var request = await CreateLeasedMoveRequestAsync(source, target);
        var service = _provider.GetRequiredService<AudiobookContentMoveService>();
        var result = await service.MoveContentsAsync(request, CancellationToken.None);
        File.Delete(result.RecoveryMarkerPath);
        var unrelated = await FileService.GetFileAsync(
            target,
            "operator-note.txt",
            "preserve me");

        await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
            service.VerifyFinalizedMoveAsync(request, CancellationToken.None));

        Assert.Equal("preserve me", await File.ReadAllTextAsync(unrelated));
    }
}
