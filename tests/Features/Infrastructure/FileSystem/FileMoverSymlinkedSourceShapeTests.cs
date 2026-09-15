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
using System.ComponentModel;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.FileSystem;

/// <summary>
/// The symlink shapes the existing coverage does not reach.
///
/// FileMoverSymlinkedSourcePathTests covers one shape only: a symlinked directory, on the
/// capability probe, with no move. That leaves the shape the ELOOP reports are actually about
/// untested, because "Could not open pinned file" comes from the leaf open and a symlinked
/// directory never reaches it. These tests fill the rest of the matrix: leaf link and ancestor
/// link, each on the probe path and on the move path, plus the pairing the automatic import uses
/// (probe, then registration carrying the probe's proof).
///
/// These are harness tests. Several of them pin behaviour that looks wrong, and say so in a
/// comment rather than quietly blessing it. Nothing here changes production code.
///
/// Everything asserted below was measured on Linux. The native failure text and the errno values
/// are Unix specific, so the whole class is Linux gated; the Windows cells of the matrix are
/// genuinely untested rather than assumed.
/// </summary>
[Trait("Area", "FileSystem")]
[Trait("Name", "FileMoverSymlinkedSourceShapeTests")]
[Trait("Category", "PublicationCapability")]
public sealed class FileMoverSymlinkedSourceShapeTests : BaseTests
{
    // The two failure sentences whose separation is the whole finding, plus the third one the
    // hierarchy walk actually produces.
    private const string LeafOpenFailure = "Could not open pinned file";
    private const string DirectoryOpenFailure = "Could not open directory";
    private const string HierarchyWalkFailure = "Could not open a newly created pinned directory";

    private const int Eloop = 40;
    private const int ENotDir = 20;

    private sealed record Layout(string Root, string RealDirectory, string Library);

    private Layout CreateLayout(string name)
    {
        var root = FileService.GetTempDirectory(name);
        var realDirectory = Path.Join(root, "real-downloads", "completed");
        Directory.CreateDirectory(realDirectory);
        return new Layout(root, realDirectory, FileService.GetTempDirectory($"{name}-library"));
    }

    private static IFilePublicationSourceCapability AsCapability(IFileMover mover) =>
        Assert.IsAssignableFrom<IFilePublicationSourceCapability>(mover);

