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
    /// Readarr has a sixth value, "partial", which sits between downloaded and downloading. It is
    /// a per-book figure, not a per-author one: the calendar event passes the book's own
    /// statistics.percentOfBooks (frontend/src/Calendar/Events/CalendarEvent.js:67), which is
    /// BookFileCount over BookCount (src/Readarr.Api.V1/Books/BookStatisticsResource.cs:12-23).
    /// An audiobook split across several files has the same shape, so "partial" would mean
    /// something here. It is not modelled yet only because CalendarAudiobookRow carries a file
    /// count and no expected count, leaving nothing to take the ratio against. Worth revisiting.
    /// </remarks>
    public static class CalendarEventStatus
    {
        public const string Downloading = "downloading";
        public const string Downloaded = "downloaded";
        public const string Unmonitored = "unmonitored";
        public const string Missing = "missing";
        public const string Unreleased = "unreleased";

        /// <summary>
        /// Resolves the event state. What is on disk outranks an in-flight download, which
        /// outranks monitoring, and only then does the release date decide between missing and
        /// unreleased.
        /// </summary>
        /// <remarks>
        /// The disk-before-download order follows the lineage parent and Sonarr, not Radarr.
        /// Readarr returns 'downloaded' at 100 percent and 'partial' above zero before it ever
        /// tests 'downloading' (frontend/src/Calendar/getStatusStyle.js:7-17); Sonarr tests
        /// hasFile first as well (frontend/src/Calendar/getStatusStyle.ts:13-19). Only Radarr
        /// returns the queue state first (frontend/src/Calendar/getStatusStyle.ts:7-13). The
        /// observable difference is a book already on disk with an upgrade in flight: it reads
        /// downloaded here, which is what an operator arriving from Readarr or Sonarr expects.
        /// </remarks>
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
            if (hasFile)
            {
                return Downloaded;
            }

            if (isDownloading)
            {
                return Downloading;
            }

            if (!monitored)
            {
                return Unmonitored;
            }

            return releaseDate > today ? Unreleased : Missing;
        }
    }
}
