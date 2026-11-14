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

namespace Listenarr.Application.Search.Contracts.Repositories
{
    /// <summary>
    /// Persists the request gate's per-indexer state. The gate holds the authoritative copy in
    /// memory and decides every transition; this store only loads the rows at start and writes
    /// the result of each transition.
    /// </summary>
    public interface IIndexerRequestStateStore
    {
        /// <summary>
        /// Every stored row, one per indexer that has state. An indexer without a row has no
        /// cooldown and no recorded failures.
        /// </summary>
        Task<IReadOnlyList<IndexerRequestState>> LoadAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes the whole row for <see cref="IndexerRequestState.IndexerId"/> in one statement,
        /// inserting it or replacing every column of the existing row.
        /// </summary>
        Task UpsertAsync(IndexerRequestState state, CancellationToken cancellationToken = default);

        /// <summary>
        /// Resets the indexer's consecutive-failure count after a success. Writes only the count
        /// and the update time, so a cooldown already in force is left exactly as it was. Does
        /// nothing when the indexer has no row.
        /// </summary>
        Task ClearConsecutiveFailuresAsync(int indexerId, DateTime nowUtc, CancellationToken cancellationToken = default);
    }
}
