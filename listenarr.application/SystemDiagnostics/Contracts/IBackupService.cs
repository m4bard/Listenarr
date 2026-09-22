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

using Listenarr.Domain.SystemDiagnostics.Backups;

namespace Listenarr.Application.SystemDiagnostics.Contracts
{
    /// <summary>
    /// Produces and lists backup archives of the Listenarr database and its configuration file.
    /// </summary>
    public interface IBackupService
    {
        /// <summary>
        /// Writes a new backup archive and applies the retention sweep for the given trigger.
        /// </summary>
        /// <param name="trigger">Why the backup is being taken. Determines the subdirectory used.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>Metadata describing the archive that was written.</returns>
        Task<BackupArchive> CreateAsync(BackupTrigger trigger, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists every backup archive currently on disk, newest first.
        /// </summary>
        /// <param name="cancellationToken">Cancels the operation.</param>
        Task<IReadOnlyList<BackupArchive>> ListAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes automatic backups older than the configured retention window and returns how
        /// many were removed. Manual backups are never swept.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="CreateAsync"/> on purpose. Retention reads application
        /// settings, and the migration backup is taken before pending schema changes are applied,
        /// when that read is not safe. Callers sweep once the schema is current.
        /// </remarks>
        /// <param name="cancellationToken">Cancels the operation.</param>
        Task<int> ApplyRetentionAsync(CancellationToken cancellationToken = default);
    }
}
