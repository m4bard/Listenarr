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

        /// <summary>
        /// Splits the comma separated tag list the *arr feeds use, accepting either spelling.
        /// </summary>
        /// <param name="tags">
        /// Sonarr's spelling, verified at
        /// sonarr src/Sonarr.Api.V3/Calendar/CalendarFeedController.cs:31. Preferred when both are
        /// present. Radarr is reported to use the same name; it is not checked out here, so that
        /// is unverified and nothing below depends on it.
        /// </param>
        /// <param name="tagList">
        /// The Readarr spelling (src/Readarr.Api.V1/Calendar/CalendarFeedController.cs:31). Readarr
        /// is the lineage parent, so an operator migrating from it arrives holding a URL spelled
        /// this way, and a tag filter that silently does nothing is worse than one that errors.
        /// Worth knowing: Readarr's own calendar link modal emits tags= while its controller binds
        /// tagList (frontend/src/Calendar/iCal/CalendarLinkModalContent.js:33), so the filter in a
        /// URL Readarr generates for you is ignored by Readarr itself.
        /// </param>
        public static IReadOnlyCollection<string> ParseTags(string? tags, string? tagList = null)
        {
            var value = string.IsNullOrWhiteSpace(tags) ? tagList : tags;

            return string.IsNullOrWhiteSpace(value)
                ? Array.Empty<string>()
                : value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray();
        }

        /// <summary>Clamps a feed day count into the supported range.</summary>
        public static int ClampFeedDays(int days) => Math.Clamp(days, 0, MaxFeedDays);
    }
}
