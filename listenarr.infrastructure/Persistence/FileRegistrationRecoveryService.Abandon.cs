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

using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Persistence;

public sealed partial class FileRegistrationRecoveryService
{
    private const string AbandonedReasonPrefix =
        "Abandoned by operator request without verification; this action deleted no source and changed no file.";

    private const int MaxJournalErrorLength = 2048;

    /// <summary>
    /// Moves a NeedsAttention registration publication to CompletedSourceRetained.
    /// That terminal state clears the recovery boundary but never grants source
    /// retirement, and a later import re-verifies any source that is still present.
    /// No file is read, deleted or changed, and no other row is written.
    /// </summary>
    public async Task<FileRegistrationRecoveryStatus> AbandonAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException(
                "A registration recovery abandon requires a non-empty operation ID.",
                nameof(operationId));
        }

        var journal = await LoadRegistrationPublicationAsync(
            operationId,
            cancellationToken);
        if (journal.State != FileMutationJournalState.NeedsAttention)
        {
            return CreateRecoveryStatus(journal);
        }

        var reason = string.IsNullOrWhiteSpace(journal.Error)
            ? AbandonedReasonPrefix
            : $"{AbandonedReasonPrefix} Prior repair reason: {journal.Error}";
        if (reason.Length > MaxJournalErrorLength)
        {
            reason = reason[..MaxJournalErrorLength];
        }

        if (await TryMarkRestartPublicationTerminalAsync(
                operationId,
                FileMutationJournalState.NeedsAttention,
                FileMutationJournalState.CompletedSourceRetained,
                reason,
                cancellationToken))
        {
            logger.LogWarning(
                "File-registration publication {OperationId} for audiobook {AudiobookId} was abandoned by operator request from NeedsAttention; this action deleted no source and changed no file. Prior repair reason: {PriorReason}",
                operationId,
                journal.AudiobookId,
                journal.Error);
        }

        return CreateRecoveryStatus(await LoadRegistrationPublicationAsync(
            operationId,
            cancellationToken));
    }

    private static bool IsAbandonedByOperator(FileMutationJournal journal) =>
        journal.State == FileMutationJournalState.CompletedSourceRetained
        && journal.Error?.StartsWith(AbandonedReasonPrefix, StringComparison.Ordinal) == true;
}
