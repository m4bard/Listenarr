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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.FileSystem;

/// <summary>
/// A source reached through a symlinked directory is refused, and the refusal says so.
///
/// Refusing a linked ancestor is deliberate and already covered by
/// CheckPublicationSource_LinkedAncestor_ReturnsUnsupported. What was missing is any way for an
/// operator to know that is what happened: the underlying failure is an ENOTDIR from openat with
/// O_NOFOLLOW, reported as one fixed sentence that names no cause.
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
        // weaken it.
        AssertProofDescribesFile(result, "audio");
    }

    /// <summary>
    /// A configured root that is itself reached through a symlink, with a source file beneath it.
    /// </summary>
    /// <remarks>
    /// The mover is built with the root folder repository so that CheckAsync takes the managed
    /// path, where the configured root is opened through the boundary authorizer and its link is
    /// followed. Optionally adds a nested root at the book directory whose identity is unavailable,
    /// which refuses the source for a reason that has nothing to do with the link.
    /// </remarks>
    private async Task<(FileMover Mover, string Source)> CreateLinkedManagedRootAsync(
        string name,
        bool addUnavailableNestedRoot)
    {
        var directory = FileService.GetTempDirectory(name);
        var physicalRoot = Directory.CreateDirectory(Path.Join(directory, "physical-library")).FullName;
        var linkedRoot = Path.Join(directory, "library");
        Directory.CreateSymbolicLink(linkedRoot, physicalRoot);
        await AddAuthorizedRootAsync(linkedRoot, "Linked library root");

        var bookDirectory = Directory.CreateDirectory(Path.Join(linkedRoot, "Book")).FullName;
        var source = Path.Join(bookDirectory, "book.m4b");
        await File.WriteAllTextAsync(source, "audio");

        if (addUnavailableNestedRoot)
        {
            await AddUnavailableRootAsync(bookDirectory, "Unavailable nested root");
        }

        return (CreateManagedMover(), source);
    }

    private FileMover CreateManagedMover()
    {
        return new FileMover(
            NullLogger<FileMover>.Instance,
            dbContextFactory: _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>(),
            rootFolderRepository: _rootFolderRepository);
    }

    private async Task AddUnavailableRootAsync(string path, string name)
    {
        var dbContextFactory = _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.RootFolders.Add(new RootFolder
        {
            Name = name,
            Path = path,
            CaseSensitivityMode = FileSystemCaseSensitivityMode.Auto,
            PathIdentityState = PathIdentityState.Unavailable,
            ResolvedCaseSensitivity = FileSystemCaseSensitivity.Unknown
        });
        await db.SaveChangesAsync();
    }

    [DirectoryLinkFact]
    [Trait("Scenario", "A configured root reached through a symlink is publishable")]
    public async Task CheckAsync_ManagedRootReachedThroughSymlink_IsSupported()
    {
        // The control. On the managed path the configured root is opened by following its link,
        // so the link alone is not a reason to refuse.
        var (mover, source) = await CreateLinkedManagedRootAsync(
            "symlink-managed-root-control",
            addUnavailableNestedRoot: false);

        var result = await mover.CheckAsync(source);

        Assert.True(result.IsSupported, result.Reason);
    }

    [DirectoryLinkFact]
    [Trait("Scenario", "An unrelated refusal beneath a linked configured root does not blame the link")]
    public async Task CheckAsync_ManagedRootReachedThroughSymlink_UnrelatedFailure_DoesNotBlameTheLink()
    {
        var (mover, source) = await CreateLinkedManagedRootAsync(
            "symlink-managed-root-unrelated-failure",
            addUnavailableNestedRoot: true);

        var result = await mover.CheckAsync(source);

        Assert.False(result.IsSupported);
        Assert.StartsWith("InvalidOperationException: ", result.Reason, StringComparison.Ordinal);

        // The link above is supported, as the control shows, so advice to configure the real
        // path instead would send the operator after the wrong thing.
        Assert.DoesNotContain("symbolic link", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("configure the real path", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [DirectoryLinkFact]
    [Trait("Scenario", "A refusal that is not the overlap, beneath a linked configured root, does not blame the link")]
    public async Task CheckAsync_ManagedRootReachedThroughSymlink_RootCannotBeAuthorized_DoesNotBlameTheLink()
    {
        // The unrelated failure above is the overlap, which is refused before the walk would look
        // at a single directory. Here the root resolves and is then refused because nothing can
        // authorize it, so only stopping the walk at the root keeps the root's link out of it.
        var (_, source) = await CreateLinkedManagedRootAsync(
            "symlink-managed-root-unauthorized",
            addUnavailableNestedRoot: false);
        var moverWithoutAuthorizer = new FileMover(
            NullLogger<FileMover>.Instance,
            rootFolderRepository: _rootFolderRepository);

        var result = await moverWithoutAuthorizer.CheckAsync(source);

        Assert.False(result.IsSupported);
        Assert.StartsWith("InvalidOperationException: ", result.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("unresolved managed boundary", result.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("symbolic link", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("configure the real path", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [DirectoryLinkFact]
    [Trait("Scenario", "A source covered only by an unavailable root reached through a symlink does not blame the link")]
    public async Task CheckAsync_OnlyAnUnavailableRootCoversALinkedSource_DoesNotBlameTheLink()
    {
        // With no root it can resolve, the walk would otherwise cover the whole path and find the
        // root's link. The overlap is refused before any directory is opened, so the link cannot
        // be what caused it.
        var directory = FileService.GetTempDirectory("symlink-unavailable-root-only");
        var physicalRoot = Directory.CreateDirectory(Path.Join(directory, "physical-library")).FullName;
        var linkedRoot = Path.Join(directory, "library");
        Directory.CreateSymbolicLink(linkedRoot, physicalRoot);
        await AddUnavailableRootAsync(linkedRoot, "Unavailable linked root");
        var bookDirectory = Directory.CreateDirectory(Path.Join(linkedRoot, "Book")).FullName;
        var source = Path.Join(bookDirectory, "book.m4b");
        await File.WriteAllTextAsync(source, "audio");

        var result = await CreateManagedMover().CheckAsync(source);

        Assert.False(result.IsSupported);
        Assert.StartsWith("InvalidOperationException: ", result.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("symbolic link", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("configure the real path", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [DirectoryLinkFact]
    [Trait("Scenario", "A link below a configured root is still named")]
    public async Task CheckAsync_LinkBelowAConfiguredRoot_NamesTheLink()
    {
        // Stopping the walk at the root must not silence it below the root, where segments are
        // still opened without following links and a link is still the cause.
        var directory = FileService.GetTempDirectory("symlink-below-configured-root");
        var root = Directory.CreateDirectory(Path.Join(directory, "library")).FullName;
        await AddAuthorizedRootAsync(root, "Library root");
        var elsewhere = Directory.CreateDirectory(Path.Join(directory, "elsewhere")).FullName;
        var link = Path.Join(root, "LinkedSub");
        Directory.CreateSymbolicLink(link, elsewhere);
        var bookDirectory = Directory.CreateDirectory(Path.Join(link, "Book")).FullName;
        var source = Path.Join(bookDirectory, "book.m4b");
        await File.WriteAllTextAsync(source, "audio");

        var result = await CreateManagedMover().CheckAsync(source);

        Assert.False(result.IsSupported);
        Assert.Matches(@"^\w+Exception: \S", result.Reason!);
        Assert.Contains("symbolic link", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LinkedSub", result.Reason, StringComparison.Ordinal);
    }

    [DirectoryLinkFact]
    [Trait("Scenario", "Nested links that all sit outside every configured root resolve and are publishable")]
    public async Task CheckAsync_NestedSymlinksOutsideEveryRoot_IsSupported()
    {
        // The stack follows a chain of links when none of them sits inside a configured root, so
        // the nested chain that upstream refuses (and names the outer link of) is publishable here.
        // A configured root elsewhere is present so that the managed path is the one under test.
        var directory = FileService.GetTempDirectory("symlink-nested-links");
        var inner = Directory.CreateDirectory(Path.Join(directory, "inner-target", "completed")).FullName;
        var source = Path.Join(inner, "book.m4b");
        await File.WriteAllTextAsync(source, "audio");
        var innerLink = Path.Join(directory, "inner-link");
        Directory.CreateSymbolicLink(innerLink, Path.Join(directory, "inner-target"));
        var outerLink = Path.Join(directory, "outer-link");
        Directory.CreateSymbolicLink(outerLink, directory);
        var viaBoth = Path.Join(outerLink, "inner-link", "completed", "book.m4b");
        Assert.True(File.Exists(viaBoth), "the file must be reachable through both links");
        var unrelatedRoot = Directory.CreateDirectory(Path.Join(directory, "unrelated-library")).FullName;
        await AddAuthorizedRootAsync(unrelatedRoot, "Unrelated library root");

        var result = await CreateManagedMover().CheckAsync(viaBoth);

        Assert.True(
            result.IsSupported,
            $"a source reached through nested links outside every root should be publishable: {result.Reason}");
        AssertProofDescribesFile(result, "audio");
    }

    [DirectoryLinkFact]
    [Trait("Scenario", "With nested links below a configured root the outer one, where the walk stops, is named")]
    public async Task CheckAsync_NestedSymlinksBelowAConfiguredRoot_NamesTheOuterLink()
    {
        // Below a configured root every segment is opened without following links, so the
        // first link the walk meets is the cause and the one behind it is never reached.
        var directory = FileService.GetTempDirectory("symlink-nested-links-below-root");
        var root = Directory.CreateDirectory(Path.Join(directory, "library")).FullName;
        await AddAuthorizedRootAsync(root, "Library root");
        var inner = Directory.CreateDirectory(Path.Join(directory, "inner-target", "completed")).FullName;
        await File.WriteAllTextAsync(Path.Join(inner, "book.m4b"), "audio");
        var middle = Directory.CreateDirectory(Path.Join(directory, "middle")).FullName;
        Directory.CreateSymbolicLink(Path.Join(middle, "inner-link"), Path.Join(directory, "inner-target"));
        var outerLink = Path.Join(root, "outer-link");
        Directory.CreateSymbolicLink(outerLink, middle);
        var viaBoth = Path.Join(outerLink, "inner-link", "completed", "book.m4b");
        Assert.True(File.Exists(viaBoth), "the file must be reachable through both links");

        var result = await CreateManagedMover().CheckAsync(viaBoth);

        Assert.False(result.IsSupported);
        Assert.Contains("'" + outerLink + "'", result.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.Join(outerLink, "inner-link"), result.Reason, StringComparison.Ordinal);
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
        AssertProofDescribesFile(result, "audio");
    }

    // Source capability exports content evidence only: the pinned entry's kernel identity is
    // deliberately kept out of the proof, so OperationLocalPhysicalObjectIdentity is absent for
    // every source. "The proof describes the object" is therefore checked on the content: the
    // length and digest of the file the links lead to.
    private static void AssertProofDescribesFile(
        FilePublicationSourceCapabilityResult result,
        string content)
    {
        var proof = Assert.NotNull(result.SourceProof);
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        Assert.Equal(bytes.LongLength, proof.Length);
        Assert.Equal(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
            proof.Sha256,
            ignoreCase: true);
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
