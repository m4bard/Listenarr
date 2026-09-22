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
/// One table's retention, with the counting, the ceiling, the preview and the batching done once.
/// A housekeeper supplies its name, its floor and its predicate, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Materialise then RemoveRange, not ExecuteDeleteAsync.</b> This is the shape the existing
/// retention sweep in this repository already uses, at
/// <c>Persistence/Repositories/EfDownloadProcessingJobRepository.DeleteCompletedBeforeAsync</c>,
/// down to the batch size. It earns that on four counts. It gives the deleted count for free
/// rather than trusting a returned row count. It keeps each write transaction to one batch, which
/// matters because there is no retry policy and no busy_timeout anywhere in this codebase, so a
/// long write transaction against the SQLite file is felt as SQLITE_BUSY by whatever else is
/// serving the application. It respects the cascade EF is configured with rather than the
/// database level one, which is worth not depending on: nothing in this repository sets PRAGMA
/// foreign_keys, so whether a bare DELETE cascades is whatever the driver defaults to, and
/// MoveJobHousekeeperTests measures that it currently defaults to on. And it behaves identically
/// on the EF in-memory
/// provider that most of the suite runs on, where ExecuteDeleteAsync simply throws, so a test and
/// the shipped code are not two different mechanisms wearing the same name.
/// </para>
/// <para>
/// <b>The preview counts and stops.</b> It issues the COUNT and nothing else, so a previewing
/// install is repeatable by construction and "it changed nothing" is a claim about the database
/// rather than about one flag.
/// </para>
/// </remarks>
public abstract class TableHousekeepingTask<TEntity>(
    IDbContextFactory<ListenArrDbContext> dbContextFactory) : IHousekeepingTask
    where TEntity : class
{
    /// <summary>
    /// Rows per write transaction. Five hundred is the existing retention sweep's batch size,
    /// reused rather than re-argued. A housekeeper whose table has a large per-row fan-out into
    /// child tables lowers it, because what the batching bounds is the size of one transaction
    /// and not the number of parents in it.
    /// </summary>
    protected virtual int DeleteBatchSize => 500;

    public abstract string Name { get; }

    public virtual int MinimumRetentionDays => 0;

    /// <summary>
    /// The rows this housekeeper considers eligible, ordered oldest first so a cycle that hits
    /// the ceiling takes the rows that have been terminal longest and leaves the freshest for
    /// next time.
    /// </summary>
    protected abstract IQueryable<TEntity> Eligible(
        ListenArrDbContext context,
        HousekeepingCycle cycle);

    public async Task<HousekeepingTaskOutcome> RunAsync(
        HousekeepingCycle cycle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var matched = await Eligible(context, cycle).CountAsync(cancellationToken);
        if (matched == 0)
        {
            return HousekeepingTaskOutcome.Nothing;
        }

        var ceilingReached = matched > cycle.MaxRowsPerTask;
        var budget = Math.Min(matched, cycle.MaxRowsPerTask);
        if (cycle.DryRun)
        {
            return new HousekeepingTaskOutcome(matched, budget, ceilingReached);
        }

        var deleted = 0;
        while (deleted < budget)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = await Eligible(context, cycle)
                .Take(Math.Min(DeleteBatchSize, budget - deleted))
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            await DeleteBatchAsync(context, batch, cancellationToken);
            deleted += batch.Count;
            context.ChangeTracker.Clear();
        }

        return new HousekeepingTaskOutcome(matched, deleted, ceilingReached);
    }

    /// <summary>
    /// Removes one batch. Overridden by a housekeeper whose table has children that have to go in
    /// the same transaction rather than being left to a cascade nothing has confirmed is enforced.
    /// </summary>
    protected virtual async Task DeleteBatchAsync(
        ListenArrDbContext context,
        IReadOnlyList<TEntity> batch,
        CancellationToken cancellationToken)
    {
        context.Set<TEntity>().RemoveRange(batch);
        await context.SaveChangesAsync(cancellationToken);
    }
}
