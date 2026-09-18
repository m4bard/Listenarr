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
    /// not prior art for a per-file completeness ratio, and it is worth writing down why, because
    /// the name invites the assumption that it is.
    ///
    /// The calendar event passes the book's own statistics.percentOfBooks
    /// (frontend/src/Calendar/Events/CalendarEvent.js:67), which is BookFileCount over BookCount
    /// (src/Readarr.Api.V1/Books/BookStatisticsResource.cs:12-23). But in the query behind it
    /// BookCount is a CASE expression yielding 1 or 0, while BookFileCount is a COUNT over the
    /// joined files, grouped per author and per book
    /// (src/NzbDrone.Core/AuthorStats/AuthorStatisticsRepository.cs:58-59, grouped at :64-65).
    /// So for one book the ratio is 0, or 100, or a multiple of 100: one file gives 100 and reads
    /// "downloaded", two files give 200, which is not 100 and is above 0, so it reads "partial".
    /// The state therefore fires when a book has more files than expected rather than fewer. It
    /// is not a measure of completeness, and it carries no design we could copy.
    ///
    /// If Listenarr ever wants a real partial state for a multi-file audiobook it has to be
    /// designed here rather than borrowed, and it would need an expected-file count on
    /// CalendarAudiobookRow, which today carries a file count and nothing to divide it by.
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
        /// The disk-before-download order follows the lineage parent and Sonarr. Readarr returns
        /// 'downloaded' at 100 percent and 'partial' above zero before it ever tests 'downloading'
        /// (readarr frontend/src/Calendar/getStatusStyle.js:7-17); Sonarr tests hasFile first as
        /// well (sonarr frontend/src/Calendar/getStatusStyle.ts:13-19). The observable difference
        /// is a book already on disk with an upgrade in flight: it reads downloaded here, which is
        /// what an operator arriving from either app expects.
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
