using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.FileSystem;

internal sealed partial class EfFileMutationJournalStore
{
    // The one backward transition. A verified publication that no audiobook or file
    // owns has registered nothing, so it may be planned again once its published
    // copy is gone. Any owner, or any other state, keeps the monotonic rule.
    public async Task<FileMutationJournal> ReplanUnownedVerifiedAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var db =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var journal = await db.FileMutationJournals.SingleAsync(
            candidate => candidate.OperationId == operationId,
            cancellationToken);
        if (journal.State != FileMutationJournalState.TargetVerified
            || journal.AudiobookId.HasValue
            || journal.AudiobookFileId.HasValue)
        {
            throw new InvalidOperationException(
                "Only a verified registration publication without an owner can be planned again.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (!db.Database.IsRelational())
        {
            journal.State = FileMutationJournalState.Planned;
            journal.TargetPhysicalObjectIdentity = null;
            journal.Error = null;
            journal.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return journal;
        }

        var affected = await db.FileMutationJournals
            .Where(candidate => candidate.OperationId == operationId
                && candidate.State == FileMutationJournalState.TargetVerified
                && candidate.AudiobookId == null
                && candidate.AudiobookFileId == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        candidate => candidate.State,
                        FileMutationJournalState.Planned)
                    .SetProperty(
                        candidate => candidate.TargetPhysicalObjectIdentity,
                        (string?)null)
                    .SetProperty(
                        candidate => candidate.Error,
                        (string?)null)
                    .SetProperty(
                        candidate => candidate.UpdatedAt,
                        now),
                cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException(
                "The registration publication changed while it was being planned again.");
        }

        db.ChangeTracker.Clear();
        return await db.FileMutationJournals.SingleAsync(
            candidate => candidate.OperationId == operationId,
            cancellationToken);
    }
}
