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
using System.Runtime.InteropServices;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Xunit.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.FileSystem;

/// <summary>
/// Where a symlinked source ancestor sits relative to the configured roots decides whether
/// following it is harmless or an escape.
///
/// The download shape: no download root is configured, the client reports its directories through
/// one symlinked ancestor whose target is on a different filesystem, and the library root is an
/// ordinary directory elsewhere. That has to keep publishing. The escape shape: a directory inside
/// a configured root is a link to somewhere outside every root. That must not publish, and a Move
/// must not retire the file it leads to.
/// </summary>
[Trait("Area", "FileSystem")]
[Trait("Name", "FileMoverSymlinkedSourceBoundaryTests")]
[Trait("Category", "PublicationCapability")]
public sealed class FileMoverSymlinkedSourceBoundaryTests : BaseTests
{
    private const int CrossDeviceLinkErrno = 18;
    private readonly ITestOutputHelper _output;
    private readonly List<string> _crossFilesystemDirectories = [];

    public FileMoverSymlinkedSourceBoundaryTests(ITestOutputHelper output)
    {
        _output = output;
        Init(builder => builder.WithSingleton<ILogger<FileMover>>(new OutputLogger(output)));
    }

    public override async Task DisposeAsync()
    {
        foreach (var directory in _crossFilesystemDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }

        await base.DisposeAsync();
    }

    [CrossFilesystemDirectoryLinkTheory]
    [InlineData(FileAction.Copy)]
    [InlineData(FileAction.Move)]
    [Trait("Scenario", "A download directory reached through a linked ancestor on another filesystem publishes")]
    public async Task DownloadShape_LinkedAncestorOnAnotherFilesystem_PublishesIntoLibraryRoot(FileAction action)
    {
        var layout = CreateDownloadLayout("boundary-download-shape");
        await AddAuthorizedRootAsync(layout.Library, "Library root");

        var outcome = await PublishAsync(action, layout.Source, layout.Destination);

        Assert.True(outcome.Supported, $"the capability check refused the download source: {outcome.Reason}");
        Assert.True(outcome.Published, $"publication failed: {outcome.Failure}");
        Assert.Equal("audio", await File.ReadAllTextAsync(layout.Destination));
    }

    [CrossFilesystemDirectoryLinkTheory]
    [InlineData("client-download-path")]
    [InlineData("root-through-link")]
    [InlineData("root-at-real-path")]
    [Trait("Scenario", "Configuring the download directory does not change whether it publishes")]
    public async Task DownloadShape_DownloadDirectoryConfigured_StillPublishes(string configuration)
    {
        var layout = CreateDownloadLayout("boundary-download-configured");
        await AddAuthorizedRootAsync(layout.Library, "Library root");
        switch (configuration)
        {
            case "client-download-path":
                await _downloadClientConfigurationRepository.SaveAsync(
                    new DownloadClientConfigurationBuilder().WithPath(layout.ClientDirectory).Build());
                break;
            case "root-through-link":
                await AddAuthorizedRootAsync(layout.DownloadTree, "Download root through the link");
                break;
            case "root-at-real-path":
                await AddAuthorizedRootAsync(layout.RealDownloadTree, "Download root at its real path");
                break;
        }

        var outcome = await PublishAsync(FileAction.Copy, layout.Source, layout.Destination);

        Assert.True(outcome.Supported, $"the capability check refused the download source: {outcome.Reason}");
        Assert.True(outcome.Published, $"publication failed: {outcome.Failure}");
    }

