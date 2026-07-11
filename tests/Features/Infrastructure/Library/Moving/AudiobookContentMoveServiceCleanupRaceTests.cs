using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Library.Moving;

public partial class AudiobookContentMoveServiceTests
{
    [Fact]
    public async Task MoveContentsAsync_SourceRootReplacedBeforeQuarantineMove_PreservesExternalFile()
    {
        var capabilityParent = FileService.GetTempDirectory("content-move-cleanup-race-capability");
        var capabilityTarget = FileService.GetTempDirectory("content-move-cleanup-race-capability-target");
        var capabilityLink = Path.Join(capabilityParent, "link");
        if (!TryCreateDirectoryLink(capabilityLink, capabilityTarget))
        {
            return;
        }

        TryRemoveDirectoryLink(capabilityLink);
        var source = FileService.GetTempDirectory("content-move-cleanup-race-src");
        await FileService.GetFileAsync(source, "book.m4b", "verified audio");
        var sourceBackup = source + $"-backup-{Guid.NewGuid():N}";
        var target = Path.Join(
            FileService.GetTempPath(),
            $"content-move-cleanup-race-dst-{Guid.NewGuid():N}");
        var external = FileService.GetTempDirectory("content-move-cleanup-race-external");
        var externalFile = await FileService.GetFileAsync(
            external,
            "book.m4b",
            "verified audio");
        var request = await CreateLeasedMoveRequestAsync(source, target);
        var injector = new ReplaceSourceRootBeforeCleanupMove(
            source,
            sourceBackup,
            external);
        var service = new AudiobookContentMoveService(
            _provider.GetRequiredService<ILogger<AudiobookContentMoveService>>(),
            _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>(),
            TimeProvider.System,
            injector);

        try
        {
            await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
                service.MoveContentsAsync(request, CancellationToken.None));

            Assert.Equal("verified audio", await File.ReadAllTextAsync(externalFile));
            Assert.True(File.Exists(Path.Join(sourceBackup, "book.m4b")));
            Assert.True(File.Exists(Path.Join(target, "book.m4b")));
        }
        finally
        {
            TryRemoveDirectoryLink(source);
            if (Directory.Exists(sourceBackup) && !Directory.Exists(source))
            {
                Directory.Move(sourceBackup, source);
            }
        }
    }

    [Fact]
    public async Task MoveContentsAsync_TargetFileReplacedBeforeQuarantineDelete_PreservesQuarantineAndExternalFile()
    {
        var capabilityRoot = FileService.GetTempDirectory("content-move-target-race-capability");
        var capabilityTarget = await FileService.GetFileAsync(
            capabilityRoot,
            "target.bin",
            "capability");
        var capabilityLink = Path.Join(capabilityRoot, "link.bin");
        try
        {
            File.CreateSymbolicLink(capabilityLink, capabilityTarget);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        File.Delete(capabilityLink);
        var source = FileService.GetTempDirectory("content-move-target-race-src");
        await FileService.GetFileAsync(source, "book.m4b", "verified audio");
        var target = Path.Join(
            FileService.GetTempPath(),
            $"content-move-target-race-dst-{Guid.NewGuid():N}");
        var external = FileService.GetTempDirectory("content-move-target-race-external");
        var externalFile = await FileService.GetFileAsync(
            external,
            "book.m4b",
            "verified audio");
        var request = await CreateLeasedMoveRequestAsync(source, target);
        var injector = new ReplaceTargetFileBeforeQuarantineDelete(
            Path.Join(target, "book.m4b"),
            externalFile);
        var service = new AudiobookContentMoveService(
            _provider.GetRequiredService<ILogger<AudiobookContentMoveService>>(),
            _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>(),
            TimeProvider.System,
            injector);

        try
        {
            await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
                service.MoveContentsAsync(request, CancellationToken.None));

            var quarantineRoot = Path.Join(
                Path.GetDirectoryName(source)!,
                $".listenarr-quarantine-{request.JobId:N}");
            Assert.Equal("verified audio", await File.ReadAllTextAsync(externalFile));
            Assert.True(File.Exists(Path.Join(quarantineRoot, "book.m4b")));
            Assert.True(File.Exists(Path.Join(target, "book.m4b")));
        }
        finally
        {
            var linkedTargetFile = Path.Join(target, "book.m4b");
            if (File.Exists(linkedTargetFile))
            {
                File.Delete(linkedTargetFile);
            }
        }
    }

    private sealed class ReplaceTargetFileBeforeQuarantineDelete(
        string targetFile,
        string externalFile) : IMoveFaultInjector
    {
        private bool _replaced;

        public void OnSourceCleanupMutation(
            Guid jobId,
            SourceCleanupFaultPoint faultPoint)
        {
            if (_replaced || faultPoint != SourceCleanupFaultPoint.BeforeQuarantineFileDelete)
            {
                return;
            }

            File.Delete(targetFile);
            File.CreateSymbolicLink(targetFile, externalFile);
            _replaced = true;
        }
    }

    private sealed class ReplaceSourceRootBeforeCleanupMove(
        string source,
        string sourceBackup,
        string external) : IMoveFaultInjector
    {
        private bool _replaced;

        public void OnSourceCleanupMutation(
            Guid jobId,
            SourceCleanupFaultPoint faultPoint)
        {
            if (_replaced || faultPoint != SourceCleanupFaultPoint.BeforeSourceFileMove)
            {
                return;
            }

            Directory.Move(source, sourceBackup);
            if (!TryCreateDirectoryLink(source, external))
            {
                throw new IOException("The source replacement link could not be created.");
            }

            _replaced = true;
        }
    }
}
