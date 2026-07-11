using System.Text.Json;

namespace Listenarr.Tests.Features.Infrastructure.Library.Moving;

public partial class AudiobookContentMoveServiceTests
{
    [Fact]
    public async Task GetRecoverableMoveAsync_AtomicMarkerWithSourceAndTarget_RequiresAttention()
    {
        var source = FileService.GetTempDirectory("content-move-atomic-both-src");
        await FileService.GetFileAsync(source, "book.m4b", "source audio");
        var target = FileService.GetTempDirectory("content-move-atomic-both-dst");
        await FileService.GetFileAsync(target, "book.m4b", "target audio");
        var jobId = Guid.NewGuid();
        var request = await CreateLeasedMoveRequestAsync(source, target, jobId);
        await WriteRecoveryMarkerAsync(target, jobId, source, target, "atomic-rename-complete");

        var service = _provider.GetRequiredService<AudiobookContentMoveService>();
        var exception = await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
            service.GetRecoverableMoveAsync(request));

        Assert.Contains("Both source and target exist", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("source audio", await File.ReadAllTextAsync(Path.Join(source, "book.m4b")));
        Assert.Equal("target audio", await File.ReadAllTextAsync(Path.Join(target, "book.m4b")));
    }

    [Fact]
    public async Task GetRecoverableMoveAsync_AtomicMarkerBeforeRename_DoesNotRecover()
    {
        var source = FileService.GetTempDirectory("content-move-atomic-before-src");
        await FileService.GetFileAsync(source, "book.m4b", "audio");
        var target = Path.Join(FileService.GetTempPath(), $"content-move-atomic-before-dst-{Guid.NewGuid():N}");
        var jobId = Guid.NewGuid();
        var request = await CreateLeasedMoveRequestAsync(source, target, jobId);
        await WriteRecoveryMarkerAsync(source, jobId, source, target, "atomic-rename-complete");

        var service = _provider.GetRequiredService<AudiobookContentMoveService>();
        var result = await service.GetRecoverableMoveAsync(request);

        Assert.Null(result);
        Assert.True(Directory.Exists(source));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public async Task GetRecoverableMoveAsync_MissingSourceAndTarget_DoesNotRecover()
    {
        var source = FileService.GetTempDirectory("content-move-atomic-neither-src");
        var target = Path.Join(FileService.GetTempPath(), $"content-move-atomic-neither-dst-{Guid.NewGuid():N}");
        var jobId = Guid.NewGuid();
        var request = await CreateLeasedMoveRequestAsync(source, target, jobId);
        Directory.Delete(source, recursive: true);

        var service = _provider.GetRequiredService<AudiobookContentMoveService>();
        var result = await service.GetRecoverableMoveAsync(request);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRecoverableMoveAsync_LegacyAtomicMarker_RequiresAttention()
    {
        var source = FileService.GetTempDirectory("content-move-legacy-atomic-src");
        await FileService.GetFileAsync(source, "book.m4b", "audio");
        var target = Path.Join(FileService.GetTempPath(), $"content-move-legacy-atomic-dst-{Guid.NewGuid():N}");
        var jobId = Guid.NewGuid();
        var request = await CreateLeasedMoveRequestAsync(source, target, jobId);
        await File.WriteAllTextAsync(
            Path.Join(source, $".listenarr-move-{jobId:N}.pending"),
            "atomic-rename-complete");
        Directory.Move(source, target);

        var service = _provider.GetRequiredService<AudiobookContentMoveService>();
        var exception = await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
            service.GetRecoverableMoveAsync(request));

        Assert.Contains("legacy atomic rename marker", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Join(target, "book.m4b")));
    }