    [DirectoryLinkTheory]
    [InlineData(FileAction.Copy)]
    [InlineData(FileAction.Move)]
    [Trait("Scenario", "A link inside a configured root that leads outside every root is refused")]
    public async Task ManagedRoot_LinkedDescendantLeavingEveryRoot_IsRefusedAndLeavesTheTarget(FileAction action)
    {
        var temp = FileService.GetTempDirectory("boundary-escape");
        var outside = Directory.CreateDirectory(Path.Join(temp, "outside", "Book")).FullName;
        var outsideFile = Path.Join(outside, "book.m4b");
        await File.WriteAllTextAsync(outsideFile, "audio");
        var root = Path.Join(temp, "library");
        await AddAuthorizedRootAsync(root, "Escape root");
        Directory.CreateSymbolicLink(Path.Join(root, "incoming"), Path.Join(temp, "outside"));

        var outcome = await PublishAsync(
            action,
            Path.Join(root, "incoming", "Book", "book.m4b"),
            Path.Join(root, "Author", "Book", "book.m4b"));

        Assert.True(
            File.Exists(outsideFile),
            $"the file outside every root was retired through a link inside the root (plan {outcome.Plan})");
        Assert.False(outcome.Supported, "a link inside a configured root carried the source outside every root");
        AssertRefusalNamesLink(outcome, Path.Join(root, "incoming"));
    }

    // The same link inside the root, reached through an alias of the root rather than the root's
    // configured spelling. Where the link physically sits decides, not how the path is spelled.
    [DirectoryLinkFact]
    [Trait("Scenario", "A link inside a root reached through an alias of the root is refused")]
    public async Task ManagedRoot_ReachedThroughAnAlias_LinkLeavingEveryRoot_IsRefused()
    {
        var temp = FileService.GetTempDirectory("boundary-escape-alias");
        var outsideFile = Path.Join(Directory.CreateDirectory(Path.Join(temp, "outside", "Book")).FullName, "book.m4b");
        await File.WriteAllTextAsync(outsideFile, "audio");
        var root = Path.Join(temp, "library");
        await AddAuthorizedRootAsync(root, "Escape root");
        Directory.CreateSymbolicLink(Path.Join(root, "incoming"), Path.Join(temp, "outside"));
        var alias = Path.Join(temp, "alias");
        Directory.CreateSymbolicLink(alias, root);

        var outcome = await PublishAsync(
            FileAction.Move,
            Path.Join(alias, "incoming", "Book", "book.m4b"),
            Path.Join(root, "Author", "Book", "book.m4b"));

        Assert.True(File.Exists(outsideFile), "the file outside every root was retired through a link inside the root");
        Assert.False(outcome.Supported, "a link inside a configured root carried the source outside every root");
        AssertRefusalNamesLink(outcome, alias);
    }

    [DirectoryLinkFact]
    [Trait("Scenario", "A link inside a root configured through a link is refused at the root's real path")]
    public async Task ManagedRoot_ConfiguredThroughALink_LinkLeavingEveryRoot_IsRefusedAtTheRealPath()
    {
        var temp = FileService.GetTempDirectory("boundary-escape-linked-root");
        var outsideFile = Path.Join(Directory.CreateDirectory(Path.Join(temp, "outside", "Book")).FullName, "book.m4b");
        await File.WriteAllTextAsync(outsideFile, "audio");
        var real = Directory.CreateDirectory(Path.Join(temp, "real-library")).FullName;
        Directory.CreateSymbolicLink(Path.Join(real, "incoming"), Path.Join(temp, "outside"));
        var linkedRoot = Path.Join(temp, "linked-root");
        Directory.CreateSymbolicLink(linkedRoot, real);
        await AddAuthorizedRootAsync(linkedRoot, "Escape root");

        var outcome = await PublishAsync(
            FileAction.Move,
            Path.Join(real, "incoming", "Book", "book.m4b"),
            Path.Join(linkedRoot, "Author", "Book", "book.m4b"));

        Assert.True(File.Exists(outsideFile), "the file outside every root was retired through a link inside the root");
        Assert.False(outcome.Supported, "a link inside a configured root carried the source outside every root");
        AssertRefusalNamesLink(outcome, Path.Join(real, "incoming"));
    }

