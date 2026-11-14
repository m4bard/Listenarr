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
    /// The request gate's persisted state for one indexer: its consecutive-failure count, any
    /// cooldown in force, and when a request was last dispatched to it.
    ///
    /// Kept in its own table rather than on <see cref="Indexer"/>, because the indexer row is
    /// rewritten whole whenever its settings are saved, which would race the gate's writes. An
    /// indexer with no row has no cooldown and no recorded failures.
    /// </summary>
    public class IndexerRequestState
    {
        /// <summary>
        /// The indexer this state belongs to. Also the primary key: there is at most one row per
        /// indexer, and it is deleted with the indexer.
        /// </summary>
        public int IndexerId { get; set; }

        /// <summary>
        /// Failed requests in a row since the last success. A success resets it to zero.
        /// </summary>
        public int ConsecutiveFailures { get; set; }

        /// <summary>
        /// While this is in the future, requests to the indexer are deferred without being sent.
        /// Null when no cooldown has been set.
        /// </summary>
        public DateTime? CooldownUntilUtc { get; set; }

        /// <summary>
        /// The name of the <see cref="RequestDeferralReason"/> that set the cooldown, returned as
        /// the reason of every deferral the cooldown causes.
        /// </summary>
        public string? CooldownReason { get; set; }

        /// <summary>
        /// When a request to the indexer was last dispatched, as of the last state write. Seeds
        /// pacing after a restart.
        /// </summary>
        public DateTime? LastDispatchUtc { get; set; }

        public DateTime UpdatedUtc { get; set; }
    }
}
