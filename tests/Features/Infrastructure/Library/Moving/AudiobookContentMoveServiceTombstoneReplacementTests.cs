using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Library.Moving;

public partial class AudiobookContentMoveServiceTests
{
    [Fact]
    public async Task GetRecoverableMoveAsync_TombstonedQuarantineReplacedByFile_PreservesEvidence()
    {
        var source = FileService.GetTempDirectory("content-move-tombstone-file-src");
        await FileService.GetFileAsync(source, "book.m4b", "verified audio");
        var target = Path.Join(
            FileService.GetTempPath(),
            $"content-move-tombstone-file-dst-{Guid.NewGuid():N}");
        var request = await CreateLeasedMoveRequestAsync(source, target);
        var faultingService = new AudiobookContentMoveService(
            _provider.GetRequiredService<ILogger<AudiobookContentMoveService>>(),
            _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>(),
            TimeProvider.System,
            new OwnershipCleanupFaultInjector(
                OwnershipCleanupFaultPoint.BeforeDirectoryDelete));

        await Assert.ThrowsAsync<IOException>(() =>
            faultingService.MoveContentsAsync(request, CancellationToken.None));

        var sourceParent = Path.GetDirectoryName(source)!;
        var quarantineRoot = Path.Join(
            sourceParent,
            $".listenarr-quarantine-{request.JobId:N}");
        var cleanupDirectory = Path.Join(
            sourceParent,
            $".listenarr-quarantine-directory-{request.JobId:N}.cleanup-dir");
        var tombstonePath = Path.Join(
            sourceParent,
            $".listenarr-quarantine-directory-{request.JobId:N}.cleanup.json");
        await File.WriteAllTextAsync(quarantineRoot, "replacement file");
        var service = _provider.GetRequiredService<AudiobookContentMoveService>();

        var exception = await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
            service.GetRecoverableMoveAsync(request, CancellationToken.None));

        Assert.Contains("recreated", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("replacement file", await File.ReadAllTextAsync(quarantineRoot));
        Assert.True(Directory.Exists(cleanupDirectory));
        Assert.True(File.Exists(tombstonePath));
        Assert.True(File.Exists(Path.Join(target, "book.m4b")));
    }
}
