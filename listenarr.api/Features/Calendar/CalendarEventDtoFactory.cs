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

using System.Globalization;
using Listenarr.Application.Calendar;

namespace Listenarr.Api.Features.Calendar
{
    /// <summary>Maps application calendar events onto the API contract.</summary>
    public static class CalendarEventDtoFactory
    {
        public static CalendarEventDto Create(CalendarEvent calendarEvent)
        {
            ArgumentNullException.ThrowIfNull(calendarEvent);

            return new CalendarEventDto
            {
                AudiobookId = calendarEvent.AudiobookId,
                Title = calendarEvent.Title,
                Authors = calendarEvent.Authors,
                Series = calendarEvent.Series,
                SeriesNumber = calendarEvent.SeriesNumber,
                Genres = calendarEvent.Genres,
                Asin = calendarEvent.Asin,
                ImageUrl = calendarEvent.ImageUrl,
                Runtime = calendarEvent.Runtime,
                ReleaseDate = calendarEvent.ReleaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Monitored = calendarEvent.Monitored,
                HasFile = calendarEvent.HasFile,
                Status = calendarEvent.Status
            };
        }
    }
}
