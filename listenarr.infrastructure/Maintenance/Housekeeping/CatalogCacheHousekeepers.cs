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
/// Retention for the persisted author catalogue cache.
/// </summary>
/// <remarks>
/// <para>
/// There is no state machine here and nothing to be mid-flight: a row is a copy of what a provider
/// said about an author, and every read path returns plain null on a miss with no exception and no
/// negative-cache row written. <c>AuthorCatalogService</c> falls straight through to a live fetch
/// and re-persists, so the only operator-visible effect of removing a row is one slower lookup the
/// next time that author is asked about. Nothing anywhere treats the absence of a row as a fact
/// about the author.
/// </para>
/// <para>
/// <b>Age is per row and the sweep is not "keep the newest per key".</b> The unique index is on
/// name and region; ASIN and region is not unique, and a test in this repository exercises two
/// rows sharing an ASIN at different ages and asserts the freshest wins. A per-row cutoff removes
/// the stale duplicate and can never strand a key at zero rows, because the freshest row for a key
/// anybody is still asking about is by definition recently touched.
/// </para>
/// <para>
/// Age is measured from <c>UpdatedAt</c>, which every upsert stamps, rather than from
/// <c>LastFetchedAt</c>, which is nullable and would make a row that has never carried one
/// immortal.
/// </para>
/// </remarks>
public sealed class AuthorCacheHousekeeper(IDbContextFactory<ListenArrDbContext> dbContextFactory)
    : TableHousekeepingTask<AuthorCacheEntry>(dbContextFactory)
{
    public override string Name => "AuthorCacheEntries";

    protected override IQueryable<AuthorCacheEntry> Eligible(
        ListenArrDbContext context,
        HousekeepingCycle cycle) =>
        context.AuthorCacheEntries
            .Where(entry => entry.UpdatedAt < cycle.CutoffUtc)
            .OrderBy(entry => entry.UpdatedAt)
            .ThenBy(entry => entry.Id);
}

/// <summary>
/// Retention for the persisted series catalogue cache. The same table shape, the same read paths
/// and the same argument as <see cref="AuthorCacheHousekeeper" />.
/// </summary>
public sealed class SeriesCacheHousekeeper(IDbContextFactory<ListenArrDbContext> dbContextFactory)
    : TableHousekeepingTask<SeriesCacheEntry>(dbContextFactory)
{
    public override string Name => "SeriesCacheEntries";

    protected override IQueryable<SeriesCacheEntry> Eligible(
        ListenArrDbContext context,
        HousekeepingCycle cycle) =>
        context.SeriesCacheEntries
            .Where(entry => entry.UpdatedAt < cycle.CutoffUtc)
            .OrderBy(entry => entry.UpdatedAt)
            .ThenBy(entry => entry.Id);
}
