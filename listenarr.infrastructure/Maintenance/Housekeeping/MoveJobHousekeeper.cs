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
/// Retention for finished move jobs and the three tables that hang off them.
/// </summary>
/// <remarks>
/// <para>
/// This is the largest grower of the candidates, and not because of MoveJobs itself. One root
/// folder relocation writes one job per audiobook under the root, so a single operator click on a
/// large library writes thousands of jobs in one transaction, every one of which goes terminal
/// inside that operation. Each job then carries one MoveJobEntry per filesystem object in the
/// audiobook folder plus two synthetic boundary-authorization rows, and MoveJobEntry holds a 2000
/// character relative path and two 512 character identity columns, so the entries table is one to
/// two orders of magnitude larger than its parent and is where the space actually goes.
/// </para>
/// <para>
/// <b>The predicate, and the clause that carries it.</b> A job is eligible when its status is
/// Completed or Superseded, its CompletedAt is past the cutoff, it either has no relocation or
/// its relocation is itself finished, and it either has no scan handoff or its handoff is
/// Succeeded or Superseded.
/// </para>
/// <para>
/// The relocation clause is the important one. RootFolderRelocationService recomputes its
/// completed-job count from the surviving rows and branches on it, so a job set that a sweep has
/// thinned makes the next reconciliation pass reach a different conclusion than the history
/// warrants. The sharp edge is an empty set, where the all-jobs-completed test is vacuously true
/// and the relocation would be finalized or offered for abandonment, which performs filesystem
/// retirement.
/// <para>
/// Note what actually protects against that, because it is not what it looks like. It is not that
/// a sweep cannot thin a relocation's job set partially: the per-cycle ceiling and the batch size
/// mean it certainly can. It is that the only writer of the recomputed count is reachable only
/// from a move job state change, and every job under a finished relocation is already terminal
/// and cannot change state again, so that code is never re-entered for one. Requiring the
/// relocation to be finished, meaning no active root folder and a Completed status, is what buys
/// that.
/// </para>
/// </para>
/// <para>
/// The handoff clause is the second. A Completed job is terminal for recovery and not terminal
/// for the post-move library rescan: the handoff store projects the claimed handoff through the
/// navigation onto MoveJobs and re-reads the job's entries. Deleting a Completed job with a
/// Pending or Claimed handoff cancels that rescan with no error anywhere, and deleting only its
/// entries leaves the handoff alive and permanently undispatchable.
/// </para>
/// <para>
/// Failed and NeedsAttention are not swept at all. Failed is not terminal, since the recovery
/// policy returns RetryAvailable for it and there is a real operator requeue route to it.
/// NeedsAttention is where every automatic retry exhaustion lands and is the operator's work
/// item.
/// </para>
/// <para>
/// <b>Children go first, explicitly, in the same transaction.</b> The three child relationships
/// are configured Cascade, and the database level cascade does fire here: MoveJobHousekeeperTests
/// measures that PRAGMA foreign_keys reports on and that a bare delete of the parent empties all
/// three child tables. The point is that nothing in this repository asks for that. It is the
/// driver's default, and one connection string keyword would turn it off. Deleting the children
/// by MoveJobId before the parents is correct either way, it needs no Include on a navigation
/// configured AutoInclude(false), and it cannot silently orphan the largest table of the three.
/// Being plain about it: with the pragma on, no test here can tell the explicit delete apart from
/// relying on the cascade. It is insurance against that default moving, not something the suite
/// currently catches.
/// </para>
/// <para>
/// <b>Batch size.</b> Smaller than the five hundred the other housekeepers use, because the
/// fan-out is per parent: five hundred parents can be tens of thousands of child rows in one
/// write transaction, which is the thing the batching exists to avoid.
/// </para>
/// <para>
/// Nothing here deletes a RootFolderRelocation. The Restrict between them runs from the job side,
/// so it blocks deleting a relocation while jobs still reference it and places no constraint on
/// deleting a job; if a relocation sweep is ever added it has to run after this one.
/// </para>
/// </remarks>
public sealed class MoveJobHousekeeper(IDbContextFactory<ListenArrDbContext> dbContextFactory)
    : TableHousekeepingTask<MoveJob>(dbContextFactory)
{
    public override string Name => "MoveJobs";

    protected override int DeleteBatchSize => 100;

    protected override IQueryable<MoveJob> Eligible(
        ListenArrDbContext context,
        HousekeepingCycle cycle) =>
        context.MoveJobs
            .Where(job =>
                (job.Status == MoveJobStatus.Completed || job.Status == MoveJobStatus.Superseded)
                && job.CompletedAt != null
                && job.CompletedAt < cycle.CutoffUtc
                && (job.RelocationId == null
                    || context.RootFolderRelocations.Any(relocation =>
                        relocation.Id == job.RelocationId
                        && relocation.ActiveRootFolderId == null
                        && relocation.Status == RootFolderRelocationStatus.Completed))
                && !context.MoveScanHandoffs.Any(handoff =>
                    handoff.MoveJobId == job.Id
                    && handoff.Status != MoveScanHandoffStatus.Succeeded
                    && handoff.Status != MoveScanHandoffStatus.Superseded))
            .OrderBy(job => job.CompletedAt)
            .ThenBy(job => job.Id);

    protected override async Task DeleteBatchAsync(
        ListenArrDbContext context,
        IReadOnlyList<MoveJob> batch,
        CancellationToken cancellationToken)
    {
        var jobIds = batch.Select(job => job.Id).ToList();
        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        context.MoveJobEntries.RemoveRange(
            await context.MoveJobEntries
                .Where(entry => jobIds.Contains(entry.MoveJobId))
                .ToListAsync(cancellationToken));
        context.MoveJobCreatedDirectories.RemoveRange(
            await context.MoveJobCreatedDirectories
                .Where(directory => jobIds.Contains(directory.MoveJobId))
                .ToListAsync(cancellationToken));
        context.MoveScanHandoffs.RemoveRange(
            await context.MoveScanHandoffs
                .Where(handoff => jobIds.Contains(handoff.MoveJobId))
                .ToListAsync(cancellationToken));
        context.MoveJobs.RemoveRange(batch);

        await context.SaveChangesAsync(cancellationToken);
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }
}
