using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.FileSystem;

public partial class FileMover
{
    // An interrupted import can leave an unowned copy (created, or created and
    // verified) that is removed before the retry. The journal has no owner, so no
    // owner commit happened and the source was never retired. A catalog row for
    // the destination path may already exist in a narrow window before the owner
    // is journaled; publishing the same verified bytes to that path again is safe
    // because registration then finds the path already owned by the audiobook and
    // adds nothing. Publication starts over from the source (whose proof the
    // Planned step re-checks) instead of parking the operation for good. A
    // destination that is present, even if replaced or half written, is never
    // touched here and is judged by the usual verification.
    private async Task<FileMutationJournal> ReplanRemovedUnownedTargetAsync(
        FileMoveGateLease gate,
        FileMutationJournal journal,
        CancellationToken cancellationToken)
    {
        if ((journal.State != FileMutationJournalState.TargetVerified
                && journal.State != FileMutationJournalState.TargetIdentityPersisted)
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
            "Markerless registration publication {OperationId} lost its unowned destination; publishing again from the source",
            journal.OperationId);
        return await _fileMutationJournalStore!.ReplanUnownedPublicationAsync(
            journal.OperationId,
            cancellationToken);
    }
}
