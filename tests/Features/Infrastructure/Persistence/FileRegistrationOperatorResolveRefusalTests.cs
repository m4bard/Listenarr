using System.Text.Json;
using Listenarr.Application.Common.Exceptions;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;
using static Listenarr.Tests.Features.Infrastructure.Persistence.FileRegistrationOperatorReviewHarness;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

[Trait("Area", "Library")]
[Trait("Name", "FileRegistrationOperatorResolveRefusalTests")]
[Trait("Category", "Infrastructure")]
public sealed class FileRegistrationOperatorResolveRefusalTests : BaseTests
{
    private const string Note = "Reviewed the retained source and the destination";

    [Theory]
    [InlineData(FileAction.None, null)]
    [InlineData(FileAction.Move, 5)]
    [InlineData(FileAction.Move, 0)]
    [InlineData(FileAction.Move, -1)]
    [InlineData(FileAction.Move, -2)]
    public async Task NonRegistrationJournal_IsRefusedByInspectAndResolve_WithoutWriting(FileAction action, int? audiobookFileId)
    {
        var seeded = await SeedAsync(FileService.GetTempDirectory("refuse-foreign"), DestinationShape.MissingFile,
            configure: journal => { journal.Action = action; journal.AudiobookFileId = audiobookFileId; });
        var before = await LoadJournalAsync(seeded);
        var service = CreateService(seeded.Factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InspectAsync(seeded.OperationId));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ResolveAsync(seeded.OperationId, new string('0', 64), Note, true, "tester"));

