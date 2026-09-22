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
/// Retention for the compatibility publication journal, on the one predicate that can be shown to
/// put a row beyond every reader rather than merely beyond a window.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why almost the whole table stays.</b> Deleting a row here is not losing history. The store's
/// GetOrCreate is keyed on the operation ID alone, and on a miss it writes a fresh Planned row
/// rather than re-deriving what is on disk. The very next thing the publication does with a Planned
/// row is refuse when the destination already exists, and that refusal is written as NeedsAttention.
/// NeedsAttention is one way: Advance refuses to leave it, the recovery service skips it, and no UI
/// reads this table at all. So a deleted row whose operation is repeated becomes stuck work nobody
/// can see. That is the trap this housekeeper exists to route around rather than to argue with.
/// </para>
/// <para>
/// <b>Age cannot be the predicate, because the key is content addressed.</b> The operation ID a
/// repeat presents is a SHA-256 over the scope, the audiobook ID, the action, the two path keys and
/// the source proof, and nothing in it moves with the clock. For exactly the storage this table
/// serves, the source proof degrades further: a source that cannot expose a durable object identity
/// is proved by the literal string content-only followed by its own SHA-256, so every input to the
/// key is a function of the audiobook, the paths and the bytes. The same release re-downloaded to
/// the same path reproduces the same key, and there is no bound on when that happens. A ninety day
/// window and a nine year window are equally uninformative about it.
/// </para>
/// <para>
/// <b>What the audiobook ID buys, which is the whole predicate.</b> The audiobook ID is one of the
/// hashed inputs. A row whose audiobook has been deleted can therefore only be selected again if
/// that same integer comes back, and the audiobooks table is declared with the SQLite autoincrement
/// annotation, which is what asks SQLite never to reuse a row ID. So the operation ID on such a row
/// is spent. Nothing else collects these rows either: AudiobookId here is a plain nullable int with
/// no relationship configured, so deleting an audiobook leaves its compatibility journals behind
/// forever.
/// </para>
/// <para>
/// <b>The batch clause, which is not about age.</b> The source cleanup coordinator loads a batch by
/// BatchId with no state filter, and it retains every source unless every row it finds is at
/// RegistrationCommitted. Removing a Completed row from a batch that still has RegistrationCommitted
/// rows in it would therefore not merely lose a row, it would flip that batch from retaining its
/// sources to deleting them. Requiring that no sibling of the same batch sits at
/// RegistrationCommitted closes that without depending on the sweep and an import never overlapping.
/// </para>
/// <para>
/// <b>What the query costs.</b> Unlike the file mutation journal, this table is indexed on State,
/// AudiobookId and BatchId but not on UpdatedAt, so the State index carries the scan and the age
/// comparison is a filter over what it returns. That is the right way round: Completed is the
/// selective term once an install has any history at all, and adding an index for a daily sweep
/// would put a write cost on every publication to save a read cost once a day.
/// </para>
/// <para>
/// <b>The floor.</b> Ninety days, matching the file mutation journal for the same reason: the
/// questions these rows answer arrive with unrelated later requests rather than on a schedule. A
/// longer configured window still wins.
/// </para>
/// </remarks>
public sealed class CompatibilityFilePublicationJournalHousekeeper(
    IDbContextFactory<ListenArrDbContext> dbContextFactory)
    : TableHousekeepingTask<CompatibilityFilePublicationJournal>(dbContextFactory)
{
    private const int CompatibilityJournalMinimumRetentionDays = 90;

    public override string Name => "CompatibilityFilePublicationJournals";

    public override int MinimumRetentionDays => CompatibilityJournalMinimumRetentionDays;

    protected override IQueryable<CompatibilityFilePublicationJournal> Eligible(
        ListenArrDbContext context,
        HousekeepingCycle cycle) =>
        context.CompatibilityFilePublicationJournals
            .Where(journal =>
                journal.UpdatedAt < cycle.CutoffUtc
                && journal.State == CompatibilityFilePublicationState.Completed
                && journal.AudiobookId != null
                // A reused audiobook ID would make this clause match fewer rows rather than more,
                // so it fails towards keeping them.
                && !context.Audiobooks.Any(audiobook => audiobook.Id == journal.AudiobookId)
                && (journal.BatchId == null
                    || !context.CompatibilityFilePublicationJournals.Any(sibling =>
                        sibling.BatchId == journal.BatchId
                        && sibling.State
                            == CompatibilityFilePublicationState.RegistrationCommitted)))
            .OrderBy(journal => journal.UpdatedAt)
            .ThenBy(journal => journal.OperationId);
}
