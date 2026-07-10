using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Library.Moving;

[Trait("Name", "MoveCleanupBoundaryResolverTests")]
[Trait("Category", "Library")]
public sealed class MoveCleanupBoundaryResolverTests : BaseTests
{
    [Fact]
    public async Task ResolveAsync_ConfiguredSourceRoot_TakesPrecedenceOverCommonAncestor()
    {
        var root = FileService.GetTempDirectory("move-boundary-configured-root");
        var series = Path.Join(root, "Author", "Series");
        var source = Path.Join(series, "Old Title", "test");
        var target = Path.Join(series, "New Title", "test");
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(
            source,
            target,
            [new RootFolder { Name = "Library", Path = root }]);

        Assert.True(result.IsAvailable);
        Assert.Equal(MoveCleanupBoundaryKind.ConfiguredRoot, result.Kind);
        Assert.Equal(Path.GetFullPath(root), result.Boundary);
    }

    [Fact]
    public async Task ResolveAsync_CustomSiblingMove_UsesCommonSeriesAncestor()
    {
        var customRoot = FileService.GetTempDirectory("move-boundary-custom-root");
        var series = Path.Join(customRoot, "Matt Dinniman", "Dungeon Crawler Carl");
        var source = Path.Join(series, "A Parade of Horribles (20262)", "test");
        var target = Path.Join(series, "A Parade of Horribles (2026)", "test");
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(source, target, []);

        Assert.True(result.IsAvailable);
        Assert.Equal(MoveCleanupBoundaryKind.CommonAncestor, result.Kind);
        Assert.Equal(Path.GetFullPath(series), result.Boundary);
    }

    [Fact]
    public async Task ResolveAsync_PersistedBoundaryOutsideSource_IsUnavailable()
    {
        var sourceRoot = FileService.GetTempDirectory("move-boundary-source");
        var targetRoot = FileService.GetTempDirectory("move-boundary-target");
        var source = Path.Join(sourceRoot, "Author", "Title", "test");
        var target = Path.Join(targetRoot, "Author", "Title", "test");
        var unrelatedBoundary = FileService.GetTempDirectory("move-boundary-unrelated");
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(
            source,
            target,
            [],
            unrelatedBoundary);

        Assert.False(result.IsAvailable);
        Assert.Equal(MoveCleanupBoundaryKind.Unavailable, result.Kind);
        Assert.Contains("no longer contains", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveAsync_CrossRootWindowsMove_UsesSourceVolumeAnchor()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var driveRoot = Path.GetPathRoot(FileService.GetTempPath())!;
        var sourceAnchor = Path.Join(driveRoot, "Listenarr Downloads");
        var source = Path.Join(
            sourceAnchor,
            "Matt Dinniman",
            "Dungeon Crawler Carl",
            "A Parade of Horribles (20262)",
            "test");
        var target = Path.Join(
            driveRoot,
            "Listenarr Test",
            "Matt Dinniman",
            "Dungeon Crawler Carl",
            "A Parade of Horribles (2026)",
            "test");
        var resolver = CreateResolver(new FileSystemPathSemantics(
            FileSystemPathSyntax.Windows,
            FileSystemCaseSensitivity.Insensitive));

        var result = await resolver.ResolveAsync(source, target, []);

        Assert.True(result.IsAvailable);
        Assert.Equal(MoveCleanupBoundaryKind.VolumeAnchor, result.Kind);
        Assert.Equal(sourceAnchor, result.Boundary);
    }

    [Fact]
    public async Task ResolveAsync_UnixConfiguredSourceRoot_PreservesRootAcrossCrossRootMove()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var sourceRoot = Path.Join(
            Path.GetTempPath(),
            $"listenarr-unix-source-{Guid.NewGuid():N}");
        var targetRoot = Path.Join(
            Path.GetTempPath(),
            $"listenarr-unix-target-{Guid.NewGuid():N}");
        var source = Path.Join(sourceRoot, "Author", "Series", "Old Title", "test");
        var target = Path.Join(targetRoot, "Author", "Series", "New Title", "test");
        var resolver = CreateResolver(new FileSystemPathSemantics(
            FileSystemPathSyntax.Unix,
            FileSystemCaseSensitivity.Sensitive));

        var result = await resolver.ResolveAsync(
            source,
            target,
            [new RootFolder { Name = "Unix Source", Path = sourceRoot }]);

        Assert.True(result.IsAvailable);
        Assert.Equal(MoveCleanupBoundaryKind.ConfiguredRoot, result.Kind);
        Assert.Equal(Path.GetFullPath(sourceRoot), result.Boundary);
    }

