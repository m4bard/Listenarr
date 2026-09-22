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
namespace Listenarr.Domain.SystemDiagnostics.Exceptions
{
    /// <summary>
    /// Thrown when a manual backup is requested and the limit on how many are kept is already met.
    /// </summary>
    /// <remarks>
    /// Refusing is deliberate, and it is the reason manual archives are not swept by count. The
    /// alternative, deleting the oldest to make room, would let anyone who can reach the port
    /// destroy an operator's archives, and on a default install that is anyone on the network,
    /// because Listenarr ships with authentication off. Refusing bounds the disk without giving
    /// away the power to delete: an operator removes archives from the config volume, which is
    /// already mounted, and the promise that nothing removes a manual backup for you stays true.
    /// </remarks>
    public class BackupLimitReachedException : Exception
    {
        public BackupLimitReachedException(int limit)
            : base($"There are already {limit} manual backups, which is the most that are kept. Remove one from the backups directory in the config volume before taking another.")
        {
            Limit = limit;
        }

        /// <summary>The number of manual archives that may exist at once.</summary>
        public int Limit { get; }
    }
}
