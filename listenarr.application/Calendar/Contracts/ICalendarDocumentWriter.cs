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


namespace Listenarr.Application.Calendar.Contracts
{
    /// <summary>
    /// Serialises calendar events into an iCalendar (RFC 5545) document.
    /// </summary>
    /// <remarks>
    /// The *arr apps do this with Ical.Net. Listenarr carries no iCalendar package, and the events
    /// here are all-day and non-recurring, so the in-repo writer covers the subset in use. This
    /// interface exists so swapping in a library is a one-file change.
    /// </remarks>
    public interface ICalendarDocumentWriter
    {
        /// <summary>Serialises <paramref name="events"/> into a complete VCALENDAR document.</summary>
        /// <param name="events">Events to write, in the order they should appear.</param>
        /// <param name="calendarName">Value for the NAME and X-WR-CALNAME properties.</param>
        string Write(IReadOnlyCollection<CalendarEvent> events, string calendarName);
    }
}
