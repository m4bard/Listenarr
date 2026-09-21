using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Api.Services;

/// <summary>
/// Pins the publication behaviour of <see cref="FileAction.HardlinkCopy"/> when the hardlink
/// step cannot produce a link. The existing coverage in FileMoverHardlinkTests only ever
/// injects an <see cref="IOException"/>, so it cannot distinguish a fallback that is reached
/// for every failure shape from one that is reached for a single exception type, and it never
/// exercises a real filesystem boundary.
/// </summary>
[Trait("Area", "FileSystem")]
[Trait("Name", "FileMoverHardlinkFallbackTests")]
[Trait("Category", "FileSystem")]
public sealed class FileMoverHardlinkFallbackTests : BaseTests
{
    /// <summary>
    /// A real cross-filesystem publication. The destination arrives with the source content and
    /// is an independent inode, proven by writing through the destination and observing that the
    /// source does not follow. The same-volume control below must come out the other way.
    /// </summary>
    [CrossVolumeFact]
    public async Task PerformActionOn_HardlinkCopy_RealCrossVolumeCopiesAndKeepsSource()
    {
        var sourceRoot = FileService.GetTempDirectory("hardlink-fallback-cross-volume");
        var source = await FileService.GetFileAsync(sourceRoot, "source.mp3", "audio content");
        var destinationRoot = Path.Join(
            Path.GetFullPath(Environment.GetEnvironmentVariable(
                CrossVolumeFactAttribute.DestinationPathEnvironmentVariable)
                ?? throw new InvalidOperationException(
                    "A real cross-volume destination was not provided.")),
            $"listenarr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destinationRoot);
        var destination = Path.Join(destinationRoot, "destination.mp3");
        var operationId = Guid.NewGuid();

        try
        {
            Assert.True(await CreateMover().PerformActionOn(
                FileAction.HardlinkCopy,
                source,
                destination,
                operationId));

            Assert.True(File.Exists(source));
            Assert.Equal("audio content", await File.ReadAllTextAsync(destination));
            await AssertIsIndependentCopyAsync(source, destination);
            await AssertCompletedJournalAsync(operationId, FileAction.HardlinkCopy);
        }
        finally
        {
            if (Directory.Exists(destinationRoot))
            {
                Directory.Delete(destinationRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// The control for the cross-volume case. On one filesystem the publication must still be a
    /// real link, not a copy, so a fallback that fired unconditionally would fail here.
    /// </summary>
    [Fact]
    public async Task PerformActionOn_HardlinkCopy_SameVolumeStillLinksRatherThanCopies()
    {
        var root = FileService.GetTempDirectory("hardlink-fallback-same-volume");
        var source = await FileService.GetFileAsync(root, "source.mp3", "audio content");
        var destination = Path.Join(root, "destination.mp3");
        var operationId = Guid.NewGuid();

        Assert.True(await CreateMover().PerformActionOn(
            FileAction.HardlinkCopy,
            source,
            destination,
            operationId));

        await AssertIsSameInodeAsync(source, destination);
        await AssertCompletedJournalAsync(operationId, FileAction.HardlinkCopy);
    }

    /// <summary>
    /// A cross-device link error surfaces as a <see cref="Win32Exception"/> carrying EXDEV,
    /// because PinnedFileEntry.CreateHardLinkTo raises Marshal.GetLastWin32Error() when linkat
    /// returns nonzero. That shape must reach the copy fallback.
    /// </summary>
    [Fact]
    public async Task PerformActionOn_HardlinkCopy_CrossDeviceLinkErrorFallsBackToCopy()
    {
        await AssertHardlinkFailureFallsBackAsync(
            "hardlink-fallback-exdev",
            new System.ComponentModel.Win32Exception(
                CrossDeviceLinkErrorNumber,
                "Could not create a hardlink between pinned filesystem endpoints."));
    }

    /// <summary>
    /// CreateHardLinkTo also raises InvalidOperationException, for an endpoint that changed
    /// before link creation and for a created link that does not identify the pinned source
    /// generation. Both are transient races on one filesystem, and both must degrade to a copy
    /// rather than failing the import, exactly as the IOException and Win32Exception shapes do.
    /// </summary>
    [Theory]
    [InlineData("A pinned hardlink endpoint changed before link creation.")]
    [InlineData("The created hardlink does not identify the pinned source generation.")]
    public async Task PerformActionOn_HardlinkCopy_PinnedEndpointRaceFallsBackToCopy(
        string message)
    {
        await AssertHardlinkFailureFallsBackAsync(
            "hardlink-fallback-race",
            new InvalidOperationException(message));
    }

    /// <summary>
    /// The widened fallback is scoped to HardlinkCopy. A Unix move reaching the same
    /// publication step must still fail closed rather than quietly copying and then retiring
    /// the source, so the move contract is unchanged.
    /// </summary>
    [LinuxFact]
    [SupportedOSPlatform("linux")]
    public async Task PrepareActionForRegistration_Move_HardlinkFailureStillFailsClosed()
    {
        var root = FileService.GetTempDirectory(
            $"hardlink-fallback-move-closed-{Guid.NewGuid():N}");
        var source = await FileService.GetFileAsync(root, "source.mp3", "audio content");
        var destination = Path.Join(root, "destination.mp3");
        var mover = CreateMover(() => Task.FromException(
            new IOException("the pinned move endpoint changed before link creation")));

        await Assert.ThrowsAsync<IOException>(() =>
            mover.PrepareActionForRegistrationAsync(
                FileAction.Move,
                source,
                destination,
                Guid.NewGuid()));

        Assert.True(File.Exists(source));
        Assert.Equal("audio content", await File.ReadAllTextAsync(source));
        Assert.False(File.Exists(destination));
    }

    /// <summary>
    /// A plain Copy is unaffected: it never attempts a link and never gains link semantics.
    /// </summary>
    [Fact]
    public async Task PerformActionOn_Copy_StillProducesAnIndependentCopy()
    {
        var root = FileService.GetTempDirectory("hardlink-fallback-plain-copy");
        var source = await FileService.GetFileAsync(root, "source.mp3", "audio content");
        var destination = Path.Join(root, "destination.mp3");
        var operationId = Guid.NewGuid();

        Assert.True(await CreateMover().PerformActionOn(
            FileAction.Copy,
            source,
            destination,
            operationId));

        await AssertIsIndependentCopyAsync(source, destination);
        await AssertCompletedJournalAsync(operationId, FileAction.Copy);
    }

    /// <summary>
    /// A real permission failure must not be masked by the fallback. An unwritable destination
    /// directory cannot be published by a link or by a copy, so the publication has to fail.
    /// </summary>
    [LinuxFact]
    [SupportedOSPlatform("linux")]
    public async Task PerformActionOn_HardlinkCopy_UnwritableDestinationIsNotMaskedByFallback()
    {
        Assert.NotEqual((uint)0, GetEffectiveUserId());

        var root = FileService.GetTempDirectory("hardlink-fallback-permissions");
        var source = await FileService.GetFileAsync(root, "source.mp3", "audio content");
        var destinationRoot = FileService.GetTempDirectory(
            "hardlink-fallback-permissions-destination");
        var destination = Path.Join(destinationRoot, "destination.mp3");
        File.SetUnixFileMode(
            destinationRoot,
            UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            var published = false;
            try
            {
                published = await CreateMover().PerformActionOn(
                    FileAction.HardlinkCopy,
                    source,
                    destination,
                    Guid.NewGuid());
            }
            catch (InvalidOperationException)
            {
                published = false;
            }

            Assert.False(published);
            Assert.False(File.Exists(destination));
            Assert.Equal("audio content", await File.ReadAllTextAsync(source));
        }
        finally
        {
            File.SetUnixFileMode(
                destinationRoot,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private const int CrossDeviceLinkErrorNumber = 18;

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();

    private async Task AssertHardlinkFailureFallsBackAsync(
        string directory,
        Exception hardlinkFailure)
    {
        var root = FileService.GetTempDirectory($"{directory}-{Guid.NewGuid():N}");
        var source = await FileService.GetFileAsync(root, "source.mp3", "audio content");
        var destination = Path.Join(root, "destination.mp3");
        var operationId = Guid.NewGuid();
        var mover = CreateMover(() => Task.FromException(hardlinkFailure));

        Assert.True(await mover.PerformActionOn(
            FileAction.HardlinkCopy,
            source,
            destination,
            operationId));

        Assert.Equal("audio content", await File.ReadAllTextAsync(destination));
        await AssertIsIndependentCopyAsync(source, destination);
        await AssertCompletedJournalAsync(operationId, FileAction.HardlinkCopy);
    }

    /// <summary>
    /// Proves the two paths resolve to one inode by writing through the destination and
    /// observing the source follow. This is stronger than asserting both files exist.
    /// </summary>
    private static async Task AssertIsSameInodeAsync(string source, string destination)
    {
        Assert.True(File.Exists(source));
        Assert.True(File.Exists(destination));
        await File.WriteAllTextAsync(destination, "written through the link");
        Assert.Equal("written through the link", await File.ReadAllTextAsync(source));
    }

    private static async Task AssertIsIndependentCopyAsync(string source, string destination)
    {
        Assert.True(File.Exists(source));
        Assert.True(File.Exists(destination));
        var original = await File.ReadAllTextAsync(source);
        await File.WriteAllTextAsync(destination, "written through the copy");
        Assert.Equal(original, await File.ReadAllTextAsync(source));
    }

    private FileMover CreateMover(Func<Task>? beforeHardlinkCreation = null)
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
        return new FileMover(
            new NullLogger<FileMover>(),
            dbContextFactory: factory,
            timeProvider: TimeProvider.System)
        {
            FileMoveLockDirectoryForTest = FileService.GetTempDirectory(
                $"hardlink-fallback-locks-{Guid.NewGuid():N}"),
            BeforePinnedHardlinkCreationForTestAsync = beforeHardlinkCreation
        };
    }

    private async Task AssertCompletedJournalAsync(Guid operationId, FileAction action)
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var journal = await db.FileMutationJournals
            .AsNoTracking()
            .SingleAsync(candidate => candidate.OperationId == operationId);
        Assert.Equal(action, journal.Action);
        Assert.Equal(FileMutationJournalState.Completed, journal.State);
    }
}
