using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Downloads.Import;

// No DB or DI is needed here, only BaseTests to satisfy the repository's test-class
// convention (BackendArchitectureTests.TestClasses_FollowRepositoryConventions).
[Trait("Name", "FreeSpaceImportGuardTests")]
[Trait("Category", "DownloadProcessingJob")]
public sealed class FreeSpaceImportGuardTests : BaseTests
{
    // Free space is read on the parent of the destination folder, matching Readarr's
    // FreeSpaceSpecification (Directory.GetParent(item.Author.Path)), because the
    // audiobook's own folder may not exist yet at import time.
    private const string Destination = "/library/Author/Book";
    private static readonly string ProbedParent = Path.GetDirectoryName(
        Destination.TrimEnd('/'))!;

    private static (FreeSpaceImportGuard Guard, Mock<IDiskSpaceProbe> Probe, Mock<IFileSystem> FileSystem)
        CreateGuard(bool immediateParentExists = true)
    {
        var probe = new Mock<IDiskSpaceProbe>();
        var fileSystem = new Mock<IFileSystem>();
        // By default the immediate parent already exists, matching every test below except
        // the ones in the "ancestor walk" group, which override this explicitly. Anything
        // further up the tree defaults to existing too, so a lookup never runs off the end
        // of the mock in a test that never expects to reach it.
        fileSystem.Setup(fs => fs.DirectoryExists(It.IsAny<string>())).Returns(true);
        if (!immediateParentExists)
        {
            fileSystem.Setup(fs => fs.DirectoryExists(ProbedParent)).Returns(false);
        }

        var guard = new FreeSpaceImportGuard(
            probe.Object,
            fileSystem.Object,
            NullLogger<FreeSpaceImportGuard>.Instance);
        return (guard, probe, fileSystem);
    }

    [Fact]
    public void Evaluate_FreeSpaceBelowRequiredPlusMargin_Rejects()
    {
        var (guard, probe, _) = CreateGuard();
        long total = 0;
        long free = 150 * 1024L * 1024L; // 150 MB free
        probe.Setup(p => p.TryGetDiskSpace(ProbedParent, out total, out free)).Returns(true);
        var settings = new ApplicationSettingsBuilder()
            .WithMinimumFreeSpaceWhenImporting(100)
            .Build();

        // Requires 100MB of file plus a 100MB margin = 200MB, but only 150MB is free.
        var result = guard.Evaluate(Destination, 100 * 1024L * 1024L, settings);

        Assert.False(result.IsAllowed);
        Assert.Equal(free, result.FreeBytes);
    }

