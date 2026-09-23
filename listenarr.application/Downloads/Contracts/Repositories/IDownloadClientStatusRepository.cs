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

namespace Listenarr.Application.Downloads.Contracts.Repositories
{
    /// <summary>
    /// Persistence for <see cref="DownloadClientStatus"/>, one row per client that has failed.
    /// </summary>
    public interface IDownloadClientStatusRepository
    {
        Task<List<DownloadClientStatus>> GetAllAsync(CancellationToken ct = default);

        Task<DownloadClientStatus?> GetByClientIdAsync(string clientId, CancellationToken ct = default);

        /// <summary>
        /// Inserts or replaces the row for <see cref="DownloadClientStatus.ClientId"/>. Returns false
        /// and writes nothing when no such client is saved, which is the case for a connection test
        /// run from the add form before the client exists.
        /// </summary>
        Task<bool> UpsertAsync(DownloadClientStatus status, CancellationToken ct = default);
    }
}
