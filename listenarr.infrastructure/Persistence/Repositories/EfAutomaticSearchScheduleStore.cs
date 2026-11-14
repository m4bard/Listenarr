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
using Listenarr.Application.Search.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories;

public sealed class EfAutomaticSearchScheduleStore(ListenArrDbContext db) : IAutomaticSearchScheduleStore
{
    // The first two terms are the filter of IX_Audiobooks_AutomaticSearchDue in the form EF
    // emits them; with the due range and the ordering below, SQLite reads the page straight off
    // that index and stops at the limit.
    public async Task<IReadOnlyList<Audiobook>> GetDuePageAsync(
        DateTime nowUtc,
        IReadOnlyCollection<int> excludedIds,
        int pageSize,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(excludedIds);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        return await db.Audiobooks
            .AsNoTracking()
            .Include(book => book.QualityProfile)
            .Where(book => book.Monitored && book.QualityProfileId != null)
            .Where(book => book.AutomaticSearchDueUtc <= nowUtc)
            .Where(book => !excludedIds.Contains(book.Id))
            .OrderBy(book => book.AutomaticSearchDueUtc)
            .ThenBy(book => book.Id)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AudiobookIndexerSearchState>> GetPairStatesAsync(
        IReadOnlyCollection<int> audiobookIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(audiobookIds);
        if (audiobookIds.Count == 0)
        {
            return [];
        }

        return await db.AudiobookIndexerSearchStates
            .AsNoTracking()
            .Where(state => audiobookIds.Contains(state.AudiobookId))
            .ToListAsync(ct);
    }
}
