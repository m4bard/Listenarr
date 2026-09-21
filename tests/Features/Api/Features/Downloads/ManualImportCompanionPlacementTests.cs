/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Api.Dtos.ManualImport;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Api.Features.Downloads;

/// <summary>
/// Where the manual-import companion pass puts the files it sweeps up.
///
/// The request carries a <c>path</c>, and GET preview enumerates below it with
/// SearchOption.AllDirectories, so that path is routinely several directories above the files
/// the user then selects. Relativizing a companion against it recreated every directory in
/// between inside the book folder. These pin the placement rule rather than the symptom: the
/// only structure reproduced at the destination is the one the selected files themselves have.
/// </summary>
[Trait("Name", "ManualImportCompanionPlacementTests")]
[Trait("Category", "Unit")]
public sealed class ManualImportCompanionPlacementTests : BaseTests
{
    private sealed record CompanionOutcome(
        int Imported,
        IReadOnlyList<string> Destinations);

    /// <summary>
    /// Drives one companion pass over real files on disk, with the publication and ownership
    /// collaborators stubbed out, and reports the destination paths the pass asked the mover to
    /// publish to. Nothing is moved: the destination path is the claim under test.
    /// </summary>
    private static async Task<CompanionOutcome> RunPassAsync(
        string requestPath,
        IReadOnlyList<(string Source, string Destination)> selected,
        string audiobookBasePath)
    {
        var destinations = new List<string>();
        var audiobook = new Audiobook { Id = 77, BasePath = audiobookBasePath };

        var lease = new Mock<IAudiobookFileRegistrationLease>();
        lease.Setup(instance => instance.PrepareCleanupRecovery(It.IsAny<int>())).Returns(true);
        lease.Setup(instance => instance.CompletePublication())
            .Returns(RegistrationPublicationCompletion.Completed);

        var mover = new Mock<IFileMover>();
        mover
            .Setup(service => service.PrepareActionForRegistrationDetailedAsync(
                It.IsAny<FilePublicationPlan>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<FilePublicationSourceProof>(),
                It.IsAny<bool>(),
                It.IsAny<int?>()))
            .Callback<FilePublicationPlan, string, string, Guid, string?,
                FilePublicationSourceProof, bool, int?>(
                (_, _, destination, _, _, _, _, _) => destinations.Add(destination))
            .ReturnsAsync(new FilePublicationPreparationResult(
                FilePublicationOutcome.Success,
                FileAction.Copy,
                FileAction.Copy,
                FilePublicationSourceDisposition.Unchanged,
                lease.Object));

        var sourceCapability = new Mock<IFilePublicationSourceCapability>();
        sourceCapability
            .Setup(service => service.CheckAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FilePublicationSourceCapabilityResult.SupportedForProof(
                new FilePublicationSourceProof("test-source-generation", 1, new string('A', 64))));

        var fileService = new Mock<IAudiobookFileService>();
        fileService
            .Setup(service => service.CheckAudiobookFileOwnershipAsync(
                It.IsAny<Audiobook>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AudiobookFileOwnershipCheckResult(
                AudiobookFileOwnershipCheckOutcome.Available));

        var ownershipStore = new Mock<ILibraryDirectoryOwnershipStore>();
        ownershipStore
            .Setup(store => store.EnsureCreatedHierarchyAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<FileSystemPathSemantics>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        ownershipStore
            .Setup(store => store.EnsureAdditiveHierarchyAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<FileSystemPathSemantics>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var importer = new ManualImportCompanionImporter(
            Mock.Of<IMetadataService>(),
            mover.Object,
            sourceCapability.Object,
            new LocalFileSystem(),
            ownershipStore.Object,
            NullLogger<ManualImportCompanionImporter>.Instance,
            fileService.Object);

        var semanticsResolver = new FileSystemSemanticsResolver();
        var sourceResolution = await semanticsResolver.ResolveAsync(requestPath);
        var destinationResolution = await semanticsResolver.ResolveAsync(audiobookBasePath);
        Assert.Equal(PathIdentityState.Valid, sourceResolution.State);
        Assert.Equal(PathIdentityState.Valid, destinationResolution.State);

        var items = selected
            .Select(entry => new ManualImportItemDto
            {
                FullPath = entry.Source,
                MatchedAudiobookId = audiobook.Id
            })
            .ToList();
        var results = selected
            .Select(entry => new ManualImportResultDto
            {
                Success = true,
                SourcePath = entry.Source,
                DestinationPath = entry.Destination,
                Audiobook = audiobook
            })
            .ToList();

        var imported = await importer.ImportAsync(
            FileAction.Copy,
            items,
            results,
            requestPath,
            selectedAudioProfiles: [],
            new ManualImportDestinationTracker(
                new LocalFileSystem(),
                Mock.Of<IFilePublicationSourceCapability>()),
            sourceResolution.Semantics,
            new Dictionary<int, FileSystemSemanticsResolution>
            {
                [audiobook.Id] = destinationResolution
            },
            importBlacklist: []);

        return new CompanionOutcome(imported, destinations);
    }

    private static string NewTestRoot(string name) => Path.Join(
        Path.GetTempPath(),
        "listenarr-tests",
        $"manual-companion-placement-{name}-{Guid.NewGuid():N}");

    private static async Task WriteAsync(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    /// <summary>
    /// The defect. The request's path is the folder the user browsed to; the audio sits three
    /// directories below it. Before the fix the companion was relativized against the request
    /// path, so <c>src/Arthur Conan Doyle/2006 - The Valley of Fear/</c> was recreated inside the
    /// book folder.
    /// </summary>
    [Fact]
    public async Task ImportAsync_RequestPathAboveTheSelectedFiles_DoesNotMirrorTheSourceTree()
    {
        var testRoot = NewTestRoot("ancestor");
        var requestPath = Path.Join(testRoot, "src");
        var selectedDirectory = Path.Join(
            requestPath,
            "Arthur Conan Doyle",
            "2006 - The Valley of Fear");
        var bookFolder = Path.Join(testRoot, "library", "The Valley of Fear");
        try
        {
            var audioSource = Path.Join(selectedDirectory, "book.m4b");
            var companionSource = Path.Join(selectedDirectory, "book.nfo");
            await WriteAsync(audioSource, "audio");
            await WriteAsync(companionSource, "sidecar");
            Directory.CreateDirectory(bookFolder);

            var outcome = await RunPassAsync(
                requestPath,
                [(audioSource, Path.Join(bookFolder, "The Valley of Fear.m4b"))],
                bookFolder);

            Assert.Equal(1, outcome.Imported);
            AssertPlacedAt(bookFolder, outcome.Destinations, "book.nfo");
            AssertNoDirectoriesUnder(bookFolder);
        }
        finally
        {
            Cleanup(testRoot);
        }
    }

    /// <summary>
    /// The bare-root variant of the same defect: a request path with nothing below it to
    /// relativize against hands back the companion's own absolute path with the root stripped
    /// off, which no containment check can reject.
    /// </summary>
    [Fact]
    public async Task ImportAsync_RequestPathIsTheFilesystemRoot_DoesNotMirrorTheAbsolutePath()
    {
        var testRoot = NewTestRoot("bare-root");
        var selectedDirectory = Path.Join(testRoot, "downloads", "The.Release");
        var bookFolder = Path.Join(testRoot, "library", "The Valley of Fear");
        var filesystemRoot = Path.GetPathRoot(testRoot)
            ?? throw new InvalidOperationException("The host filesystem root is unavailable.");
        try
        {
            var audioSource = Path.Join(selectedDirectory, "book.m4b");
            var companionSource = Path.Join(selectedDirectory, "book.nfo");
            await WriteAsync(audioSource, "audio");
            await WriteAsync(companionSource, "sidecar");
            Directory.CreateDirectory(bookFolder);

            var outcome = await RunPassAsync(
                filesystemRoot,
                [(audioSource, Path.Join(bookFolder, "The Valley of Fear.m4b"))],
                bookFolder);

            Assert.Equal(1, outcome.Imported);
            AssertPlacedAt(bookFolder, outcome.Destinations, "book.nfo");
            AssertNoDirectoriesUnder(bookFolder);
        }
        finally
        {
            Cleanup(testRoot);
        }
    }

    /// <summary>
    /// Control: nothing was flattened. A companion that genuinely sits one directory below the
    /// audio it accompanies keeps that relationship, because the selected files themselves have
    /// that structure and the audio's destination has it too.
    /// </summary>
    [Fact]
    public async Task ImportAsync_CompanionBelowTheAudioDirectory_KeepsItsSubdirectory()
    {
        var testRoot = NewTestRoot("nested");
        var requestPath = Path.Join(testRoot, "src", "The.Release");
        var extrasDirectory = Path.Join(requestPath, "extras");
        var bookFolder = Path.Join(testRoot, "library", "The Valley of Fear");
        try
        {
            var audioSource = Path.Join(requestPath, "book.m4b");
            var extrasAudioSource = Path.Join(extrasDirectory, "interview.m4b");
            var companionSource = Path.Join(extrasDirectory, "interview.nfo");
            await WriteAsync(audioSource, "audio");
            await WriteAsync(extrasAudioSource, "bonus audio");
            await WriteAsync(companionSource, "sidecar");
            Directory.CreateDirectory(bookFolder);

            var outcome = await RunPassAsync(
                requestPath,
                [
                    (audioSource, Path.Join(bookFolder, "The Valley of Fear.m4b")),
                    (extrasAudioSource, Path.Join(bookFolder, "extras", "interview.m4b"))
                ],
                bookFolder);

            Assert.Equal(1, outcome.Imported);
            AssertPlacedAt(
                bookFolder,
                outcome.Destinations,
                Path.Join("extras", "interview.nfo"));
        }
        finally
        {
            Cleanup(testRoot);
        }
    }

    /// <summary>
    /// Control: nothing was refused wholesale. The ordinary shape, where the request path is the
    /// folder the audio is actually in, places the companion beside the audio exactly as before.
    /// </summary>
    [Fact]
    public async Task ImportAsync_RequestPathAtTheSelectedDirectory_PlacesCompanionBesideTheAudio()
    {
        var testRoot = NewTestRoot("plain");
        var requestPath = Path.Join(testRoot, "src", "The.Release");
        var bookFolder = Path.Join(testRoot, "library", "The Valley of Fear");
        try
        {
            var audioSource = Path.Join(requestPath, "book.m4b");
            var companionSource = Path.Join(requestPath, "book.nfo");
            await WriteAsync(audioSource, "audio");
            await WriteAsync(companionSource, "sidecar");
            Directory.CreateDirectory(bookFolder);

            var outcome = await RunPassAsync(
                requestPath,
                [(audioSource, Path.Join(bookFolder, "The Valley of Fear.m4b"))],
                bookFolder);

            Assert.Equal(1, outcome.Imported);
            AssertPlacedAt(bookFolder, outcome.Destinations, "book.nfo");
            AssertNoDirectoriesUnder(bookFolder);
        }
        finally
        {
            Cleanup(testRoot);
        }
    }

    /// <summary>
    /// The containment guard the resolved relative path still passes through. The resolver no
    /// longer produces a traversing path, so this is the control showing the guard that would
    /// catch one is intact and still discriminates.
    /// </summary>
    [Fact]
    public void ContainmentGuard_StillRejectsATraversingRelativePath()
    {
        var unix = new FileSystemPathSemantics(
            FileSystemPathSyntax.Unix,
            FileSystemCaseSensitivity.Sensitive);

        Assert.False(FileSystemPathIdentity.TryResolveRelativePathWithinBase(
            "/library/Book", "../escaped.nfo", unix, out _));
        Assert.False(FileSystemPathIdentity.TryResolveRelativePathWithinBase(
            "/library/Book", "/etc/passwd", unix, out _));
        Assert.True(FileSystemPathIdentity.TryResolveRelativePathWithinBase(
            "/library/Book", "book.nfo", unix, out var allowed));
        Assert.Equal("/library/Book/book.nfo", allowed);
    }

    /// <summary>
    /// Compares the destinations as paths relative to the book folder, so a failure names the
    /// tree that was mirrored instead of two absolute paths that differ somewhere off the end
    /// of the line.
    /// </summary>
    private static void AssertPlacedAt(
        string bookFolder,
        IReadOnlyList<string> destinations,
        params string[] expectedRelativePaths)
    {
        Assert.Equal(
            expectedRelativePaths,
            destinations.Select(destination => Path.GetRelativePath(bookFolder, destination)));
    }

    /// <summary>
    /// The book folder gained no subdirectories. Asserting only the destination paths would not
    /// have caught the defect on its own, because a mirrored tree is still inside the book
    /// folder, and the directories outlive a companion that later fails to publish.
    /// </summary>
    private static void AssertNoDirectoriesUnder(string bookFolder)
    {
        Assert.Equal(
            [],
            Directory.GetDirectories(bookFolder, "*", SearchOption.AllDirectories)
                .Select(directory => Path.GetRelativePath(bookFolder, directory)));
    }

    private static void Cleanup(string testRoot)
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }
}
