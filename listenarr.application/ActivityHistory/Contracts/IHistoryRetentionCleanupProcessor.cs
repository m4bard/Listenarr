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

namespace Listenarr.Application.ActivityHistory.Contracts
{
    /// <summary>
    /// Runs one retention-cleanup cycle for history entries, using the configured
    /// <c>HistoryRetentionDays</c> application setting.
    /// </summary>
    public interface IHistoryRetentionCleanupProcessor
    {
        /// <summary>
        /// Deletes history entries older than the configured retention window.
        /// A retention value of zero (or less) means unlimited retention, so the
        /// cycle is a no-op.
        /// </summary>
        Task RunCycleAsync(CancellationToken cancellationToken = default);
    }
}
