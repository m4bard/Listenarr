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
    /// Resolves dated audiobook releases for the calendar API and the iCalendar feed.
    /// </summary>
    public interface ICalendarService
    {
        /// <summary>
        /// Returns the releases falling inside <paramref name="window"/>, ordered by release date
        /// and then title.
        /// </summary>
        /// <param name="window">Inclusive day range.</param>
        /// <param name="includeUnmonitored">
        /// When false, unmonitored audiobooks are left out, matching the *arr calendars.
        /// </param>
        /// <param name="tags">
        /// When non-empty, only audiobooks carrying at least one of these tags are returned.
        /// Matching is case insensitive.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
            CalendarWindow window,
            bool includeUnmonitored,
            IReadOnlyCollection<string>? tags = null,
            CancellationToken ct = default);
    }
}
