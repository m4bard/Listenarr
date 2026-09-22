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

    private static (FreeSpaceImportGuard Guard, Mock<IDiskSpaceProbe> Probe) CreateGuard()
    {
        var probe = new Mock<IDiskSpaceProbe>();
        var guard = new FreeSpaceImportGuard(probe.Object, NullLogger<FreeSpaceImportGuard>.Instance);
        return (guard, probe);
    }

    [Fact]
    public void Evaluate_FreeSpaceBelowRequiredPlusMargin_Rejects()
    {
        var (guard, probe) = CreateGuard();
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
        var (guard, probe) = CreateGuard();
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
        var (guard, probe) = CreateGuard();
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
        var (guard, probe) = CreateGuard();
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
    public void Evaluate_ZeroRequiredBytes_AllowsWithoutProbing()
    {
        var (guard, probe) = CreateGuard();
        var settings = new ApplicationSettingsBuilder()
            .WithMinimumFreeSpaceWhenImporting(100)
            .Build();

        var result = guard.Evaluate(Destination, 0, settings);

        Assert.True(result.IsAllowed);
        probe.Verify(
            p => p.TryGetDiskSpace(It.IsAny<string>(), out It.Ref<long>.IsAny, out It.Ref<long>.IsAny),
            Times.Never);
    }
}