    [Fact]
    public async Task GetRecoverableMoveAsync_AtomicMarkerWithWrongIdentity_RequiresAttention()
    {
        var source = FileService.GetTempDirectory("content-move-atomic-wrong-src");
        await FileService.GetFileAsync(source, "book.m4b", "audio");
        var target = Path.Join(FileService.GetTempPath(), $"content-move-atomic-wrong-dst-{Guid.NewGuid():N}");
        var jobId = Guid.NewGuid();
        var request = await CreateLeasedMoveRequestAsync(source, target, jobId);
        await WriteRecoveryMarkerAsync(
            source,
            Guid.NewGuid(),
            source,
            target,
            "atomic-rename-complete",
            markerFileJobId: jobId);
        Directory.Move(source, target);

        var service = _provider.GetRequiredService<AudiobookContentMoveService>();
        await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
            service.GetRecoverableMoveAsync(request));
    }

    [Fact]
    public async Task GetRecoverableMoveAsync_AtomicMarkerWithPersistedManifest_RequiresAttention()
    {
        var source = FileService.GetTempDirectory("content-move-atomic-manifest-src");
        var sourceFile = await FileService.GetFileAsync(source, "book.m4b", "audio");
        var target = Path.Join(FileService.GetTempPath(), $"content-move-atomic-manifest-dst-{Guid.NewGuid():N}");
        var jobId = Guid.NewGuid();
        var request = await CreateLeasedMoveRequestAsync(source, target, jobId);
        await PersistFileManifestAsync(jobId, "book.m4b", sourceFile);
        await WriteRecoveryMarkerAsync(source, jobId, source, target, "atomic-rename-complete");
        Directory.Move(source, target);

        var service = _provider.GetRequiredService<AudiobookContentMoveService>();
        var exception = await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
            service.GetRecoverableMoveAsync(request));

        Assert.Contains("persisted copy manifest", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Join(target, "book.m4b")));
    }

    [Fact]
    public async Task GetRecoverableMoveAsync_UnreadableMarker_RequiresAttention()
    {
        var source = FileService.GetTempDirectory("content-move-unreadable-marker-src");
        await FileService.GetFileAsync(source, "book.m4b", "audio");
        var target = FileService.GetTempDirectory("content-move-unreadable-marker-dst");
        var jobId = Guid.NewGuid();
        var request = await CreateLeasedMoveRequestAsync(source, target, jobId);
        await File.WriteAllTextAsync(
            Path.Join(target, $".listenarr-move-{jobId:N}.pending"),
            "{ truncated");

        var service = _provider.GetRequiredService<AudiobookContentMoveService>();
        var exception = await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
            service.GetRecoverableMoveAsync(request));

        Assert.Contains("could not be read safely", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Join(source, "book.m4b")));
    }

    [Fact]
    public async Task GetRecoverableMoveAsync_AtomicMarkerWithLinkedTarget_RequiresAttention()
    {
        var source = FileService.GetTempDirectory("content-move-atomic-linked-src");
        await FileService.GetFileAsync(source, "book.m4b", "audio");
        var targetParent = FileService.GetTempDirectory("content-move-atomic-linked-parent");
        var target = Path.Join(targetParent, "linked-target");
        var externalTarget = FileService.GetTempDirectory("content-move-atomic-linked-external");
        var externalFile = await FileService.GetFileAsync(externalTarget, "book.m4b", "external audio");
        var jobId = Guid.NewGuid();
        var request = await CreateLeasedMoveRequestAsync(source, target, jobId);
        await WriteRecoveryMarkerAsync(externalTarget, jobId, source, target, "atomic-rename-complete");
        Directory.Delete(source, recursive: true);
        if (!TryCreateDirectoryLink(target, externalTarget))
        {
            return;
        }

        try
        {
            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
                service.GetRecoverableMoveAsync(request));

            Assert.Equal("external audio", await File.ReadAllTextAsync(externalFile));
        }
        finally
        {
            TryRemoveDirectoryLink(target);
        }
    }

    private static Task WriteRecoveryMarkerAsync(
        string directory,
        Guid markerJobId,
        string source,
        string target,
        string stage,
        Guid? markerFileJobId = null)
    {
        var fileJobId = markerFileJobId ?? markerJobId;
        return File.WriteAllTextAsync(
            Path.Join(directory, $".listenarr-move-{fileJobId:N}.pending"),
            JsonSerializer.Serialize(new
            {
                Version = 1,
                JobId = markerJobId,
                Source = Path.GetFullPath(source),
                Target = Path.GetFullPath(target),
                Stage = stage
            }));
    }
}
