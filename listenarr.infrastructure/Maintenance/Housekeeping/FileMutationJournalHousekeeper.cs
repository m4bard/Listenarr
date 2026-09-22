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

using Listenarr.Domain.Audiobooks.Enumerations;
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
/// AudiobookFileId. Completed registration rows whose audiobook no longer exists, which are
/// unreachable by anything. And Completed registration rows published by Copy, which is the slice
/// of the old population 4 that the two readers keyed on a live audiobook cannot select: the
/// receipts reader is bound to Move by RegistrationMoveOwnerPredicate, and the alias resume in
/// FileMover.Actions is bound to HardlinkCopy.
/// </para>
/// <para>
/// <b>What is still left alone, and why it is not a cutoff problem.</b> Completed with a null
/// AudiobookFileId at Move, and the same at HardlinkCopy. The Move row is the idempotency receipt
/// that turns a repeat import of an already-retired source into a success rather than a missing
/// file, and the HardlinkCopy row is the resume signal whose bare presence is the permission to
/// republish over an existing alias. Neither can be bounded by age, because the key every reader
/// of them uses is content addressed rather than time addressed: the operation ID is a SHA-256 of
/// the audiobook ID, the two path keys and the source proof, and when a source cannot expose a
/// durable object identity that proof degrades to the literal string content-only plus the file's
/// own hash. The same bytes at the same path reproduce the same key, whenever they arrive, so no
/// window closes the read.
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
                        && !context.Audiobooks.Any(audiobook => audiobook.Id == journal.AudiobookId))
                    // 4. Registration publications made by Copy. Of the three readers that can
                    //    still select a Completed registration row, two are bound to an action
                    //    this one is not: the receipts reader through RegistrationMoveOwnerPredicate
                    //    to Move, and the alias resume in FileMover.Actions to HardlinkCopy. The
                    //    third exempts an operation with a journal from the managed-root capability
                    //    gate, and losing that exemption for a completed operation is the safe
                    //    direction, because a repeat of it is a new mutation and belongs behind the
                    //    gate a new mutation faces. A missing row is also re-derivable here in a way
                    //    it is not on the compatibility table: markerless registration reopens both
                    //    endpoints, and an existing destination whose content matches the source
                    //    rejoins the journal at TargetIdentityPersisted rather than being treated
                    //    as an anomaly.
                    || (journal.State == FileMutationJournalState.Completed
                        && journal.AudiobookFileId == null
                        && journal.Action == FileAction.Copy)))
            .OrderBy(journal => journal.UpdatedAt)
            .ThenBy(journal => journal.OperationId);
}
