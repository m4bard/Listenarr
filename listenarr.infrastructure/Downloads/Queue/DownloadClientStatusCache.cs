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

namespace Listenarr.Infrastructure.Downloads.Queue
{
    /// <summary>
    /// Thread-safe, process-wide holder for the latest per-client connectivity
    /// snapshot written by <see cref="QueueMonitorProcessor"/> after each background
    /// poll cycle. Registered as a singleton so both the writer (the queue monitor)
    /// and readers (e.g. the system health endpoint) share the same instance.
    /// </summary>
    public sealed class DownloadClientStatusCache : IDownloadClientStatusCache
    {
        private readonly object _lock = new();
        private IReadOnlyList<QueueClientStatus> _statuses = new List<QueueClientStatus>();

        public void SetStatuses(IReadOnlyList<QueueClientStatus> statuses)
        {
            lock (_lock)
            {
                _statuses = statuses ?? new List<QueueClientStatus>();
            }
        }

        public IReadOnlyList<QueueClientStatus> GetStatuses()
        {
            lock (_lock)
            {
                return _statuses;
            }
        }
    }
}
