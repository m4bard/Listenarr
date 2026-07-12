using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Library.Moving;

public partial class AudiobookContentMoveServiceTests
{
    [Fact]
    public async Task MoveContentsAsync_IdenticalEndpoints_RejectsBeforeMarkerCreation()
    {
        var source = FileService.GetTempDirectory("content-move-identical-endpoint");
        await FileService.GetFileAsync(source, "book.m4b", "audio");
        var request = await CreateLeasedMoveRequestAsync(source, source);
        var markerPath = Path.Join(
            source,
            $".listenarr-move-{request.JobId:N}.pending");
        var service = _provider.GetRequiredService<AudiobookContentMoveService>();

        var exception = await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
            service.MoveContentsAsync(request, CancellationToken.None));

        Assert.Contains("distinct non-root", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Join(source, "book.m4b")));
        Assert.False(File.Exists(markerPath));
    }

    [Fact]
    public async Task MoveContentsAsync_TrailingSeparator_NormalizesEndpointAndRecoveryIdentity()
    {
        var source = FileService.GetTempDirectory("content-move-trailing-endpoint-src");
        await FileService.GetFileAsync(source, "book.m4b", "audio");
        var target = Path.Join(
            Path.GetDirectoryName(source)!,
            $"content-move-trailing-endpoint-dst-{Guid.NewGuid():N}");
        var requestedTarget = target + Path.DirectorySeparatorChar;
        var request = await CreateLeasedMoveRequestAsync(source, requestedTarget);
        var service = _provider.GetRequiredService<AudiobookContentMoveService>();

        var result = await service.MoveContentsAsync(request, CancellationToken.None);
        var recovered = await service.GetRecoverableMoveAsync(
            request,
            CancellationToken.None);

        Assert.Equal(Path.GetFullPath(target), result.Target);
        Assert.NotNull(recovered);
        Assert.Equal(Path.GetFullPath(target), recovered!.Target);
        Assert.False(Directory.Exists(source));
        Assert.True(File.Exists(Path.Join(target, "book.m4b")));
    }

    [Fact]
    public async Task MoveContentsAsync_LegacyJobWithUnownedPartial_DoesNotPersistSourceIdentity()
    {
        var source = FileService.GetTempDirectory("content-move-legacy-partial-src");
        await FileService.GetFileAsync(source, "book.m4b", "audio");
        var target = FileService.GetTempDirectory("content-move-legacy-partial-dst");
        var request = await CreateLeasedMoveRequestAsync(source, target);
        var partialPath = Path.Join(
            target,
            $"book.m4b.listenarr-{request.JobId:N}.partial");
        await File.WriteAllTextAsync(partialPath, "partial audio");
        var factory = _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var job = await db.MoveJobs.SingleAsync(candidate => candidate.Id == request.JobId);
            job.SourcePath = null;
            await db.SaveChangesAsync();
        }

        var service = _provider.GetRequiredService<AudiobookContentMoveService>();
        var exception = await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
            service.MoveContentsAsync(request, CancellationToken.None));

        Assert.Contains("legacy move", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("partial audio", await File.ReadAllTextAsync(partialPath));
        await using var verificationDb = await factory.CreateDbContextAsync();
        Assert.Null((await verificationDb.MoveJobs.SingleAsync(
            candidate => candidate.Id == request.JobId)).SourcePath);
    }

    [Fact]
    public async Task MoveContentsAsync_PathTooLongPersistedTarget_RequiresAttentionWithoutMutation()
    {
        var source = FileService.GetTempDirectory("content-move-invalid-persisted-target-src");
        await FileService.GetFileAsync(source, "book.m4b", "audio");
        var target = Path.Join(
            FileService.GetTempPath(),
            $"content-move-invalid-persisted-target-dst-{Guid.NewGuid():N}");
        var request = await CreateLeasedMoveRequestAsync(source, target);
        var factory = _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var job = await db.MoveJobs.SingleAsync(candidate => candidate.Id == request.JobId);
            job.RequestedPath = new string('x', 40_000);
            await db.SaveChangesAsync();
        }

        var service = _provider.GetRequiredService<AudiobookContentMoveService>();
        var exception = await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
            service.MoveContentsAsync(request, CancellationToken.None));

        Assert.Contains("target identity is invalid", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Join(source, "book.m4b")));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public async Task MoveContentsAsync_FilesystemRootSource_RejectsBeforeTargetMutation()
    {
        var filesystemRoot = Path.GetPathRoot(FileService.GetTempPath())!;
        var target = Path.Join(
            FileService.GetTempPath(),
            $"content-move-root-source-dst-{Guid.NewGuid():N}");
        var request = await CreateLeasedMoveRequestAsync(filesystemRoot, target);
        var sourceMarkerPath = Path.Join(
            filesystemRoot,
            $".listenarr-move-{request.JobId:N}.pending");
        var service = _provider.GetRequiredService<AudiobookContentMoveService>();

        var exception = await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
            service.MoveContentsAsync(request, CancellationToken.None));

        Assert.Contains("distinct non-root", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(target));
        Assert.False(File.Exists(sourceMarkerPath));
    }

    [Fact]
    public async Task MoveContentsAsync_FilesystemRootTarget_RejectsBeforeSourceMutation()
    {
        var source = FileService.GetTempDirectory("content-move-root-target-src");
        await FileService.GetFileAsync(source, "book.m4b", "audio");
        var filesystemRoot = Path.GetPathRoot(source)!;
        var request = await CreateLeasedMoveRequestAsync(source, filesystemRoot);
        var sourceMarkerPath = Path.Join(
            source,
            $".listenarr-move-{request.JobId:N}.pending");
        var service = _provider.GetRequiredService<AudiobookContentMoveService>();

        var exception = await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
            service.MoveContentsAsync(request, CancellationToken.None));

        Assert.Contains("distinct non-root", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Join(source, "book.m4b")));
        Assert.False(File.Exists(sourceMarkerPath));
    }
}