        var after = await LoadJournalAsync(seeded);
        Assert.Equal(FileMutationJournalState.NeedsAttention, after.State);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Null(after.ResolutionNote);
    }

    [Fact]
    public async Task UnknownAndEmptyOperationIds_AreNotFound_WithoutWriting()
    {
        var seeded = await SeedAsync(FileService.GetTempDirectory("refuse-unknown"), DestinationShape.MissingFile);
        var service = CreateService(seeded.Factory);
        var token = new string('0', 64);

        foreach (var id in new[] { Guid.NewGuid(), Guid.Empty })
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.InspectAsync(id));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ResolveAsync(id, token, Note, true, "tester"));
        }

        Assert.Equal(FileMutationJournalState.NeedsAttention, (await LoadJournalAsync(seeded)).State);
    }

    [Theory]
    [InlineData(FileMutationJournalState.Planned)]
    [InlineData(FileMutationJournalState.TargetVerified)]
    [InlineData(FileMutationJournalState.RegistrationCommitted)]
    [InlineData(FileMutationJournalState.SourceDeletionAuthorized)]
    [InlineData(FileMutationJournalState.SourceDeleted)]
    [InlineData(FileMutationJournalState.Completed)]
    [InlineData(FileMutationJournalState.RolledBack)]
    [InlineData(FileMutationJournalState.CompletedSourceRetained)]
    public async Task Resolve_RowThatIsNotParked_IsStaleAndWritesNothing(FileMutationJournalState state)
    {
        var seeded = await SeedAsync(FileService.GetTempDirectory("refuse-state"), DestinationShape.MissingFile, state);
        var before = await LoadJournalAsync(seeded);

        var stale = await Assert.ThrowsAsync<ApplicationConflictException>(
            () => CreateService(seeded.Factory).ResolveAsync(seeded.OperationId, new string('0', 64), Note, true, "tester"));

        Assert.Equal("registration_review_stale", stale.Code);
        var after = await LoadJournalAsync(seeded);
        Assert.Equal(state, after.State);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Null(after.ResolutionNote);
        Assert.False((await CreateService(seeded.Factory).InspectAsync(seeded.OperationId)).CanResolve);
    }

    [Theory]
    [InlineData("", "tester", 64, true)]
    [InlineData("   ", "tester", 64, true)]
    [InlineData("note", "", 64, true)]
    [InlineData("note", "tester", 63, true)]
    [InlineData("note", "tester", 65, true)]
    [InlineData("note", "tester", 64, false)]
    public async Task Resolve_InvalidArguments_AreRejectedAndLeaveTheRowResolvable(string note, string actor, int tokenLength, bool acknowledge)
    {
        var seeded = await SeedAsync(FileService.GetTempDirectory("refuse-arguments"), DestinationShape.MissingFile);
        var service = CreateService(seeded.Factory);
        var review = await service.InspectAsync(seeded.OperationId);
        var token = tokenLength == 64 ? review.ReviewToken : new string('0', tokenLength);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ResolveAsync(seeded.OperationId, token, note, acknowledge, actor));

        Assert.Equal(FileMutationJournalState.NeedsAttention, (await LoadJournalAsync(seeded)).State);
        await service.ResolveAsync(seeded.OperationId, review.ReviewToken, "note", true, "tester");
        Assert.Equal(FileMutationJournalState.OperatorResolved, (await LoadJournalAsync(seeded)).State);
    }

    [Theory]
    [InlineData(2000, true)]
    [InlineData(2001, false)]
    public async Task Resolve_NoteLengthBoundary_IsTwoThousandCharacters(int length, bool accepted)
    {
        var seeded = await SeedAsync(FileService.GetTempDirectory("refuse-note-length"), DestinationShape.MissingFile);
        var service = CreateService(seeded.Factory);
        var review = await service.InspectAsync(seeded.OperationId);
        var resolve = () => service.ResolveAsync(seeded.OperationId, review.ReviewToken, new string('n', length), true, "tester");

        if (accepted) await resolve();
        else await Assert.ThrowsAsync<ArgumentException>(resolve);

        Assert.Equal(accepted ? FileMutationJournalState.OperatorResolved : FileMutationJournalState.NeedsAttention,
            (await LoadJournalAsync(seeded)).State);
    }

    [Fact]
    public async Task Resolve_ReplayWithDifferentNoteOrToken_IsStale_AndKeepsFirstResolution()
    {
        var seeded = await SeedAsync(FileService.GetTempDirectory("refuse-replay"), DestinationShape.MissingFile);
        var service = CreateService(seeded.Factory);
        var review = await service.InspectAsync(seeded.OperationId);
        await service.ResolveAsync(seeded.OperationId, review.ReviewToken, Note, true, "first");

        var replay = await service.ResolveAsync(seeded.OperationId, review.ReviewToken, Note, true, "second");
        var otherNote = await Assert.ThrowsAsync<ApplicationConflictException>(
            () => service.ResolveAsync(seeded.OperationId, review.ReviewToken, "different note", true, "second"));
        var otherToken = await Assert.ThrowsAsync<ApplicationConflictException>(
            () => service.ResolveAsync(seeded.OperationId, new string('0', 64), Note, true, "second"));

        Assert.Equal("first", replay.ResolvedBy);
        Assert.Equal("registration_review_stale", otherNote.Code);
        Assert.Equal("registration_review_stale", otherToken.Code);
        var row = await LoadJournalAsync(seeded);
        Assert.Equal("first", row.ResolvedBy);
        Assert.Equal(Note, row.ResolutionNote);
    }

    public enum Race { StateChanged, FileIdAssigned, UpdatedAtBumped, AudiobookReassigned, ErrorChanged, SourceHashChanged, ActionChanged, DestinationPathChanged, SourceLengthChanged, SourcePathChanged, ProtocolVersionChanged }

    [Theory]
    [InlineData(Race.StateChanged)]
    [InlineData(Race.FileIdAssigned)]
    [InlineData(Race.UpdatedAtBumped)]
    [InlineData(Race.AudiobookReassigned)]
    [InlineData(Race.ErrorChanged)]
    [InlineData(Race.SourceHashChanged)]
    [InlineData(Race.ActionChanged)]
    [InlineData(Race.DestinationPathChanged)]
    [InlineData(Race.SourceLengthChanged)]
    [InlineData(Race.SourcePathChanged)]
    [InlineData(Race.ProtocolVersionChanged)]
    public async Task Resolve_RowChangedBetweenInspectionAndWrite_IsRefusedAndDoesNotOverwrite(Race race)
    {
        var root = FileService.GetTempDirectory("refuse-race");
        var plain = SqliteOptions(root);
        var seeded = await SeedAsync(root, DestinationShape.MissingFile, options: plain);
        var interceptor = new ConcurrentWriteBeforeJournalUpdate(plain, ConcurrentWrite(race, seeded.OperationId));
        var racing = new Factory(SqliteOptions(root, interceptor));
        var service = CreateService(seeded.Factory);
        var review = await service.InspectAsync(seeded.OperationId);

        var resolve = () => CreateService(racing).ResolveAsync(seeded.OperationId, review.ReviewToken, Note, true, "tester");
        if (race == Race.FileIdAssigned)
        {
            // The row is no longer a registration publication, so the re-read refuses it instead of reporting it stale.
            await Assert.ThrowsAsync<InvalidOperationException>(resolve);
        }
        else
        {
            Assert.Equal("registration_review_stale", (await Assert.ThrowsAsync<ApplicationConflictException>(resolve)).Code);
        }

        Assert.True(interceptor.Fired);
        var persisted = await LoadJournalAsync(seeded);
        Assert.NotEqual(FileMutationJournalState.OperatorResolved, persisted.State);
        Assert.Null(persisted.ResolutionNote);
        Assert.Null(persisted.ResolvedBy);
        Assert.Null(persisted.ResolutionReviewJson);
    }

    private static Func<ListenArrDbContext, CancellationToken, Task> ConcurrentWrite(Race race, Guid operationId) =>
        (other, token) =>
        {
            var rows = other.FileMutationJournals.Where(j => j.OperationId == operationId);
            return race switch
            {
                Race.StateChanged => rows.ExecuteUpdateAsync(s => s.SetProperty(j => j.State, FileMutationJournalState.RegistrationCommitted), token),
                Race.FileIdAssigned => rows.ExecuteUpdateAsync(s => s.SetProperty(j => j.AudiobookFileId, 7), token),
                Race.UpdatedAtBumped => rows.ExecuteUpdateAsync(s => s.SetProperty(j => j.UpdatedAt, DateTime.UtcNow), token),
                Race.AudiobookReassigned => rows.ExecuteUpdateAsync(s => s.SetProperty(j => j.AudiobookId, 999), token),
                Race.ErrorChanged => rows.ExecuteUpdateAsync(s => s.SetProperty(j => j.Error, "Changed by another writer"), token),
                Race.ActionChanged => rows.ExecuteUpdateAsync(s => s.SetProperty(j => j.Action, FileAction.Copy), token),
                Race.DestinationPathChanged => rows.ExecuteUpdateAsync(s => s.SetProperty(j => j.DestinationPath, "other-destination"), token),
                Race.SourceLengthChanged => rows.ExecuteUpdateAsync(s => s.SetProperty(j => j.SourceLength, j => j.SourceLength + 1), token),
                Race.SourcePathChanged => rows.ExecuteUpdateAsync(s => s.SetProperty(j => j.SourcePath, "other-source"), token),
                Race.ProtocolVersionChanged => rows.ExecuteUpdateAsync(s => s.SetProperty(j => j.ProtocolVersion, j => j.ProtocolVersion + 1), token),
                _ => rows.ExecuteUpdateAsync(s => s.SetProperty(j => j.SourceSha256, new string('0', 64)), token)
            };
        };

    [Fact]
    public async Task Resolve_ConcurrentResolutionWithSameTokenAndNote_IsAcceptedAsReplay()
    {
        var root = FileService.GetTempDirectory("refuse-race-replay");
        var plain = SqliteOptions(root);
        var seeded = await SeedAsync(root, DestinationShape.MissingFile, options: plain);
        var review = await CreateService(seeded.Factory).InspectAsync(seeded.OperationId);
        var interceptor = new ConcurrentWriteBeforeJournalUpdate(plain, (other, token) => other.FileMutationJournals
            .Where(j => j.OperationId == seeded.OperationId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(j => j.State, FileMutationJournalState.OperatorResolved)
                .SetProperty(j => j.ResolutionNote, Note).SetProperty(j => j.ResolvedBy, "winner")
                .SetProperty(j => j.ResolutionReviewJson, JsonSerializer.Serialize(review)), token));
        var racing = new Factory(SqliteOptions(root, interceptor));

        var result = await CreateService(racing).ResolveAsync(seeded.OperationId, review.ReviewToken, Note, true, "loser");

        Assert.True(interceptor.Fired);
        Assert.Equal("OperatorResolved", result.State);
        Assert.Equal("winner", result.ResolvedBy);
        Assert.Equal("winner", (await LoadJournalAsync(seeded)).ResolvedBy);
    }

    [Fact]
    public async Task Resolve_DestinationAppearingAfterInspection_IsStale()
    {
        var seeded = await SeedAsync(FileService.GetTempDirectory("refuse-files"), DestinationShape.MissingFile);
        var service = CreateService(seeded.Factory);
        var review = await service.InspectAsync(seeded.OperationId);
        await File.WriteAllTextAsync(seeded.Destination, "appeared after inspection");

        await Assert.ThrowsAsync<ApplicationConflictException>(
            () => service.ResolveAsync(seeded.OperationId, review.ReviewToken, Note, true, "tester"));

        Assert.Equal(FileMutationJournalState.NeedsAttention, (await LoadJournalAsync(seeded)).State);
        var fresh = await service.InspectAsync(seeded.OperationId);
        Assert.NotEqual(review.ReviewToken, fresh.ReviewToken);
        await service.ResolveAsync(seeded.OperationId, fresh.ReviewToken, Note, true, "tester");
        Assert.Equal(FileMutationJournalState.OperatorResolved, (await LoadJournalAsync(seeded)).State);
    }
}
