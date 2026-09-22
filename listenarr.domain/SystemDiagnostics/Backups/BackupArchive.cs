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

namespace Listenarr.Domain.SystemDiagnostics.Backups
{
    /// <summary>
    /// Metadata describing one backup archive on disk.
    /// </summary>
    /// <remarks>
    /// This type is serialised straight onto the API. It deliberately carries no filesystem path:
    /// the archive contains the database and config.json, which hold indexer keys, download client
    /// credentials and the API key, so neither the bytes nor their location are exposed over HTTP.
    /// </remarks>
    public sealed record BackupArchive
    {
        /// <summary>The archive file name, without any directory component.</summary>
        public required string Name { get; init; }

        /// <summary>Why the archive was produced.</summary>
        public required BackupTrigger Trigger { get; init; }

        /// <summary>Size of the archive in bytes.</summary>
        public required long SizeBytes { get; init; }

        /// <summary>When the archive was written, in UTC.</summary>
        public required DateTime CreatedAtUtc { get; init; }
    }
}
