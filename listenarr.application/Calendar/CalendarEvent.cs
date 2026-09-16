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

namespace Listenarr.Application.Calendar
{
    /// <summary>
    /// A single dated audiobook release, resolved server side so both the calendar API and the
    /// iCalendar feed answer from the same shape.
    /// </summary>
    public sealed class CalendarEvent
    {
        public int AudiobookId { get; init; }
        public string? Title { get; init; }
        public string[]? Authors { get; init; }
        public string? Series { get; init; }
        public string? SeriesNumber { get; init; }
        public string[]? Genres { get; init; }
        public string? Asin { get; init; }
        public string? ImageUrl { get; init; }
        public string? Description { get; init; }
        public int? Runtime { get; init; }

        /// <summary>Release day, normalised to a date with no time component.</summary>
        public DateOnly ReleaseDate { get; init; }

        public bool Monitored { get; init; }
        public bool HasFile { get; init; }

        /// <summary>One of the <see cref="CalendarEventStatus"/> values.</summary>
        public string Status { get; init; } = CalendarEventStatus.Unreleased;

        /// <summary>"Author - Title", the summary line the *arr calendars use.</summary>
        public string DisplayTitle
        {
            get
            {
                var author = Authors is { Length: > 0 } ? Authors[0] : null;
                var title = string.IsNullOrWhiteSpace(Title) ? "Unknown title" : Title;
                return string.IsNullOrWhiteSpace(author) ? title : $"{author} - {title}";
            }
        }
    }
}
