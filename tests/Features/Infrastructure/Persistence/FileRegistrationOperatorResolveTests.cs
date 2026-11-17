using Listenarr.Application.Common.Exceptions;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;
using static Listenarr.Tests.Features.Infrastructure.Persistence.FileRegistrationOperatorReviewHarness;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

[Trait("Area", "Library")]
[Trait("Name", "FileRegistrationOperatorResolveTests")]
[Trait("Category", "Infrastructure")]
public sealed class FileRegistrationOperatorResolveTests : BaseTests
{
    private const string Note = "Reviewed the retained source and the destination";

    [Fact]
    public async Task CommittedPublicationWithMissingDestinationParent_ParksNeedsAttention_ThenIsInspectableAndResolvable()
    {
        var seeded = await SeedRegisteredMoveAsync("resolve-premise", FileMutationJournalState.SourceDeletionAuthorized);
        var service = CreateService(seeded.Factory);
        Directory.Delete(Path.GetDirectoryName(seeded.Destination)!, recursive: true);

        var blocked = await Assert.ThrowsAsync<ApplicationConflictException>(
            () => service.ReconcileAudiobookWithReceiptsAsync(seeded.AudiobookId, []));

        Assert.Equal("registration_recovery_repair_required", blocked.Code);
        var parked = await LoadJournalAsync(seeded);
        Assert.Equal(FileMutationJournalState.NeedsAttention, parked.State);
        Assert.Contains("no longer matches", parked.Error);
        var review = await service.InspectAsync(seeded.OperationId);
        Assert.Equal("Missing", review.Destination.Status);
        Assert.Equal("Present", review.Source.Status);
        Assert.True(review.CanResolve);
        var resolved = await service.ResolveAsync(seeded.OperationId, review.ReviewToken, Note, true, "tester");
        Assert.Equal("OperatorResolved", resolved.State);
        Assert.False(await new FileRegistrationRecoveryProbe(seeded.Factory).HasBlockingAsync(seeded.AudiobookId));
    }

    [Fact]
    public async Task CommittedPublicationWithDestinationParentPresent_ControlClearsWithoutParking()
    {
        var seeded = await SeedRegisteredMoveAsync("resolve-premise-control", FileMutationJournalState.SourceDeletionAuthorized);

        await CreateService(seeded.Factory).ReconcileAudiobookWithReceiptsAsync(seeded.AudiobookId, []);

        Assert.Equal(FileMutationJournalState.CompletedSourceRetained, (await LoadJournalAsync(seeded)).State);
    }

