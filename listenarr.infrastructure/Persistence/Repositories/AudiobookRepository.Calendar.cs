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
using Listenarr.Application.Calendar;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories;

public partial class AudiobookRepository
{
    public Task<List<CalendarAudiobookRow>> GetCalendarRowsAsync(
        string coarseLowerBound,
        string coarseUpperBound,
        bool includeUnmonitored,
        CancellationToken ct = default)
    {
        var query = _db.Audiobooks
            .AsNoTracking()
            .Where(audiobook =>
                audiobook.PublishedDate != null
                && audiobook.PublishedDate != string.Empty
                && audiobook.PublishedDate.CompareTo(coarseLowerBound) >= 0
                && audiobook.PublishedDate.CompareTo(coarseUpperBound) <= 0);

        if (!includeUnmonitored)
        {
            query = query.Where(audiobook => audiobook.Monitored);
        }

        return query
            .Select(audiobook => new CalendarAudiobookRow
            {
                Id = audiobook.Id,
                Title = audiobook.Title,
                Authors = audiobook.Authors,
                Genres = audiobook.Genres,
                Tags = audiobook.Tags,
                Series = audiobook.Series,
                SeriesNumber = audiobook.SeriesNumber,
                Asin = audiobook.Asin,
                ImageUrl = audiobook.ImageUrl,
                Description = audiobook.Description,
                Runtime = audiobook.Runtime,
                PublishedDate = audiobook.PublishedDate,
                Monitored = audiobook.Monitored,
                FilePath = audiobook.FilePath,
                FileCount = audiobook.Files == null ? 0 : audiobook.Files.Count
            })
            .ToListAsync(ct);
    }
}
