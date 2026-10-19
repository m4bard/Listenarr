/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.FileSystem;

/// <summary>
/// A source reached through a symlinked directory is refused, and the refusal says so.
///
/// Refusing a linked ancestor is deliberate and already covered by
/// CheckPublicationSource_LinkedAncestor_ReturnsUnsupported. What was missing is any way for an
/// operator to know that is what happened: the underlying failure is an ENOTDIR from openat with
/// O_NOFOLLOW, reported as one fixed sentence about durable physical generations.
/// </summary>
[Trait("Area", "FileSystem")]
[Trait("Name", "FileMoverSymlinkedSourcePathTests")]
[Trait("Category", "PublicationCapability")]
public sealed class FileMoverSymlinkedSourcePathTests : BaseTests
{
    private (string Direct, string ViaSymlink) CreateSymlinkedLayout(string name)
    {
        var root = FileService.GetTempDirectory(name);
        var real = Path.Join(root, "real-downloads", "completed");
        Directory.CreateDirectory(real);
        var file = Path.Join(real, "book.m4b");
        File.WriteAllText(file, "audio");

        // The shape this reproduces: one component of the path is a symlink to the real
        // directory, which is what a cache tier or a pooled mount usually looks like.
        var link = Path.Join(root, "downloads");
        Directory.CreateSymbolicLink(link, Path.Join(root, "real-downloads"));

        return (file, Path.Join(link, "completed", "book.m4b"));
    }

    [DirectoryLinkFact]
    [Trait("Scenario", "The same file is publishable by its real path")]
    public async Task CheckAsync_RealPath_IsSupported()
    {
        // The control. Both paths name the same file on the same filesystem, so anything that
        // fails for one and not the other is about the path, not the file.
        var layout = CreateSymlinkedLayout("symlink-source-control");
        var capability = Assert.IsAssignableFrom<IFilePublicationSourceCapability>(
            _provider.GetRequiredService<IFileMover>());

        var result = await capability.CheckAsync(layout.Direct);

        Assert.True(result.IsSupported, result.Reason);
    }

    [DirectoryLinkFact]
    [Trait("Scenario", "A source reached through a symlinked directory is publishable")]
    public async Task CheckAsync_PathThroughSymlinkedDirectory_IsSupported()
    {
        var layout = CreateSymlinkedLayout("symlink-source-refused");
        Assert.True(File.Exists(layout.ViaSymlink), "the file must be reachable through the link");

        var capability = Assert.IsAssignableFrom<IFilePublicationSourceCapability>(
            _provider.GetRequiredService<IFileMover>());

        var result = await capability.CheckAsync(layout.ViaSymlink);

        Assert.True(
            result.IsSupported,
            $"a source reached through a symlinked directory should be publishable: {result.Reason}");

        // The proof still describes the object, which is the point: resolving the route does not
        // weaken an inode plus content digest.
        Assert.False(string.IsNullOrWhiteSpace(result.PhysicalObjectIdentity));
    }

    [DirectoryLinkFact]
    [Trait("Scenario", "A chain of two links resolves the same as a single one")]
    public async Task CheckAsync_TwoHopSymlinkChain_IsSupported()
    {
        // c/sub is the real directory. b -> c is the single-hop case already covered above.
        // a -> b/sub adds a second hop: the last path segment ("sub") is not itself a link, but
        // an ancestor of it ("b") is. The resolver used to only ever look at the last segment, so
        // this second hop was invisible to it.
        var root = FileService.GetTempDirectory("symlink-source-two-hop");
        var real = Path.Join(root, "c", "sub");
        Directory.CreateDirectory(real);
        var file = Path.Join(real, "book.m4b");
        File.WriteAllText(file, "audio");

        var b = Path.Join(root, "b");
        Directory.CreateSymbolicLink(b, Path.Join(root, "c"));
        var a = Path.Join(root, "a");
        Directory.CreateSymbolicLink(a, Path.Join("b", "sub"));

        var viaTwoHops = Path.Join(a, "book.m4b");
        Assert.True(File.Exists(viaTwoHops), "the file must be reachable through both links");

        var capability = Assert.IsAssignableFrom<IFilePublicationSourceCapability>(
            _provider.GetRequiredService<IFileMover>());

        var result = await capability.CheckAsync(viaTwoHops);

        Assert.True(
            result.IsSupported,
            $"a source reached through a two-hop symlink chain should be publishable: {result.Reason}");
        Assert.False(string.IsNullOrWhiteSpace(result.PhysicalObjectIdentity));
    }

    [DirectoryLinkFact]
    [Trait("Scenario", "A symlink cycle is refused, naming a link in the cycle")]
    public async Task CheckAsync_SymlinkCycle_ReturnsUnsupported_NamingALinkInTheCycle()
    {
        var root = FileService.GetTempDirectory("symlink-source-cycle");
        var x = Path.Join(root, "x");
        var y = Path.Join(root, "y");
        // Neither target need exist for CreateSymbolicLink; the two links only need to name
        // each other.
        Directory.CreateSymbolicLink(x, "y");
        Directory.CreateSymbolicLink(y, "x");

        var capability = Assert.IsAssignableFrom<IFilePublicationSourceCapability>(
            _provider.GetRequiredService<IFileMover>());

        var result = await capability.CheckAsync(Path.Join(x, "book.m4b"));

        Assert.False(result.IsSupported, "a symlink cycle cannot be resolved to a real path");
        var namedLink = ExtractNamedSymlink(result.Reason);
        Assert.True(
            namedLink == x || namedLink == y,
            $"the refusal should name one of the two links forming the cycle, got: {result.Reason}");
    }

    [DirectoryLinkFact]
    [Trait("Scenario", "A dangling second hop is refused, naming the dangling link and not the first one")]
    public async Task CheckAsync_DanglingSecondHop_ReturnsUnsupported_NamingTheDanglingLink()
    {
        var root = FileService.GetTempDirectory("symlink-source-dangling-second-hop");
        var a = Path.Join(root, "a");
        var b = Path.Join(root, "b");
        // a -> b resolves fine on its own; b -> a target that never exists does not. The first
        // hop is sound, the second is what breaks, so the refusal must name b, not a.
        Directory.CreateSymbolicLink(a, "b");
        Directory.CreateSymbolicLink(b, Path.Join(root, "does-not-exist"));

        var capability = Assert.IsAssignableFrom<IFilePublicationSourceCapability>(
            _provider.GetRequiredService<IFileMover>());

        var result = await capability.CheckAsync(Path.Join(a, "book.m4b"));

        Assert.False(result.IsSupported, "a dangling second hop cannot be resolved to a real path");
        Assert.Equal(b, ExtractNamedSymlink(result.Reason));
    }

    /// <summary>
    /// Pulls the path FileMover.ComposeUnsupportedReason quotes between "symbolic link at '" and
    /// the closing quote, so a test can assert on exactly the link the refusal blames rather than
    /// on the whole sentence, which also carries the raw OS exception text and can legitimately
    /// mention other paths in the chain.
    /// </summary>
    private static string ExtractNamedSymlink(string? reason)
    {
        Assert.NotNull(reason);
        const string marker = "symbolic link at '";
        var markerIndex = reason.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"expected the reason to name the blocking link: {reason}");
        var start = markerIndex + marker.Length;
        var end = reason.IndexOf('\'', start);
        Assert.True(end > start, $"expected a closing quote around the named link: {reason}");
        return reason[start..end];
    }
}
