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

namespace Listenarr.Api.Features.Calendar
{
    /// <summary>Shared query-parameter handling for the calendar API and the iCalendar feed.</summary>
    public static class CalendarQueryParameters
    {
        /// <summary>
        /// Upper bound on how far a feed may reach in either direction. A subscription is refetched
        /// on the client's own schedule, so an unbounded futureDays would let one bookmark scan the
        /// whole library on a timer.
        /// </summary>
        public const int MaxFeedDays = 3650;

        /// <summary>Splits the comma separated tag list the *arr feeds use.</summary>
        public static IReadOnlyCollection<string> ParseTags(string? tags) =>
            string.IsNullOrWhiteSpace(tags)
                ? Array.Empty<string>()
                : tags
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray();

        /// <summary>Clamps a feed day count into the supported range.</summary>
        public static int ClampFeedDays(int days) => Math.Clamp(days, 0, MaxFeedDays);
    }
}