    private static string SeedFile(string directory, string name, string content = "audio")
    {
        var path = Path.Join(directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static Win32Exception CaptureWin32(Action action)
    {
        var exception = Record.Exception(action);
        Assert.NotNull(exception);
        return Assert.IsType<Win32Exception>(exception);
    }

    [LinuxDirectoryAndFileLinkFact]
    [Trait("Scenario", "A source that is itself a symbolic link is refused by the probe")]
    public async Task CheckAsync_SymlinkedLeafFile_IsRefusedByTheLeafOpen()
    {
        var layout = CreateLayout("symlink-shape-leaf-probe");
        var target = SeedFile(layout.RealDirectory, "book.m4b");
        var linkDirectory = Path.Join(layout.Root, "incoming");
        Directory.CreateDirectory(linkDirectory);
        var source = Path.Join(linkDirectory, "book.m4b");
        File.CreateSymbolicLink(source, target);

        var result = await AsCapability(_provider.GetRequiredService<IFileMover>())
            .CheckAsync(source);

        Assert.False(result.IsSupported);
        Assert.Equal(
            FilePublicationSourceCapabilityFailureKind.Unavailable,
            result.FailureKind);

        // This is the sentence the ELOOP reports quote, and it comes from OpenRelativeFileUnix,
        // the leaf open. A symlinked directory cannot produce it, which is why the existing
        // symlinked-directory test does not cover this at all.
        Assert.Contains(LeafOpenFailure, result.Reason, StringComparison.Ordinal);

        // PINS BEHAVIOUR THAT IS WRONG. The reason says nothing about a symbolic link even though
        // the source is one. FindSymlinkedAncestor walks directories only, so the diagnostic added
        // for linked ancestors never fires for a linked leaf, and the operator is handed a native
        // sentence with no cause in it. Correct behaviour would name the leaf as a symbolic link
        // and name its target, the way a linked ancestor is named.
        Assert.DoesNotContain("symbolic link", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [LinuxDirectoryAndFileLinkFact]
    [Trait("Scenario", "A source that is itself a symbolic link is refused by every move action")]
    public async Task PerformActionOn_SymlinkedLeafFile_RefusesEveryAction()
    {
        var layout = CreateLayout("symlink-shape-leaf-move");
        var target = SeedFile(layout.RealDirectory, "book.m4b");
        var linkDirectory = Path.Join(layout.Root, "incoming");
        Directory.CreateDirectory(linkDirectory);
        var source = Path.Join(linkDirectory, "book.m4b");
        File.CreateSymbolicLink(source, target);
        var mover = _provider.GetRequiredService<IFileMover>();

        // The control. A refusal proves nothing on its own, because a mistyped path, an unwired
        // mover or a missing file all return false as well. The same three actions have to be
        // shown working against the real file first, so that false means the link and nothing
        // else.
        foreach (var action in new[]
        {
            FileAction.Copy,
            FileAction.Move,
            FileAction.HardlinkCopy
        })
        {
            var controlSource = SeedFile(layout.RealDirectory, $"control-{action}.m4b");
            var controlDestination = action == FileAction.HardlinkCopy
                ? Path.Join(layout.RealDirectory, $"control-{action}-out.m4b")
                : Path.Join(layout.Library, $"control-{action}.m4b");

            Assert.True(
                await mover.PerformActionOn(
                    action,
                    controlSource,
                    controlDestination,
                    Guid.NewGuid()),
                $"{action} must work against the real path for the refusal below to mean anything");
            Assert.True(File.Exists(controlDestination));
        }

        foreach (var action in new[]
        {
            FileAction.Copy,
            FileAction.Move,
            FileAction.HardlinkCopy
        })
        {
            var destination = Path.Join(layout.Library, $"{action}.m4b");

            var performed = await mover.PerformActionOn(
                action,
                source,
                destination,
                Guid.NewGuid());

            // The move path refuses a linked leaf in its own way, at IsLinkedOrUnverifiableEntry,
            // long before any pinned open. It returns false rather than surfacing ELOOP, so the
            // probe and the move refuse the same shape for two unrelated reasons and produce two
            // unrelated diagnostics. That is worth knowing before anyone tries to make one of
            // them the single explanation.
            Assert.False(performed, $"{action} should refuse a symlinked source file");
            Assert.False(File.Exists(destination));
            Assert.True(File.Exists(source), "the link itself must survive a refused action");
            Assert.Equal("audio", await File.ReadAllTextAsync(target));
        }
    }

    [LinuxDirectoryAndFileLinkFact]
    [Trait("Scenario", "A dangling source link reports Unavailable where a dangling ancestor reports Missing")]
    public async Task CheckAsync_DanglingSymlinkedLeafFile_ReportsUnavailableRatherThanMissing()
    {
        var layout = CreateLayout("symlink-shape-dangling");
        var linkDirectory = Path.Join(layout.Root, "incoming");
        Directory.CreateDirectory(linkDirectory);
        var danglingLeaf = Path.Join(linkDirectory, "book.m4b");
        File.CreateSymbolicLink(danglingLeaf, Path.Join(layout.RealDirectory, "gone.m4b"));
        var danglingAncestor = Path.Join(layout.Root, "gone-tier");
        Directory.CreateSymbolicLink(danglingAncestor, Path.Join(layout.Root, "nowhere"));
        var capability = AsCapability(_provider.GetRequiredService<IFileMover>());

        var leafResult = await capability.CheckAsync(danglingLeaf);
        var ancestorResult = await capability.CheckAsync(
            Path.Join(danglingAncestor, "book.m4b"));

        // The control, and the behaviour that is right: a broken ancestor link is a missing file.
        Assert.False(ancestorResult.IsSupported);
        Assert.Equal(
            FilePublicationSourceCapabilityFailureKind.Missing,
            ancestorResult.FailureKind);

        // PINS BEHAVIOUR THAT IS WRONG. The same condition through a broken leaf link is reported
        // as Unavailable, which means temporarily unavailable and is retried. A broken symlink in
        // a download directory is not temporary, and the import will keep coming back to it.
        // Correct behaviour is Missing here too: O_NOFOLLOW on a dangling link gives ELOOP rather
        // than ENOENT, and only the errno differs, not the situation.
        Assert.False(leafResult.IsSupported);
        Assert.Equal(
            FilePublicationSourceCapabilityFailureKind.Unavailable,
            leafResult.FailureKind);
        Assert.Contains(LeafOpenFailure, leafResult.Reason, StringComparison.Ordinal);
    }

    [LinuxDirectoryAndFileLinkFact]
    [Trait("Scenario", "A source whose immediate parent is a symbolic link is publishable")]
    public async Task CheckAsync_SymlinkedImmediateParent_IsSupported()
    {
        // The existing coverage links a grandparent. A cache tier usually links the directory the
        // files sit in, which reaches ResolveSymlinkedAncestors on its first call rather than
        // through the recursion, so it is a different code path in practice.
        var layout = CreateLayout("symlink-shape-immediate-parent");
        SeedFile(layout.RealDirectory, "book.m4b");
        var link = Path.Join(layout.Root, "completed");
        Directory.CreateSymbolicLink(link, layout.RealDirectory);

        var result = await AsCapability(_provider.GetRequiredService<IFileMover>())
            .CheckAsync(Path.Join(link, "book.m4b"));

        Assert.True(result.IsSupported, result.Reason);
        Assert.False(string.IsNullOrWhiteSpace(result.PhysicalObjectIdentity));
    }

    [LinuxDirectoryAndFileLinkFact]
    [Trait("Scenario", "A relative symbolic link ancestor is resolved against the link, not the working directory")]
    public async Task CheckAsync_RelativeSymlinkedAncestor_IsSupported()
    {
        // A relative link target is the shape that breaks when resolution is done by string
        // joining or against the process working directory instead of against the link.
        var layout = CreateLayout("symlink-shape-relative-ancestor");
        SeedFile(layout.RealDirectory, "book.m4b");
        var linkDirectory = Path.Join(layout.Root, "tier");
        Directory.CreateDirectory(linkDirectory);
        var link = Path.Join(linkDirectory, "completed");
        Directory.CreateSymbolicLink(
            link,
            Path.Join("..", "real-downloads", "completed"));

        var result = await AsCapability(_provider.GetRequiredService<IFileMover>())
            .CheckAsync(Path.Join(link, "book.m4b"));

        Assert.True(result.IsSupported, result.Reason);
    }

    [LinuxDirectoryAndFileLinkFact]
    [Trait("Scenario", "A symbolic link to a symbolic link is resolved to the end of the chain")]
    public async Task CheckAsync_ChainedSymlinkedAncestors_IsSupported()
    {
        // Pooled storage stacks links. Resolution that takes one step instead of following the
        // chain lands on another link and fails with the same ELOOP as no resolution at all.
        var layout = CreateLayout("symlink-shape-chained-ancestor");
        SeedFile(layout.RealDirectory, "book.m4b");
        var inner = Path.Join(layout.Root, "tier-one");
        var outer = Path.Join(layout.Root, "tier-two");
        Directory.CreateSymbolicLink(inner, layout.RealDirectory);
        Directory.CreateSymbolicLink(outer, inner);

        var result = await AsCapability(_provider.GetRequiredService<IFileMover>())
            .CheckAsync(Path.Join(outer, "book.m4b"));

        Assert.True(result.IsSupported, result.Reason);
    }

    [LinuxDirectoryAndFileLinkFact]
    [Trait("Scenario", "A source under a symbolic link ancestor is published by every move action")]
    public async Task PerformActionOn_SymlinkedAncestor_PublishesThroughTheLink()
    {
        var layout = CreateLayout("symlink-shape-ancestor-move");
        var link = Path.Join(layout.Root, "completed");
        Directory.CreateSymbolicLink(link, layout.RealDirectory);
        var mover = _provider.GetRequiredService<IFileMover>();

        var copySource = SeedFile(layout.RealDirectory, "copy.m4b");
        var copyDestination = Path.Join(layout.Library, "copy.m4b");
        Assert.True(
            await mover.PerformActionOn(
                FileAction.Copy,
                Path.Join(link, "copy.m4b"),
                copyDestination,
                Guid.NewGuid()),
            "a copy from a linked ancestor should be performed");
        Assert.Equal("audio", await File.ReadAllTextAsync(copyDestination));
        Assert.True(File.Exists(copySource), "a copy must leave the source in place");

        var hardlinkSource = SeedFile(layout.RealDirectory, "hardlink.m4b");
        var hardlinkDestination = Path.Join(layout.RealDirectory, "hardlinked.m4b");
        Assert.True(
            await mover.PerformActionOn(
                FileAction.HardlinkCopy,
                Path.Join(link, "hardlink.m4b"),
                hardlinkDestination,
                Guid.NewGuid()),
            "a hardlink copy from a linked ancestor should be performed");
        Assert.True(File.Exists(hardlinkDestination));
        Assert.True(File.Exists(hardlinkSource));

        // The move is the one that matters, because it deletes the source. The deletion has to
        // land on the real file behind the link and not on the link itself.
        var moveSource = SeedFile(layout.RealDirectory, "move.m4b");
        var moveDestination = Path.Join(layout.Library, "move.m4b");
        Assert.True(
            await mover.PerformActionOn(
                FileAction.Move,
                Path.Join(link, "move.m4b"),
                moveDestination,
                Guid.NewGuid()),
            "a move from a linked ancestor should be performed");
        Assert.Equal("audio", await File.ReadAllTextAsync(moveDestination));
        Assert.False(File.Exists(moveSource), "the real source file should be gone after a move");
        Assert.True(
            Directory.Exists(link),
            "the link itself is not the source and must survive the move");
        Assert.True(Directory.Exists(layout.RealDirectory));
    }

    [LinuxDirectoryAndFileLinkFact]
    [Trait("Scenario", "The probe proof taken through a link is accepted by the registration gate")]
    public async Task PrepareActionForRegistration_SymlinkedAncestorProbeProof_IsAccepted()
    {
        // This is the automatic download-import pairing, and the reason it needs its own test:
        // the import probes first and then hands the proof to the gate. The probe resolves the
        // linked ancestor in FileMover.SourceCapability and the gate resolves it separately in
        // FileMover.FileMoveLocks, through TryResolvePhysicalPath. Two resolutions of the same
        // path by two mechanisms is exactly where a proof stops matching, and nothing else in the
        // suite crosses that seam with a link in the path.
        var layout = CreateLayout("symlink-shape-registration");
        SeedFile(layout.RealDirectory, "book.m4b");
        var link = Path.Join(layout.Root, "completed");
        Directory.CreateSymbolicLink(link, layout.RealDirectory);
        var source = Path.Join(link, "book.m4b");
        var destination = Path.Join(layout.Library, "book.m4b");
        var mover = _provider.GetRequiredService<IFileMover>();

        var probe = await AsCapability(mover).CheckAsync(source);
        Assert.True(probe.IsSupported, probe.Reason);
        Assert.True(probe.SourceProof.HasValue);

        using var lease = await mover.PrepareActionForRegistrationAsync(
            FileAction.Copy,
            source,
            destination,
            Guid.NewGuid(),
            expectedRegisteredPhysicalObjectIdentity: null,
            probe.SourceProof.Value);

        Assert.NotNull(lease);
        Assert.Equal("audio", await File.ReadAllTextAsync(destination));
    }

    [LinuxDirectoryAndFileLinkFact]
    [Trait("Scenario", "An ancestor link that cannot be resolved is refused without naming the link")]
    public async Task CheckAsync_UnresolvableSymlinkedAncestor_IsRefusedWithoutNamingTheLink()
    {
        var layout = CreateLayout("symlink-shape-unresolvable-ancestor");

        // Two links pointing at each other. ResolveSymlinkedAncestors cannot follow this, so the
        // pinned walk sees the link and fails, which is the right answer.
        var cyclicOne = Path.Join(layout.Root, "tier-a");
        var cyclicTwo = Path.Join(layout.Root, "tier-b");
        Directory.CreateSymbolicLink(cyclicOne, cyclicTwo);
        Directory.CreateSymbolicLink(cyclicTwo, cyclicOne);

        // A directory link that points at a regular file. Resolution succeeds and hands back a
        // path that is not a directory, so the walk fails one segment later.
        var regularFile = SeedFile(layout.RealDirectory, "book.m4b");
        var linkToFile = Path.Join(layout.Root, "tier-c");
        Directory.CreateSymbolicLink(linkToFile, regularFile);

        var capability = AsCapability(_provider.GetRequiredService<IFileMover>());
        var cyclicResult = await capability.CheckAsync(Path.Join(cyclicOne, "book.m4b"));
        var fileResult = await capability.CheckAsync(Path.Join(linkToFile, "book.m4b"));

        Assert.False(cyclicResult.IsSupported);
        Assert.False(fileResult.IsSupported);

        // PINS BEHAVIOUR THAT IS WRONG, twice over.
        //
        // First, the sentence. Nothing here was newly created; these are pre-existing directory
        // entries being opened. OpenDirectoryAtUnix carries one message for creation and for
        // opening alike, so the operator is told about a directory the process just made.
        // Correct behaviour is a message that says a hierarchy segment could not be opened, and
        // names the segment.
        //
        // Second, the cause. FindSymlinkedAncestor requires Directory.Exists on the segment, and
        // neither a cycle nor a link to a file satisfies that, so neither refusal mentions a
        // symbolic link even though a symbolic link is the entire reason. Correct behaviour is to
        // detect the link by its reparse attribute rather than by whether it resolves to a
        // readable directory.
        Assert.Contains(HierarchyWalkFailure, cyclicResult.Reason, StringComparison.Ordinal);
        Assert.Contains(HierarchyWalkFailure, fileResult.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "symbolic link",
            cyclicResult.Reason,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "symbolic link",
            fileResult.Reason,
            StringComparison.OrdinalIgnoreCase);
    }

    [LinuxDirectoryAndFileLinkFact]
    [Trait("Scenario", "A linked leaf and a linked ancestor fail at different opens with different text")]
    public void PinnedOpen_LinkedLeafAndLinkedAncestor_FailDistinguishably()
    {
        // The distinction this test exists to hold. Three separate failures, three separate
        // sentences, three separate call sites, and a report that quotes any one of them says
        // which of the three shapes the reporter actually had. Collapsing any two of these into a
        // shared message would make the ELOOP reports unreadable, and would fail here.
        var layout = CreateLayout("symlink-shape-distinct-failures");
        var target = SeedFile(layout.RealDirectory, "book.m4b");

        var leafDirectory = Path.Join(layout.Root, "incoming");
        Directory.CreateDirectory(leafDirectory);
        File.CreateSymbolicLink(Path.Join(leafDirectory, "book.m4b"), target);

        var ancestorLink = Path.Join(layout.Root, "completed");
        Directory.CreateSymbolicLink(ancestorLink, layout.RealDirectory);

        // The leaf open. O_NOFOLLOW on a symbolic link gives ELOOP, and only this call site
        // produces this sentence.
        var leafFailure = CaptureWin32(() =>
        {
            using var anchor = PinnedDirectoryCreation.OpenPinnedHierarchyNoFollow(
                leafDirectory,
                createMissing: false);
            using var entry = anchor.OpenExistingFile("book.m4b", requireDeleteAccess: false);
        });
        Assert.Equal(Eloop, leafFailure.NativeErrorCode);
        Assert.Contains(LeafOpenFailure, leafFailure.Message, StringComparison.Ordinal);

        // A linked ancestor opened directly. O_NOFOLLOW together with O_DIRECTORY gives ENOTDIR
        // rather than ELOOP, so even the errno differs from the leaf case.
        var directoryFailure = CaptureWin32(() =>
        {
            using var anchor = PinnedDirectoryCreation.OpenPinnedDirectoryNoFollow(ancestorLink);
        });
        Assert.Equal(ENotDir, directoryFailure.NativeErrorCode);
        Assert.Contains(DirectoryOpenFailure, directoryFailure.Message, StringComparison.Ordinal);

        // The same linked ancestor reached through the hierarchy walk, which is the path the
        // capability probe and the move gate both take. It is a third sentence, and it is the one
        // that actually reaches an operator. Anyone reading only the two above would predict
        // "Could not open directory" here and be wrong.
        var hierarchyFailure = CaptureWin32(() =>
        {
            using var anchor = PinnedDirectoryCreation.OpenPinnedHierarchyNoFollow(
                Path.Join(ancestorLink, "nested"),
                createMissing: false);
        });
        Assert.Equal(ENotDir, hierarchyFailure.NativeErrorCode);
        Assert.Contains(HierarchyWalkFailure, hierarchyFailure.Message, StringComparison.Ordinal);

        var messages = new[]
        {
            leafFailure.Message,
            directoryFailure.Message,
            hierarchyFailure.Message
        };
        Assert.Equal(3, messages.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(LeafOpenFailure, directoryFailure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(LeafOpenFailure, hierarchyFailure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            DirectoryOpenFailure,
            hierarchyFailure.Message,
            StringComparison.Ordinal);
    }

    [LinuxDirectoryAndFileLinkFact]
    [Trait("Scenario", "A destination reached through a symbolic link ancestor is written through the link")]
    public async Task PerformActionOn_DestinationUnderSymlinkedAncestor_WritesThroughTheLink()
    {
        // PINS BEHAVIOUR THAT CONTRADICTS WHAT THE BRANCH CLAIMS. The commit for the source-side
        // resolution says the write side still fails closed and that a linked destination ancestor
        // is still refused, citing two lock-directory tests. Those two tests pin the application
        // lock directory, not the destination, and the destination is not refused: the gate
        // resolves it through TryResolvePhysicalPath before pinning anything, so the write lands
        // in the link target.
        //
        // The behaviour predates the source-side change and may well be the behaviour we want,
        // since it is what lets a library root sit behind a link. The claim attached to it is what
        // is wrong. Either the write side should refuse a linked destination ancestor, in which
        // case this test should be inverted and the gate changed, or the claim should be dropped.
        var layout = CreateLayout("symlink-shape-linked-destination");
        var source = SeedFile(layout.RealDirectory, "book.m4b");
        var realLibrary = FileService.GetTempDirectory("symlink-shape-linked-destination-real");
        var linkedLibrary = Path.Join(layout.Root, "library");
        Directory.CreateSymbolicLink(linkedLibrary, realLibrary);
        var mover = _provider.GetRequiredService<IFileMover>();

        var performed = await mover.PerformActionOn(
            FileAction.Copy,
            source,
            Path.Join(linkedLibrary, "book.m4b"),
            Guid.NewGuid());

        Assert.True(performed);
        Assert.Equal("audio", await File.ReadAllTextAsync(Path.Join(realLibrary, "book.m4b")));
    }
}
