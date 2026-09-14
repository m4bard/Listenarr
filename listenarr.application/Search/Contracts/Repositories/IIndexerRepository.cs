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
    public interface IIndexerRepository
    {
        Task<Indexer?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<Indexer?> GetByNameAsync(string name, CancellationToken ct = default);
        Task<List<Indexer>> GetAllAsync(CancellationToken ct = default);
        Task<List<Indexer>> GetEnabledAsync(bool isAutomaticSearch, CancellationToken ct = default);
        Task<Indexer> AddAsync(Indexer indexer, CancellationToken ct = default);
        Task UpdateAsync(Indexer indexer, CancellationToken ct = default);
        Task DeleteAsync(int id, CancellationToken ct = default);

        /// <summary>
        /// Writes only the failure-backoff columns of one indexer, leaving every configuration
        /// column alone. Deliberately not expressed as <see cref="UpdateAsync"/> with a mutated
        /// entity: that path overwrites the whole row from whatever was read, so a status write
        /// built on a stale read would revert a settings edit made in between.
        /// </summary>
        Task UpdateBackoffStateAsync(int indexerId, IndexerBackoffState state, CancellationToken ct = default);
    }
}
