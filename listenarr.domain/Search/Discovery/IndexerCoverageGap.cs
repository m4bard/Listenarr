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

namespace Listenarr.Domain.Search.Discovery
{
    /// <summary>
    /// A stretch of an indexer's feed that discovery could not read, because more was
    /// published between two polls than the feed or the page bound reached back.
    /// </summary>
    public class IndexerCoverageGap
    {
        public int Id { get; set; }

        public int IndexerId { get; set; }

        /// <summary>
        /// Start of the unread stretch: the checkpoint's publish date, or the last successful
        /// poll's local time on a feed without usable dates.
        /// </summary>
        public DateTime GapStartUtc { get; set; }

        /// <summary>
        /// End of the unread stretch: the oldest usable date fetched, or the local poll time on
        /// a feed without usable dates.
        /// </summary>
        public DateTime GapEndUtc { get; set; }

        public DateTime DetectedUtc { get; set; }

        public DateTime? AcknowledgedUtc { get; set; }
    }
}
