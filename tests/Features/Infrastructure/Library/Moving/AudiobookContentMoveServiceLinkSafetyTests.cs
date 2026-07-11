using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Library.Moving;

public partial class AudiobookContentMoveServiceTests
{
    [Fact]
    public async Task MoveContentsAsync_SourceDirectoryLink_BlocksAtomicRename()
    {
        var externalSource = FileService.GetTempDirectory("content-move-root-link-external");
        var externalFile = await FileService.GetFileAsync(externalSource, "book.m4b", "external audio");
        var linkParent = FileService.GetTempDirectory("content-move-root-link-parent");
        var sourceLink = Path.Join(linkParent, "linked-source");
        if (!TryCreateDirectoryLink(sourceLink, externalSource))
        {
            return;
        }

        try
        {
            var target = Path.Join(linkParent, $"target-{Guid.NewGuid():N}");
            var request = await CreateLeasedMoveRequestAsync(sourceLink, target);

            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
                service.MoveContentsAsync(request, CancellationToken.None));

            Assert.True(Directory.Exists(sourceLink));
            Assert.True(File.Exists(externalFile));
            Assert.Equal("external audio", await File.ReadAllTextAsync(externalFile));
            Assert.False(Directory.Exists(target));
        }
        finally
        {
            TryRemoveDirectoryLink(sourceLink);
        }
    }

    [Fact]
    public async Task MoveContentsAsync_WindowsSourceJunction_BlocksAtomicRename()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var externalSource = FileService.GetTempDirectory("content-move-root-junction-external");
        var externalFile = await FileService.GetFileAsync(externalSource, "book.m4b", "external audio");
        var linkParent = FileService.GetTempDirectory("content-move-root-junction-parent");
        var sourceJunction = Path.Join(linkParent, "junction-source");
        if (!TryCreateWindowsJunction(sourceJunction, externalSource))
        {
            return;
        }

        try
        {
            var target = Path.Join(linkParent, $"target-{Guid.NewGuid():N}");
            var request = await CreateLeasedMoveRequestAsync(sourceJunction, target);

            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
                service.MoveContentsAsync(request, CancellationToken.None));

            Assert.True(Directory.Exists(sourceJunction));
            Assert.True(File.Exists(externalFile));
            Assert.False(Directory.Exists(target));
        }
        finally
        {
            TryRemoveDirectoryLink(sourceJunction);
        }
    }

    [Fact]
    public async Task MoveContentsAsync_NestedDirectoryLink_BlocksAtomicRename()
    {
        var source = FileService.GetTempDirectory("content-move-nested-link-src");
        var sourceFile = await FileService.GetFileAsync(source, "book.m4b", "audio");
        var external = FileService.GetTempDirectory("content-move-nested-link-external");
        var externalFile = await FileService.GetFileAsync(external, "external.txt", "external");
        var nestedLink = Path.Join(source, "linked");
        if (!TryCreateDirectoryLink(nestedLink, external))
        {
            return;
        }

        try
        {
            var target = Path.Join(FileService.GetTempPath(), $"content-move-nested-link-dst-{Guid.NewGuid():N}");
            var request = await CreateLeasedMoveRequestAsync(source, target);

            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
                service.MoveContentsAsync(request, CancellationToken.None));

            Assert.True(File.Exists(sourceFile));
            Assert.True(File.Exists(externalFile));
            Assert.False(Directory.Exists(target));
        }
        finally
        {
            TryRemoveDirectoryLink(nestedLink);
        }
    }

    [Fact]
    public async Task MoveContentsAsync_NestedFileSymlink_BlocksAtomicRename()
    {
        var source = FileService.GetTempDirectory("content-move-file-link-src");
        var sourceFile = await FileService.GetFileAsync(source, "book.m4b", "audio");
        var external = FileService.GetTempDirectory("content-move-file-link-external");
        var externalFile = await FileService.GetFileAsync(external, "external.txt", "external");
        var linkedFile = Path.Join(source, "linked.txt");
        try
        {
            File.CreateSymbolicLink(linkedFile, externalFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        try
        {
            var target = Path.Join(FileService.GetTempPath(), $"content-move-file-link-dst-{Guid.NewGuid():N}");
            var request = await CreateLeasedMoveRequestAsync(source, target);
            var service = _provider.GetRequiredService<AudiobookContentMoveService>();

            await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
                service.MoveContentsAsync(request, CancellationToken.None));

            Assert.True(File.Exists(sourceFile));
            Assert.True(File.Exists(linkedFile));
            Assert.True(File.Exists(externalFile));
            Assert.False(Directory.Exists(target));
        }
        finally
        {
            if (File.Exists(linkedFile))
            {
                File.Delete(linkedFile);
            }
        }
    }

    [Fact]
    public async Task MoveContentsAsync_NormalSameVolumeSource_UsesAtomicRename()
    {
        var source = FileService.GetTempDirectory("content-move-atomic-normal-src");
        await FileService.GetFileAsync(source, "book.m4b", "audio");
        var target = Path.Join(
            Path.GetDirectoryName(source)!,
            $"content-move-atomic-normal-dst-{Guid.NewGuid():N}");
        var jobId = Guid.NewGuid();
        var request = await CreateLeasedMoveRequestAsync(source, target, jobId);

        var service = _provider.GetRequiredService<AudiobookContentMoveService>();
        var result = await service.MoveContentsAsync(request, CancellationToken.None);

        Assert.False(Directory.Exists(source));
        Assert.True(File.Exists(Path.Join(target, "book.m4b")));
        Assert.True(File.Exists(result.RecoveryMarkerPath));
        var factory = _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.MoveJobEntries.AnyAsync(entry => entry.MoveJobId == jobId));
    }

    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return OperatingSystem.IsWindows()
                && TryCreateWindowsJunction(linkPath, targetPath);
        }
    }

    private static bool TryCreateWindowsJunction(string linkPath, string targetPath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/d /c mklink /J \"{linkPath}\" \"{targetPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            process?.WaitForExit();
            return process?.ExitCode == 0 && Directory.Exists(linkPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryRemoveDirectoryLink(string linkPath)
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
                $"Failed to remove test directory link '{linkPath}': {exception.Message}");
        }
    }
}
