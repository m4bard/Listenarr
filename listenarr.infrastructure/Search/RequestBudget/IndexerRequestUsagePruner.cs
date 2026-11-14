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
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Search.RequestBudget;

/// <summary>
/// Deletes usage rows too old to count, at most once per UTC hour for the process, after a
/// charge has been admitted. Pruning is housekeeping: it runs under no caller's token, and a
/// failure is logged and left for the next hour, so it never changes whether a request goes.
/// </summary>
internal sealed class IndexerRequestUsagePruner
{
    private readonly ILogger _logger;

    // The hour the last prune was claimed for, as ticks so concurrent callers claim it with one
    // exchange: only the caller that swaps a different hour out runs the delete.
    private long _prunedHourTicks;

    public IndexerRequestUsagePruner(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PruneOncePerHourAsync(ListenArrDbContext db, DateTime hourStartUtc)
    {
        if (Interlocked.Exchange(ref _prunedHourTicks, hourStartUtc.Ticks) == hourStartUtc.Ticks)
        {
            return;
        }

        var pruneBeforeUtc = IndexerRequestBudgetPolicy.PruneBeforeUtc(hourStartUtc);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                IndexerRequestBudgetSql.DeleteUsageBefore(pruneBeforeUtc),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not delete indexer request usage older than {PruneBeforeUtc:u}; it is retried next hour",
                pruneBeforeUtc);
        }
    }
}