    // The root's real directory, and a link inside it, stay reachable while the link the root is
    // configured through cannot be followed at check time. Its real path cannot be established,
    // so the root has to count as containing the link rather than be compared by spelling alone.
    [DirectoryLinkTheory]
    [InlineData("unreadable")]
    [InlineData("deleted")]
    [InlineData("dangling")]
    [Trait("Scenario", "A root whose real path cannot be established still contains the links in its real directory")]
    public async Task ManagedRoot_ConfiguredThroughALinkThatCannotBeFollowed_LinkLeavingEveryRoot_IsRefused(string state)
    {
        var temp = FileService.GetTempDirectory("boundary-escape-unresolvable-root-" + state);
        var outsideFile = Path.Join(Directory.CreateDirectory(Path.Join(temp, "outside", "Book")).FullName, "book.m4b");
        await File.WriteAllTextAsync(outsideFile, "audio");
        var library = Path.Join(temp, "library");
        await AddAuthorizedRootAsync(library, "Library root");
        var real = Directory.CreateDirectory(Path.Join(temp, "real-library")).FullName;
        Directory.CreateSymbolicLink(Path.Join(real, "incoming"), Path.Join(temp, "outside"));
        var guard = Directory.CreateDirectory(Path.Join(temp, "guard")).FullName;
        var linkedRoot = Path.Join(guard, "linked-root");
        Directory.CreateSymbolicLink(linkedRoot, real);
        await AddAuthorizedRootAsync(linkedRoot, "Escape root");
        switch (state)
        {
            case "deleted":
                File.Delete(linkedRoot);
                break;
            case "dangling":
                var moved = Path.Join(temp, "moved-library");
                Directory.Move(real, moved);
                real = moved;
                break;
        }

        PublicationOutcome outcome;
        if (state == "unreadable")
            File.SetUnixFileMode(guard, UnixFileMode.None);
        try
        {
            outcome = await PublishAsync(
                FileAction.Move,
                Path.Join(real, "incoming", "Book", "book.m4b"),
                Path.Join(library, "Author", "Book", "book.m4b"));
        }
        finally
        {
            File.SetUnixFileMode(guard, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Assert.True(File.Exists(outsideFile), "the file outside every root was retired through a link inside the root");
        Assert.False(outcome.Supported, "a link inside a configured root carried the source outside every root");
        AssertRefusalNamesLink(outcome, Path.Join(real, "incoming"));
    }

    // Known gap, pinned as it stands rather than skipped. A chain outside every root resolves at
    // the capability gate, but preparing the publication then fails closed in its path-lock step:
    // nothing is published and the source is untouched. A skip would stop exercising either
    // half, and the fail-closed half is the one that matters. When the lock step learns to follow
    // the chain, this assertion fails and should become a publish.
    [DirectoryLinkFact]
    [Trait("Scenario", "A two-hop chain outside every root passes the gate and fails closed at publication")]
    public async Task Chain_OutsideEveryRoot_PassesTheGateAndFailsClosedAtPublication()
    {
        var temp = FileService.GetTempDirectory("boundary-chain-outside");
        Directory.CreateDirectory(Path.Join(temp, "c", "sub"));
        await File.WriteAllTextAsync(Path.Join(temp, "c", "sub", "book.m4b"), "audio");
        Directory.CreateSymbolicLink(Path.Join(temp, "b"), Path.Join(temp, "c"));
        Directory.CreateSymbolicLink(Path.Join(temp, "a"), Path.Join("b", "sub"));
        var library = Path.Join(temp, "library");
        await AddAuthorizedRootAsync(library, "Library root");

        var outcome = await PublishAsync(
            FileAction.Copy,
            Path.Join(temp, "a", "book.m4b"),
            Path.Join(library, "Author", "Book", "book.m4b"));

        Assert.True(outcome.Supported, $"the capability check refused the chained source: {outcome.Reason}");
        Assert.False(outcome.Published, "a chained source now publishes; the known gap is closed, so assert the publish");
        Assert.Contains("durable_publication_blocked", outcome.Failure, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Join(temp, "c", "sub", "book.m4b")), "the chained source was touched");
        Assert.False(File.Exists(Path.Join(library, "Author", "Book", "book.m4b")), "a blocked publication left a file");
    }

    [DirectoryLinkFact]
    [Trait("Scenario", "A two-hop chain starting inside a root and ending outside every root is refused")]
    public async Task Chain_InsideRootLeavingEveryRoot_IsRefused()
    {
        var temp = FileService.GetTempDirectory("boundary-chain-escape");
        Directory.CreateDirectory(Path.Join(temp, "outside", "Book"));
        await File.WriteAllTextAsync(Path.Join(temp, "outside", "Book", "book.m4b"), "audio");
        var root = Path.Join(temp, "library");
        await AddAuthorizedRootAsync(root, "Escape root");
        Directory.CreateSymbolicLink(Path.Join(root, "b"), Path.Join(temp, "outside"));
        Directory.CreateSymbolicLink(Path.Join(root, "a"), "b");

        var outcome = await PublishAsync(
            FileAction.Copy,
            Path.Join(root, "a", "Book", "book.m4b"),
            Path.Join(root, "Author", "Book", "book.m4b"));

        Assert.False(outcome.Supported, "a chain inside a configured root carried the source outside every root");
        AssertRefusalNamesLink(outcome, Path.Join(root, "a"));
    }

    [DirectoryLinkFact]
    [Trait("Scenario", "A two-hop chain that stays inside one root follows the root's own walk")]
    public async Task Chain_StayingInsideRoot_FollowsTheManagedWalk()
    {
        var temp = FileService.GetTempDirectory("boundary-chain-inside");
        var root = Path.Join(temp, "library");
        await AddAuthorizedRootAsync(root, "Chain root");
        Directory.CreateDirectory(Path.Join(root, "c", "sub"));
        await File.WriteAllTextAsync(Path.Join(root, "c", "sub", "book.m4b"), "audio");
        Directory.CreateSymbolicLink(Path.Join(root, "b"), Path.Join(root, "c"));
        Directory.CreateSymbolicLink(Path.Join(root, "a"), Path.Join("b", "sub"));

        var outcome = await PublishAsync(
            FileAction.Copy,
            Path.Join(root, "a", "book.m4b"),
            Path.Join(root, "Author", "Book", "book.m4b"));

        // The managed walk refuses every link below a configured root, wherever it leads. This
        // pins that policy; it is not an escape either way.
        Assert.False(outcome.Supported, "a link below a configured root was followed by the managed walk");
        AssertRefusalNamesLink(outcome, Path.Join(root, "a"));
    }

    // Inside a root the managed walk refuses the first link below the root, so that is the link
    // the operator has to be told about, whether or not it would have resolved.
    private static void AssertRefusalNamesLink(PublicationOutcome outcome, string link) =>
        Assert.Contains($"symbolic link at '{link}'", outcome.Reason, StringComparison.Ordinal);

    private DownloadLayout CreateDownloadLayout(string name)
    {
        var temp = FileService.GetTempDirectory(name);
        var crossFilesystem = Path.Join(
            Environment.GetEnvironmentVariable(
                CrossFilesystemDirectoryLinkTheoryAttribute.PathEnvironmentVariable)!,
            Guid.NewGuid().ToString("N"));
        _crossFilesystemDirectories.Add(crossFilesystem);

        // The link sits two levels down and points, by absolute target, at another filesystem.
        // The two client directories are siblings below it, and neither is itself a link.
        var target = Path.Join(crossFilesystem, "storage");
        var realBook = Directory.CreateDirectory(Path.Join(target, "dl", "torrent", "Book")).FullName;
        Directory.CreateDirectory(Path.Join(target, "dl", "usenet"));
        File.WriteAllText(Path.Join(realBook, "book.m4b"), "audio");
        var link = Path.Join(Directory.CreateDirectory(Path.Join(temp, "mnt")).FullName, "media");
        Directory.CreateSymbolicLink(link, target);

        var library = Path.Join(temp, "library");
        AssertDifferentFilesystems(Path.Join(realBook, "book.m4b"), Path.Join(temp, "hardlink-probe"));

        var downloadTree = Path.Join(link, "dl");
        var clientDirectory = Path.Join(downloadTree, "torrent");
        return new DownloadLayout(
            downloadTree,
            Path.Join(target, "dl"),
            clientDirectory,
            Path.Join(clientDirectory, "Book", "book.m4b"),
            library,
            Path.Join(library, "Author", "Book", "book.m4b"));
    }

    // The point of the second filesystem is that a hardlink cannot be made, so the import copies.
    // Proving that directly is the control: if both paths were on one filesystem this would link.
    private static void AssertDifferentFilesystems(string existingFile, string probe)
    {
        var result = link(existingFile, probe);
        var errno = Marshal.GetLastPInvokeError();
        if (result == 0)
        {
            File.Delete(probe);
        }

        Assert.True(
            result != 0 && errno == CrossDeviceLinkErrno,
            $"the link target is not on a second filesystem (link returned {result}, errno {errno})");
    }

    private async Task<PublicationOutcome> PublishAsync(FileAction action, string source, string destination)
    {
        var capability = await _provider.GetRequiredService<IFilePublicationSourceCapability>()
            .CheckAsync(source);
        _output.WriteLine(
            $"capability supported={capability.IsSupported} kind={capability.FailureKind} reason={capability.Reason}");
        if (!capability.IsSupported || !capability.SourceProof.HasValue)
        {
            return new PublicationOutcome(false, capability.Reason, null, false, "capability refused");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var operationId = Guid.NewGuid();
        var plan = await _provider.GetRequiredService<IFilePublicationCapabilityResolver>()
            .ResolveAsync(action, source, destination, capability.SourceProof.Value);
        _output.WriteLine($"plan mode={plan.Mode} effective={plan.EffectiveAction} reason={plan.ReasonCode} {plan.Message}");
        if (!plan.IsAllowed)
        {
            return new PublicationOutcome(true, null, plan.Mode, false, $"plan blocked: {plan.Message}");
        }

        var mover = _provider.GetRequiredService<IFileMover>();
        var preparation = await mover.PrepareActionForRegistrationDetailedAsync(
            plan,
            source,
            destination,
            operationId,
            expectedRegisteredPhysicalObjectIdentity: null,
            capability.SourceProof.Value);
        using var lease = preparation.RegistrationLease;
        if (lease == null || !lease.PrepareCleanupRecovery(1))
        {
            WriteFileState(source, destination);
            return new PublicationOutcome(true, null, plan.Mode, false,
                $"preparation {preparation.Outcome} {preparation.ReasonCode} {preparation.Message}");
        }

        // The order registration uses: the publication commits, then a Move retires its source.
        _output.WriteLine($"published completion={lease.CompletePublication()}");
        if (plan.EffectiveAction == FileAction.Move
            && !await mover.CompletePreparedMoveAsync(source, destination, lease, operationId))
        {
            WriteFileState(source, destination);
            return new PublicationOutcome(true, null, plan.Mode, false, "prepared move did not complete");
        }

        WriteFileState(source, destination);
        return new PublicationOutcome(true, null, plan.Mode, true, null);
    }

    private void WriteFileState(string source, string destination) =>
        _output.WriteLine($"files sourceExists={File.Exists(source)} destinationExists={File.Exists(destination)}");

    [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int link(string existingPath, string newPath);

    private sealed class OutputLogger(ITestOutputHelper output) : ILogger<FileMover>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            output.WriteLine($"log {logLevel}: {formatter(state, exception)} {exception?.GetType().Name} {exception?.Message}");
    }

    private sealed record DownloadLayout(
        string DownloadTree,
        string RealDownloadTree,
        string ClientDirectory,
        string Source,
        string Library,
        string Destination);

    private sealed record PublicationOutcome(
        bool Supported,
        string? Reason,
        FilePublicationExecutionMode? Plan,
        bool Published,
        string? Failure);
}
