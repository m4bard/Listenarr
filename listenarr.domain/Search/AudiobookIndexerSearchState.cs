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

namespace Listenarr.Domain.Search
{
    /// <summary>
    /// The automatic search's progress for one book on one indexer. A book with no row for an
    /// indexer is due on it; a row records when that indexer may be asked again and how its
    /// recent searches for the book ended, so a restart or a partial outage resumes per indexer
    /// instead of repeating every indexer for the book.
    /// </summary>
    public class AudiobookIndexerSearchState
    {
        public int AudiobookId { get; set; }

        public int IndexerId { get; set; }

        /// <summary>When this indexer may next be searched for this book.</summary>
        public DateTime NextAttemptUtc { get; set; }

        /// <summary>Consecutive searches that completed without an acceptable result.</summary>
        public int NoResultCount { get; set; }

        /// <summary>Consecutive searches that failed (transport, status, parse or timeout).</summary>
        public int FailureCount { get; set; }

        /// <summary>How the most recent search on this indexer ended.</summary>
        public string LastOutcome { get; set; } = string.Empty;

        /// <summary>When a search on this indexer last completed for this book, if ever.</summary>
        public DateTime? LastCompletedUtc { get; set; }
    }
}
