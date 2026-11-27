using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.FileSystem;

public partial class FileMover
{
    // An interrupted import can leave a verified but unregistered copy that is
    // removed before the retry. Nothing was committed, so publication starts over
    // from the source (whose proof the Planned step re-checks) instead of parking
    // the operation for good. A destination that is present, even if replaced,
    // is never touched here and is judged by the usual verification.
    private async Task<FileMutationJournal> ReplanRemovedVerifiedTargetAsync(
        FileMoveGateLease gate,
        FileMutationJournal journal,
        CancellationToken cancellationToken)
    {
        if (journal.State != FileMutationJournalState.TargetVerified
            || journal.AudiobookId.HasValue
            || journal.AudiobookFileId.HasValue)
        {
            return journal;
        }

        using var currentTarget = gate.DestinationParent.TryOpenExistingFile(
            gate.DestinationName,
            requireDeleteAccess: false);
        if (currentTarget != null)
        {
            return journal;
        }

        _logger.LogInformation(
            "Markerless registration publication {OperationId} lost its unregistered destination; publishing again from the source",
            journal.OperationId);
        return await _fileMutationJournalStore!.ReplanUnownedVerifiedAsync(
            journal.OperationId,
            cancellationToken);
    }
}