    [Fact]
    public async Task ResolveAsync_UnixCustomSiblingMove_UsesCommonSeriesAncestor()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var series = Path.Join(
            Path.GetTempPath(),
            $"listenarr-unix-sibling-{Guid.NewGuid():N}",
            "Matt Dinniman",
            "Dungeon Crawler Carl");
        var source = Path.Join(series, "A Parade of Horribles (20262)", "test");
        var target = Path.Join(series, "A Parade of Horribles (2026)", "test");
        var resolver = CreateResolver(new FileSystemPathSemantics(
            FileSystemPathSyntax.Unix,
            FileSystemCaseSensitivity.Sensitive));

        var result = await resolver.ResolveAsync(source, target, []);

        Assert.True(result.IsAvailable);
        Assert.Equal(MoveCleanupBoundaryKind.CommonAncestor, result.Kind);
        Assert.Equal(Path.GetFullPath(series), result.Boundary);
    }

    [Fact]
    public async Task ResolveAsync_UnixCrossRootWithoutConfiguredRoot_DoesNotUseFilesystemRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var source = "/listenarr-downloads/Author/Series/Old Title/test";
        var target = "/listenarr-library/Author/Series/New Title/test";
        var resolver = CreateResolver(new FileSystemPathSemantics(
            FileSystemPathSyntax.Unix,
            FileSystemCaseSensitivity.Sensitive));

        var result = await resolver.ResolveAsync(source, target, []);

        Assert.False(result.IsAvailable);
        Assert.Equal(MoveCleanupBoundaryKind.Unavailable, result.Kind);
        Assert.Contains("No configured source root", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_UncConfiguredSourceRoot_TakesPrecedence()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string sourceRoot = @"\\server\downloads\Listenarr Downloads";
        const string source = @"\\server\downloads\Listenarr Downloads\Author\Series\Old Title\test";
        const string target = @"\\server\library\Audiobooks\Author\Series\New Title\test";
        var resolver = CreateResolver(new FileSystemPathSemantics(
            FileSystemPathSyntax.Windows,
            FileSystemCaseSensitivity.Insensitive));

        var result = await resolver.ResolveAsync(
            source,
            target,
            [new RootFolder { Name = "UNC Source", Path = sourceRoot }]);

        Assert.True(result.IsAvailable);
        Assert.Equal(MoveCleanupBoundaryKind.ConfiguredRoot, result.Kind);
        Assert.Equal(sourceRoot, result.Boundary);
    }

    [Fact]
    public async Task ResolveAsync_UncSameShareSiblingMove_UsesCommonSeriesAncestor()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string series = @"\\server\share\Listenarr Downloads\Matt Dinniman\Dungeon Crawler Carl";
        const string source = @"\\server\share\Listenarr Downloads\Matt Dinniman\Dungeon Crawler Carl\A Parade of Horribles (20262)\test";
        const string target = @"\\server\share\Listenarr Downloads\Matt Dinniman\Dungeon Crawler Carl\A Parade of Horribles (2026)\test";
        var resolver = CreateResolver(new FileSystemPathSemantics(
            FileSystemPathSyntax.Windows,
            FileSystemCaseSensitivity.Insensitive));

        var result = await resolver.ResolveAsync(source, target, []);

        Assert.True(result.IsAvailable);
        Assert.Equal(MoveCleanupBoundaryKind.CommonAncestor, result.Kind);
        Assert.Equal(series, result.Boundary);
    }

    [Fact]
    public async Task ResolveAsync_UncCrossShareMove_UsesTopLevelSourceAnchor()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string sourceAnchor = @"\\server\downloads\Listenarr Downloads";
        const string source = @"\\server\downloads\Listenarr Downloads\Author\Series\Old Title\test";
        const string target = @"\\server\library\Audiobooks\Author\Series\New Title\test";
        var resolver = CreateResolver(new FileSystemPathSemantics(
            FileSystemPathSyntax.Windows,
            FileSystemCaseSensitivity.Insensitive));

        var result = await resolver.ResolveAsync(source, target, []);

        Assert.True(result.IsAvailable);
        Assert.Equal(MoveCleanupBoundaryKind.VolumeAnchor, result.Kind);
        Assert.Equal(sourceAnchor, result.Boundary);
        Assert.NotEqual(@"\\server\downloads", result.Boundary);
    }

    private static MoveCleanupBoundaryResolver CreateResolver(
        FileSystemPathSemantics? semantics = null)
    {
        var resolvedSemantics = semantics ?? FileSystemPathSemantics.CurrentHostDefault;
        var semanticsResolver = new Mock<IFileSystemSemanticsResolver>();
        semanticsResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<FileSystemCaseSensitivityMode>(),
                It.IsAny<CancellationToken>()))
            .Returns((string path, FileSystemCaseSensitivityMode _, CancellationToken _) =>
                ValueTask.FromResult(new FileSystemSemanticsResolution(
                    resolvedSemantics,
                    PathIdentityState.Valid,
                    Path.GetPathRoot(path) ?? path)));
        return new MoveCleanupBoundaryResolver(semanticsResolver.Object);
    }
}