    [Theory]
    [InlineData(DestinationShape.MissingFile, "Missing", "completeness cannot be verified")]
    [InlineData(DestinationShape.MissingParent, "Missing", "completeness cannot be verified")]
    [InlineData(DestinationShape.Partial, "Present", "does not match")]
    [InlineData(DestinationShape.Matching, "Present", "Destination matches")]
    public async Task Resolve_RetainedSourcePresent_ReleasesBoundaryForEveryDestinationShape(
        DestinationShape shape, string destinationStatus, string evidenceFragment)
    {
        var seeded = await SeedAsync(FileService.GetTempDirectory("resolve-shapes"), shape);
        var service = CreateService(seeded.Factory);
        var filesBefore = await HashTreeAsync(seeded.Root);
        var review = await service.InspectAsync(seeded.OperationId);
        Assert.Equal("Present", review.Source.Status);
        Assert.Equal(destinationStatus, review.Destination.Status);
        Assert.Contains(evidenceFragment, review.ContentEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.True(review.CanResolve);
        Assert.True(await new FileRegistrationRecoveryProbe(seeded.Factory).HasBlockingAsync(seeded.AudiobookId));

        var resolved = await service.ResolveAsync(seeded.OperationId, review.ReviewToken, Note, true, "tester");

        Assert.Equal("OperatorResolved", resolved.State);
        Assert.Equal(destinationStatus, resolved.Destination.Status);
        Assert.False(await new FileRegistrationRecoveryProbe(seeded.Factory).HasBlockingAsync(seeded.AudiobookId));
        Assert.Equal(filesBefore, await HashTreeAsync(seeded.Root));
        var row = await LoadJournalAsync(seeded);
        Assert.Equal(FileMutationJournalState.OperatorResolved, row.State);
        Assert.Equal(ParkedError, row.Error);
        Assert.Equal(Note, row.ResolutionNote);
        Assert.Equal(FileAction.HardlinkCopy, row.Action);
        Assert.Null(row.AudiobookFileId);
        Assert.Equal(shape == DestinationShape.MissingParent, !Directory.Exists(Path.GetDirectoryName(seeded.Destination)!));
        await using var db = seeded.Factory.CreateDbContext();
        Assert.Empty(await db.AudiobookFiles.ToListAsync());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(FileMutationProtocol.MarkerlessDatabaseState)]
    [InlineData(FileMutationProtocol.ParentGenerationMarkerlessDatabaseState)]
    [InlineData(FileMutationProtocol.OperationEvidence)]
    [InlineData(99)]
    public async Task Resolve_ParkedRowOfAnyProtocolVersion_ReleasesBoundary(int protocolVersion)
    {
        var seeded = await SeedAsync(FileService.GetTempDirectory("resolve-protocol"), DestinationShape.MissingFile,
            configure: journal => journal.ProtocolVersion = protocolVersion);
        var service = CreateService(seeded.Factory);
        var review = await service.InspectAsync(seeded.OperationId);
        Assert.Equal(protocolVersion, review.ProtocolVersion);

        await service.ResolveAsync(seeded.OperationId, review.ReviewToken, Note, true, "tester");

        var row = await LoadJournalAsync(seeded);
        Assert.Equal(FileMutationJournalState.OperatorResolved, row.State);
        Assert.Equal(protocolVersion, row.ProtocolVersion);
        Assert.False(await new FileRegistrationRecoveryProbe(seeded.Factory).HasBlockingAsync(seeded.AudiobookId));
    }

    [Theory]
    [InlineData(FileAction.Move)]
    [InlineData(FileAction.Copy)]
    [InlineData(FileAction.HardlinkCopy)]
    public async Task Resolve_AnyRegistrationAction_ReleasesBoundaryAndKeepsAction(FileAction action)
    {
        var seeded = await SeedAsync(FileService.GetTempDirectory("resolve-action"), DestinationShape.MissingFile,
            configure: journal => journal.Action = action);
        var service = CreateService(seeded.Factory);
        var review = await service.InspectAsync(seeded.OperationId);

        await service.ResolveAsync(seeded.OperationId, review.ReviewToken, Note, true, "tester");

        var row = await LoadJournalAsync(seeded);
        Assert.Equal(FileMutationJournalState.OperatorResolved, row.State);
        Assert.Equal(action, row.Action);
    }

    [Fact]
    public async Task Resolve_ChangesNoDownloadJobHistoryOrOtherJournal_AndDownloadIdIsAGuidString()
    {
        var seeded = await SeedAsync(FileService.GetTempDirectory("resolve-rows"), DestinationShape.MissingFile);
        var downloadId = await SeedDownloadActivityAsync(seeded);
        Assert.True(Guid.TryParse(downloadId, out _));
        var parkedBystander = await SeedBystanderAsync(seeded, FileMutationJournalState.NeedsAttention);
        await SeedBystanderAsync(seeded, FileMutationJournalState.Completed);
        var twin = await SeedTwinAsync(seeded);
        var tablesBefore = await SnapshotAsync(seeded, journalsOnly: false);
        var journalsBefore = await SnapshotAsync(seeded, journalsOnly: true, exceptOperationId: seeded.OperationId);
        Assert.Contains(downloadId, tablesBefore["Downloads"]);
        Assert.Contains(downloadId, tablesBefore["DownloadProcessingJobs"]);
        Assert.Contains(downloadId, tablesBefore["History"]);
        Assert.Contains(parkedBystander.ToString().ToUpperInvariant(), journalsBefore["FileMutationJournals"]);
        Assert.Contains(twin.ToString().ToUpperInvariant(), journalsBefore["FileMutationJournals"]);
        var service = CreateService(seeded.Factory);
        var review = await service.InspectAsync(seeded.OperationId);

        await service.ResolveAsync(seeded.OperationId, review.ReviewToken, Note, true, "tester");

        Assert.Equal(tablesBefore, await SnapshotAsync(seeded, journalsOnly: false));
        Assert.Equal(journalsBefore, await SnapshotAsync(seeded, journalsOnly: true, exceptOperationId: seeded.OperationId));
        await using (var db = seeded.Factory.CreateDbContext())
        {
            var download = await db.Downloads.SingleAsync();
            Assert.Equal(downloadId, download.Id);
            await db.Downloads.ExecuteUpdateAsync(s => s.SetProperty(d => d.Title, "changed"));
        }
        Assert.NotEqual(tablesBefore, await SnapshotAsync(seeded, journalsOnly: false));
    }

    [Fact]
    public async Task OperatorResolvedMoveRow_WithMatchingRegisteredTarget_ProducesNoImportReceipt()
    {
        var resolved = await SeedRegisteredMoveAsync("resolve-receipt", FileMutationJournalState.NeedsAttention);
        var service = CreateService(resolved.Factory);
        var review = await service.InspectAsync(resolved.OperationId);
        await service.ResolveAsync(resolved.OperationId, review.ReviewToken, Note, true, "tester");

        var receipts = await service.ReconcileAudiobookWithReceiptsAsync(resolved.AudiobookId, [resolved.Source]);

        Assert.Empty(receipts);
        Assert.Equal(FileMutationJournalState.OperatorResolved, (await LoadJournalAsync(resolved)).State);
    }

    [Fact]
    public async Task CompletedSourceRetainedMoveRow_WithMatchingRegisteredTarget_ProducesReceipt_Control()
    {
        var control = await SeedRegisteredMoveAsync("resolve-receipt-control", FileMutationJournalState.CompletedSourceRetained);

        var receipt = Assert.Single(await CreateService(control.Factory)
            .ReconcileAudiobookWithReceiptsAsync(control.AudiobookId, [control.Source]));

        Assert.Equal(control.OperationId, receipt.OperationId);
        Assert.True(receipt.SourceRetained);
    }

    private async Task<Seeded> SeedRegisteredMoveAsync(string name, FileMutationJournalState state)
    {
        var seeded = await SeedAsync(FileService.GetTempDirectory(name), DestinationShape.Matching, state,
            journal => { journal.Action = FileAction.Move; journal.ProtocolVersion = FileMutationProtocol.Current; });
        var audiobook = new AudiobookBuilder().WithTitle("A Princess of Mars")
            .WithBasePath(Path.GetDirectoryName(seeded.Destination)!).Build();
        var identity = await _provider.GetRequiredService<IAudiobookFilePathIdentityResolver>()
            .ResolveAsync(audiobook, seeded.Destination);
        Assert.Equal(PathIdentityState.Valid, identity.State);
        var file = AudiobookFile.CreateUnresolved(seeded.Destination);
        file.ApplyPathIdentity(seeded.Destination, identity);
        audiobook.Files = [file];
        await using var db = seeded.Factory.CreateDbContext();
        db.Audiobooks.Add(audiobook);
        await db.SaveChangesAsync();
        await db.FileMutationJournals.ExecuteUpdateAsync(s => s.SetProperty(j => j.AudiobookId, audiobook.Id));
        return seeded with { AudiobookId = audiobook.Id };
    }

    private static async Task<string> SeedDownloadActivityAsync(Seeded seeded)
    {
        var downloadId = Guid.NewGuid().ToString();
        await using var db = seeded.Factory.CreateDbContext();
        db.Downloads.Add(new Download
        {
            Id = downloadId,
            AudiobookId = seeded.AudiobookId,
            Title = "A Princess of Mars",
            DownloadPath = Path.GetDirectoryName(seeded.Source)!,
            FinalPath = Path.GetDirectoryName(seeded.Source)!
        });
        db.DownloadProcessingJobs.Add(new DownloadProcessingJob
        {
            Id = Guid.NewGuid().ToString(),
            DownloadId = downloadId,
            SourcePath = seeded.Source
        });
        db.History.Add(new History
        {
            AudiobookId = seeded.AudiobookId,
            AudiobookTitle = "A Princess of Mars",
            EventType = "Imported",
            DownloadId = downloadId
        });
        await db.SaveChangesAsync();
        return downloadId;
    }

    /// <summary>A parked row identical to the selected one in every column but its operation id.</summary>
    private static async Task<Guid> SeedTwinAsync(Seeded seeded)
    {
        var twin = await LoadJournalAsync(seeded);
        twin.OperationId = Guid.NewGuid();
        await using var db = seeded.Factory.CreateDbContext();
        db.FileMutationJournals.Add(twin);
        await db.SaveChangesAsync();
        return twin.OperationId;
    }

    private static async Task<Guid> SeedBystanderAsync(Seeded seeded, FileMutationJournalState state)
    {
        var operationId = Guid.NewGuid();
        await using var db = seeded.Factory.CreateDbContext();
        db.FileMutationJournals.Add(new FileMutationJournal
        {
            OperationId = operationId,
            ProtocolVersion = FileMutationProtocol.Current,
            Action = FileAction.Move,
            SourcePath = Path.Join(seeded.Root, "source", $"{operationId:N}.m4b"),
            DestinationPath = Path.Join(seeded.Root, "destination", $"{operationId:N}.m4b"),
            AudiobookId = seeded.AudiobookId,
            State = state,
            Error = state == FileMutationJournalState.NeedsAttention ? ParkedError : null,
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        await db.SaveChangesAsync();
        return operationId;
    }
}
