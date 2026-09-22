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

/// <summary>Shared by both catalogue cache housekeepers; the argument is on each of them.</summary>
internal static class CatalogCacheRetention
{
    internal const int MinimumRetentionDays = 180;
}

/// <summary>
/// Retention for the persisted author catalogue cache.
/// </summary>
/// <remarks>
/// <para>
/// There is no state machine here and nothing to be mid-flight: a row is a copy of what a provider
/// said about an author, every read path returns plain null on a miss with no exception and no
/// negative-cache row written, and nothing anywhere treats the absence of a row as a fact about
/// the author. On the ordinary path a miss costs one live fetch, which is then re-persisted.
/// </para>
/// <para>
/// <b>It is not only a speed-up, and that is why the window is long.</b> When a live refresh comes
/// back with no books at all, <c>AuthorCatalogService</c> deliberately keeps and serves the
/// persisted row rather than reporting an empty catalogue, and logs that it is doing so. The row
/// is therefore the fallback for a provider outage, and deleting it turns a degraded lookup into
/// a failed one.
/// </para>
/// <para>
/// <b>UpdatedAt is last successful fetch, not last use.</b> The upsert is reached only after a
/// fetch, and the cache-hit path returns before it, so an author served from cache every day for
/// a year still carries a year-old <c>UpdatedAt</c>. There is no TTL anywhere to correct that. The
/// better fix is to touch the row on a hit, and it is deliberately not done here: it puts a write
/// on every cache read, which is a behaviour change on a hot path and does not belong in a
/// retention branch.
/// </para>
/// <para>
/// <b>The floor, at six months.</b> Those two facts together mean a short window evicts entries
/// that are in active service, and costs the outage fallback for them. Against that, the table is
/// bounded by distinct authors and series looked at rather than growing with library operations,
/// so aggressive eviction buys very little. Six months keeps the fallback for anything touched
/// anywhere near recently while still bounding the table, and a longer configured window still
/// wins.
/// </para>
/// <para>
/// <b>Age is per row and the sweep is not "keep the newest per key".</b> The unique index is on
/// name and region; ASIN and region is not unique, and a test in this repository exercises two
/// rows sharing an ASIN at different ages and asserts the freshest wins. A per-row cutoff removes
/// the stale duplicate and can never strand a key at zero rows, because the freshest row for a key
/// anybody is still fetching is by definition recently touched.
/// </para>
/// <para>
/// <c>UpdatedAt</c> is used rather than <c>LastFetchedAt</c>, which is nullable and would make a
/// row that has never carried one immortal.
/// </para>
/// </remarks>
public sealed class AuthorCacheHousekeeper(IDbContextFactory<ListenArrDbContext> dbContextFactory)
    : TableHousekeepingTask<AuthorCacheEntry>(dbContextFactory)
{
    public override string Name => "AuthorCacheEntries";

    public override int MinimumRetentionDays => CatalogCacheRetention.MinimumRetentionDays;

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

    public override int MinimumRetentionDays => CatalogCacheRetention.MinimumRetentionDays;

    protected override IQueryable<SeriesCacheEntry> Eligible(
        ListenArrDbContext context,
        HousekeepingCycle cycle) =>
        context.SeriesCacheEntries
            .Where(entry => entry.UpdatedAt < cycle.CutoffUtc)
            .OrderBy(entry => entry.UpdatedAt)
            .ThenBy(entry => entry.Id);
}
