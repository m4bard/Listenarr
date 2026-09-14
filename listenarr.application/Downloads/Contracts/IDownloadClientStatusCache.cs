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
    /// Holds the most recent per-client connectivity snapshot produced by the
    /// background download-queue poller (<see cref="QueueSnapshot.Clients"/>).
    /// </summary>
    /// <remarks>
    /// Consumers that only need to know whether a download client is currently
    /// reachable (e.g. the system health endpoint) read from here instead of
    /// triggering their own live probe. The queue poller already probes every
    /// enabled client on a short interval with a bounded per-client timeout, so
    /// re-using its result avoids making an unrelated status check slow, or
    /// capable of hanging on an unreachable client.
    /// </remarks>
    public interface IDownloadClientStatusCache
    {
        /// <summary>Replaces the cached statuses with the latest poll result.</summary>
        void SetStatuses(IReadOnlyList<QueueClientStatus> statuses);

        /// <summary>
        /// The most recently recorded statuses, or an empty list if the poller has
        /// not completed a cycle yet.
        /// </summary>
        IReadOnlyList<QueueClientStatus> GetStatuses();
    }
}
