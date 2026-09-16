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
    /// Calendar event state, using the vocabulary the *arr calendar legends already use so an
    /// operator arriving from Sonarr or Readarr reads the same words.
    /// </summary>
    /// <remarks>
    /// Readarr resolves these client side in frontend/src/Calendar/getStatusStyle.js. Resolving
    /// them here instead is what lets the iCalendar feed carry the state as well, and saves the
    /// calendar page from loading the whole library to colour a cell.
    ///
    /// Readarr's sixth value, "partial", is a percentage across an author or series rather than a
    /// property of one book, so it has no per-event meaning and is not modelled.
    /// </remarks>
    public static class CalendarEventStatus
    {
        public const string Downloading = "downloading";
        public const string Downloaded = "downloaded";
        public const string Unmonitored = "unmonitored";
        public const string Missing = "missing";
        public const string Unreleased = "unreleased";

        /// <summary>
        /// Resolves the event state. Precedence matches Readarr's getStatusStyle: an in-flight
        /// download outranks what is on disk, which outranks monitoring, and only then does the
        /// release date decide between missing and unreleased.
        /// </summary>
        /// <param name="isDownloading">An active download row references this audiobook.</param>
        /// <param name="hasFile">Tracked files exist, or the legacy single-file path is set.</param>
        /// <param name="monitored">The audiobook is monitored.</param>
        /// <param name="releaseDate">The release day.</param>
        /// <param name="today">The day to compare the release against.</param>
        public static string Compute(
            bool isDownloading,
            bool hasFile,
            bool monitored,
            DateOnly releaseDate,
            DateOnly today)
        {
            if (isDownloading)
            {
                return Downloading;
            }

            if (hasFile)
            {
                return Downloaded;
            }

            if (!monitored)
            {
                return Unmonitored;
            }

            return releaseDate > today ? Unreleased : Missing;
        }
    }
}
