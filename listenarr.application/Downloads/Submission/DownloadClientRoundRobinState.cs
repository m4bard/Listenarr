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

using System.Collections.Concurrent;

namespace Listenarr.Application.Downloads.Submission
{
    /// <summary>
    /// Remembers the client each protocol last handed a grab to, so that two clients
    /// sharing the lowest priority are used in turn instead of the first one always
    /// winning. Registered as a singleton: the selector itself is scoped, so keeping
    /// the cursor on the selector would reset it on every request.
    /// </summary>
    public sealed class DownloadClientRoundRobinState
    {
        private readonly ConcurrentDictionary<DownloadProtocol, string> _lastUsedByProtocol = new();

        public string? GetLastUsed(DownloadProtocol protocol) =>
            _lastUsedByProtocol.TryGetValue(protocol, out var clientId) ? clientId : null;

        public void SetLastUsed(DownloadProtocol protocol, string clientId) =>
            _lastUsedByProtocol[protocol] = clientId;
    }
}
