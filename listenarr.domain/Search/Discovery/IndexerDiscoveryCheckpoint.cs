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
    /// How far recent-release discovery has read one indexer's feed. A missing row means the
    /// indexer has never been polled, so its first poll is a baseline and records no gap.
    /// </summary>
    public class IndexerDiscoveryCheckpoint
    {
        public int IndexerId { get; set; }

        /// <summary>
        /// The newest usable publish date seen, in the indexer's own clock. Null while every
        /// item seen so far was undated or dated in the future.
        /// </summary>
        public DateTime? NewestPublishedUtc { get; set; }

        /// <summary>
        /// Release id of the newest item the checkpoint stands on. Used to find the checkpoint
        /// again on a feed without usable dates.
        /// </summary>
        public string? NewestReleaseId { get; set; }

        /// <summary>Local time of the last poll attempt, whatever its outcome.</summary>
        public DateTime? LastPollUtc { get; set; }

        /// <summary>Local time of the last poll that advanced the checkpoint.</summary>
        public DateTime? LastSuccessUtc { get; set; }

        /// <summary>
        /// Running count of items dated later than poll time plus the overlap. They are stored
        /// but never move the checkpoint.
        /// </summary>
        public int FutureDatedItemCount { get; set; }

        /// <summary>Running count of items with no usable publish date.</summary>
        public int UndatedItemCount { get; set; }
    }
}
