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

namespace Listenarr.Application.Downloads.Contracts
{
    /// <summary>
    /// One client's failure status as read for display, with whether it is blocked right now
    /// worked out against the service's clock.
    /// </summary>
    public sealed record DownloadClientStatusSnapshot(
        string ClientId,
        int EscalationLevel,
        DateTime? InitialFailure,
        DateTime? MostRecentFailure,
        DateTime? DisabledTill,
        bool IsBlocked);

    /// <summary>
    /// Per-client failure backoff for download clients: which clients selection should avoid, and
    /// the recording of each call's outcome that moves a client on and off the ladder. The
    /// counterpart of Readarr's IDownloadClientStatusService.
    /// </summary>
    public interface IDownloadClientStatusService
    {
        /// <summary>Ids of every client whose block has not yet expired.</summary>
        Task<IReadOnlySet<string>> GetBlockedClientIdsAsync(CancellationToken ct = default);

        /// <summary>Every recorded status, for display.</summary>
        Task<IReadOnlyList<DownloadClientStatusSnapshot>> GetStatusesAsync(CancellationToken ct = default);

        /// <summary>The client answered. Walks it one rung down the ladder.</summary>
        Task RecordSuccessAsync(string clientId, CancellationToken ct = default);

        /// <summary>The client could not do its job. Walks it up the ladder.</summary>
        Task RecordFailureAsync(string clientId, CancellationToken ct = default);
    }
}
