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
    /// One release seen on an indexer's feed, stored in the same transaction that moves the
    /// indexer's checkpoint past it, then matched and handled from here.
    /// </summary>
    public class DiscoveredRelease
    {
        public int Id { get; set; }

        public int IndexerId { get; set; }

        /// <summary>
        /// The release's identity as <see cref="ReleaseIdentity"/> derives it. Unique per
        /// indexer, which is what makes a repeated feed item a no-op.
        /// </summary>
        public string ReleaseId { get; set; } = string.Empty;

        /// <summary>The item's publish date when it had one, in the indexer's own clock.</summary>
        public DateTime? PublishedUtc { get; set; }

        public string Title { get; set; } = string.Empty;

        /// <summary>The search result as JSON, so the release can be handed on without a refetch.</summary>
        public string Payload { get; set; } = string.Empty;

        public DiscoveredReleaseState State { get; set; } = DiscoveredReleaseState.Pending;

        public DateTime FirstSeenUtc { get; set; }

        public DateTime? HandledUtc { get; set; }

        /// <summary>
        /// The monitored book the release matched. Cleared if that book is deleted, and the
        /// row is then handled as not needed.
        /// </summary>
        public int? MatchedAudiobookId { get; set; }

        /// <summary>Why the row reached its terminal state, for status and logs only.</summary>
        public string? Outcome { get; set; }
    }
}
