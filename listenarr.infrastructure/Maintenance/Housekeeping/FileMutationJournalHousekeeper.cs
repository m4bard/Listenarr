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

using Listenarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Maintenance.Housekeeping;

/// <summary>
/// Retention for the file mutation journal, on a predicate deliberately narrower than "Completed
/// and old".
/// </summary>
/// <remarks>
/// <para>
/// <b>Completed is not terminal here, and that is the whole difficulty.</b> AudiobookFileId is an
/// owner discriminator as well as an optional row id, documented as such on FileMutationOwner. For
/// an owner-bound row, meaning an AudiobookFileId of zero or positive, the terminal state is
/// OwnerMetadataReconciled and Completed means the filesystem mutation is done while the owner's
/// metadata is not yet committed. That row is mid-flight. FileRenameCommitStore loads exactly
/// those rows by OperationId, with no state filter and no age filter, and throws if any is
/// missing, so deleting one aborts a live rename commit. The ternary that encodes the owner
/// classes appears verbatim in five readers, starting at FileRenameRecoveryReconciler.
/// </para>
/// <para>
/// <b>NeedsAttention is never swept, at any age.</b> It is one-way and nothing clears it, the row
/// carries the only diagnosis because Error is written beside the state, and a single one of them
/// throws at startup and puts the application into filesystem_initialization_failed with all
/// filesystem mutations disabled, naming that OperationId as the operator's only handle on the
/// problem. Deleting it would be the one thing that unbricks such an install, which is an
/// operator repair action and not something a scheduled sweep decides on its own.
/// </para>
/// <para>
/// <b>What is left, in descending confidence.</b> OwnerMetadataReconciled at any owner class, the
/// genuinely terminal state that no reader selects. Completed for the two companion classes, which
/// are terminal and invisible to the receipts reader because that reader requires a null
/// AudiobookFileId. And Completed registration rows whose audiobook no longer exists, which are
/// unreachable by anything. The population this leaves alone on purpose is Completed with a null
/// AudiobookFileId and a live audiobook: that row is both the idempotency receipt that makes a
/// repeated import a no-op and the hardlink resume signal where bare presence at any state is the
/// permission to proceed, and the manual-import repeat window is unbounded, so no cutoff bounds
/// the risk. It is left for a follow-up rather than swept on a predicate that cannot be made
/// provably safe.
/// </para>
/// <para>
/// <b>The floor.</b> Ninety days, against the operator's setting, for the same unbounded-window
/// reason: the questions these rows answer are asked by later unrelated requests with no bounded
/// arrival time, so a thirty-day window that suits a regenerable cache does not suit this table. A
/// longer configured window still wins.
/// </para>
/// <para>
/// This is the one candidate table whose sweep query is fully index-supported: it is indexed on
/// both State and UpdatedAt.
/// </para>
/// </remarks>
public sealed class FileMutationJournalHousekeeper(
    IDbContextFactory<ListenArrDbContext> dbContextFactory)
    : TableHousekeepingTask<FileMutationJournal>(dbContextFactory)
{
    /// <summary>
    /// The manual-import repeat window is unbounded, so this floor does not make the population
    /// left out above safe. It bounds the exposure of the three that are.
    /// </summary>
    private const int JournalMinimumRetentionDays = 90;

    public override string Name => "FileMutationJournals";

    public override int MinimumRetentionDays => JournalMinimumRetentionDays;

    protected override IQueryable<FileMutationJournal> Eligible(
        ListenArrDbContext context,
        HousekeepingCycle cycle) =>
        context.FileMutationJournals
            .Where(journal =>
                journal.UpdatedAt < cycle.CutoffUtc
                && (
                    // 1. Genuinely terminal for every owner class.
                    journal.State == FileMutationJournalState.OwnerMetadataReconciled
                    // 2. Companion publications, terminal at Completed and invisible to the
                    //    receipts reader, which requires a null AudiobookFileId.
                    || (journal.State == FileMutationJournalState.Completed
                        && (journal.AudiobookFileId == FileMutationOwner.CompanionFile
                            || journal.AudiobookFileId == FileMutationOwner.RegistrationCompanionFile))
                    // 3. Registration publications whose audiobook is gone. AudiobookId is a plain
                    //    nullable int with no relationship configured, so deleting an audiobook
                    //    leaves its journals behind and nothing else ever collects them. A reused
                    //    id would make this clause match fewer rows rather than more, so it fails
                    //    towards keeping them.
                    || (journal.State == FileMutationJournalState.Completed
                        && journal.AudiobookFileId == null
                        && journal.AudiobookId != null
                        && !context.Audiobooks.Any(audiobook => audiobook.Id == journal.AudiobookId))))
            .OrderBy(journal => journal.UpdatedAt)
            .ThenBy(journal => journal.OperationId);
}
