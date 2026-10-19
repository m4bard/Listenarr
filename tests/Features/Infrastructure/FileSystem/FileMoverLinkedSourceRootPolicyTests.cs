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
/// A source reported through a link outside every root can physically be a file a root holds.
/// That root's storage policy has to decide what happens to it, exactly as when the same file is
/// reported at its in-root path.
/// </summary>
[Trait("Area", "FileSystem")]
[Trait("Name", "FileMoverLinkedSourceRootPolicyTests")]
[Trait("Category", "PublicationCapability")]
public sealed class FileMoverLinkedSourceRootPolicyTests : BaseTests
{
    private const string HoldingRootName = "Holding root";

    public FileMoverLinkedSourceRootPolicyTests()
    {
        Init(builder => builder.WithSingleton<IRootFolderStorageHealthResolver>(new HoldingRootForbidsMutation()));
    }

    [DirectoryLinkTheory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Scenario", "A Move of a file a root holds follows that root's mutation policy however it is reported")]
    public async Task Move_SourceInAHoldingRoot_IsBlockedByThatRootWhetherReportedThereOrThroughALink(bool throughLink)
    {
        var temp = FileService.GetTempDirectory("linked-source-root-policy");
        var holding = Path.Join(temp, "holding");
        await AddAuthorizedRootAsync(holding, HoldingRootName);
        var held = Directory.CreateDirectory(Path.Join(holding, "Other", "Book")).FullName;
        var heldFile = Path.Join(held, "book.m4b");
        await File.WriteAllTextAsync(heldFile, "audio");
        var library = Path.Join(temp, "library");
        await AddAuthorizedRootAsync(library, "Library root");
        var downloads = Directory.CreateDirectory(Path.Join(temp, "dl")).FullName;
        Directory.CreateSymbolicLink(Path.Join(downloads, "incoming"), held);
        var source = throughLink ? Path.Join(downloads, "incoming", "book.m4b") : heldFile;
        var destination = Path.Join(library, "Author", "Book", "book.m4b");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        var capability = await _provider.GetRequiredService<IFilePublicationSourceCapability>()
            .CheckAsync(source);
        Assert.True(capability.IsSupported, capability.Reason);
        var plan = await _provider.GetRequiredService<IFilePublicationCapabilityResolver>()
            .ResolveAsync(FileAction.Move, source, destination, capability.SourceProof!.Value);
        Assert.Equal(FilePublicationExecutionMode.Durable, plan.Mode);
        var preparation = await _provider.GetRequiredService<IFileMover>()
            .PrepareActionForRegistrationDetailedAsync(
                plan,
                source,
                destination,
                Guid.NewGuid(),
                expectedRegisteredPhysicalObjectIdentity: null,
                capability.SourceProof.Value);
        using var lease = preparation.RegistrationLease;

        Assert.Equal(FilePublicationOutcome.Blocked, preparation.Outcome);
        Assert.True(File.Exists(heldFile), "the holding root's file was touched");
        Assert.False(File.Exists(destination), "a blocked Move published its destination");
    }

    // The holding root may retire sources, so the plan stays a durable Move, but it does not
    // authorize filesystem mutation, which is what the Move itself has to respect.
    private sealed class HoldingRootForbidsMutation : IRootFolderStorageHealthResolver
    {
        public Task<RootFolderStorageObservation> ResolveAsync(
            RootFolder root,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(root.Name == HoldingRootName
                ? new RootFolderStorageObservation(
                    RootFolderStorageState.Limited, RootFolderStorageReason.MutationCapabilityUnavailable,
                    "The holding root does not authorize filesystem mutation.",
                    CanConfirmCurrentFolder: false, CanChangePath: false,
                    CanMutateFilesystem: false, ConfirmationToken: null,
                    CanPublishNewFiles: true, CanRetireSource: true,
                    CanRetireAfterVerifiedCopy: true)
                : new RootFolderStorageObservation(
                    RootFolderStorageState.Healthy, RootFolderStorageReason.None, null,
                    CanConfirmCurrentFolder: true, CanChangePath: true,
                    CanMutateFilesystem: true, ConfirmationToken: null,
                    CanPublishNewFiles: true, CanRetireSource: true,
                    CanRetireAfterVerifiedCopy: true));
    }
}