    [Fact]
    public void Evaluate_FreeSpaceAboveRequiredPlusMargin_Allows()
    {
        // Control: identical setup to the rejection case above except the disk reports more
        // free space, so the same required-bytes-plus-margin arithmetic comes out allowed.
        // If the guard's comparison were inert (e.g. comparing the wrong operands, or always
        // returning true), this case alone would not catch it, but paired with the rejection
        // test above the two must disagree for the guard to be doing anything at all.
        var (guard, probe, _) = CreateGuard();
        long total = 0;
        long free = 500 * 1024L * 1024L; // 500 MB free
        probe.Setup(p => p.TryGetDiskSpace(ProbedParent, out total, out free)).Returns(true);
        var settings = new ApplicationSettingsBuilder()
            .WithMinimumFreeSpaceWhenImporting(100)
            .Build();

        var result = guard.Evaluate(Destination, 100 * 1024L * 1024L, settings);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void Evaluate_SkipFreeSpaceCheckWhenImporting_AllowsWithoutProbing()
    {
        var (guard, probe, _) = CreateGuard();
        var settings = new ApplicationSettingsBuilder()
            .WithSkipFreeSpaceCheckWhenImporting()
            .WithMinimumFreeSpaceWhenImporting(100)
            .Build();

        // An impossibly large requirement would fail if the probe were consulted at all.
        var result = guard.Evaluate(Destination, long.MaxValue / 2, settings);

        Assert.True(result.IsAllowed);
        probe.Verify(
            p => p.TryGetDiskSpace(It.IsAny<string>(), out It.Ref<long>.IsAny, out It.Ref<long>.IsAny),
            Times.Never);
    }

    [Fact]
    public void Evaluate_ProbeCannotMeasure_FailsOpen()
    {
        // Mirrors Readarr's own escape hatch reasoning: a network filesystem can report free
        // space Listenarr has no way to trust, so an unmeasurable probe must not block every
        // import.
        var (guard, probe, _) = CreateGuard();
        long total = 0;
        long free = 0;
        probe.Setup(p => p.TryGetDiskSpace(ProbedParent, out total, out free)).Returns(false);
        var settings = new ApplicationSettingsBuilder()
            .WithMinimumFreeSpaceWhenImporting(100)
            .Build();

        var result = guard.Evaluate(Destination, 100 * 1024L * 1024L, settings);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void Evaluate_ZeroRequiredBytesButNotEnoughMargin_StillRejects()
    {
        // Readarr applies the margin regardless of item size; it never special-cases a zero
        // or unreadable item size. A guard that skipped the whole check whenever the required
        // byte count is zero would let an import through even when free space is below the
        // configured minimum, so this is a real behavior, not a smoke test: it would fail
        // outright if that shortcut existed.
        var (guard, probe, _) = CreateGuard();
        long total = 0;
        long free = 50 * 1024L * 1024L; // below the 100MB margin on its own
        probe.Setup(p => p.TryGetDiskSpace(ProbedParent, out total, out free)).Returns(true);
        var settings = new ApplicationSettingsBuilder()
            .WithMinimumFreeSpaceWhenImporting(100)
            .Build();

        var result = guard.Evaluate(Destination, 0, settings);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void Evaluate_ZeroRequiredBytesWithEnoughMargin_Allows()
    {
        // Control for the case above: same zero-byte requirement, only the reported free
        // space differs, and the two must disagree.
        var (guard, probe, _) = CreateGuard();
        long total = 0;
        long free = 500 * 1024L * 1024L;
        probe.Setup(p => p.TryGetDiskSpace(ProbedParent, out total, out free)).Returns(true);
        var settings = new ApplicationSettingsBuilder()
            .WithMinimumFreeSpaceWhenImporting(100)
            .Build();

        var result = guard.Evaluate(Destination, 0, settings);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void Evaluate_NegativeStoredMargin_DoesNotShrinkBelowRawFileSize()
    {
        // Not reachable through the shipped UI (it clamps to zero), but reachable through a
        // direct API write. A negative margin must not subtract from the required bytes.
        var (guard, probe, _) = CreateGuard();
        long total = 0;
        long free = 90 * 1024L * 1024L; // less than the 100MB file itself
        probe.Setup(p => p.TryGetDiskSpace(ProbedParent, out total, out free)).Returns(true);
        var settings = new ApplicationSettingsBuilder()
            .WithMinimumFreeSpaceWhenImporting(-500)
            .Build();

        var result = guard.Evaluate(Destination, 100 * 1024L * 1024L, settings);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void Evaluate_ImmediateParentMissing_WalksUpToNearestExistingAncestor()
    {
        // Reproduces the default FolderNamingPattern shape ({Author}/{Series}/{Title}):
        // for a brand new author or series, none of the intermediate folders exist yet, so
        // probing the immediate parent alone (DiskSpaceProbe.TryGetDiskSpace requires
        // Directory.Exists) would always report "cannot measure" and silently never fire.
        var (guard, probe, fileSystem) = CreateGuard(immediateParentExists: false);
        var rootPath = Path.GetDirectoryName(ProbedParent)!;
        fileSystem.Setup(fs => fs.DirectoryExists(rootPath)).Returns(true);
        long total = 0;
        long free = 10 * 1024L * 1024L; // well below the 100MB requirement plus margin
        probe.Setup(p => p.TryGetDiskSpace(rootPath, out total, out free)).Returns(true);
        var settings = new ApplicationSettingsBuilder()
            .WithMinimumFreeSpaceWhenImporting(100)
            .Build();

        var result = guard.Evaluate(Destination, 100 * 1024L * 1024L, settings);

        Assert.False(result.IsAllowed);
        probe.Verify(
            p => p.TryGetDiskSpace(ProbedParent, out It.Ref<long>.IsAny, out It.Ref<long>.IsAny),
            Times.Never);
        probe.Verify(
            p => p.TryGetDiskSpace(rootPath, out It.Ref<long>.IsAny, out It.Ref<long>.IsAny),
            Times.Once);
    }
}
