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

using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Downloads.Common
{
    /// <summary>
    /// Shape only: the escalation ladder is not implemented yet.
    /// </summary>
    public sealed class DownloadClientStatusService(
        IDownloadClientStatusRepository repository,
        DownloadClientBackoffStartupWindow startupWindow,
        TimeProvider timeProvider,
        ILogger<DownloadClientStatusService> logger) : IDownloadClientStatusService
    {
        /// <summary>Highest rung a download client reaches.</summary>
        public const int MaximumEscalationLevel = 5;

        public Task<IReadOnlySet<string>> GetBlockedClientIdsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<IReadOnlyList<DownloadClientStatusSnapshot>> GetStatusesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DownloadClientStatusSnapshot>>([]);

        public Task RecordSuccessAsync(string clientId, CancellationToken ct = default) => Task.CompletedTask;

        public Task RecordFailureAsync(string clientId, CancellationToken ct = default)
        {
            _ = (repository, startupWindow, timeProvider, logger);
            return Task.CompletedTask;
        }
    }
}
