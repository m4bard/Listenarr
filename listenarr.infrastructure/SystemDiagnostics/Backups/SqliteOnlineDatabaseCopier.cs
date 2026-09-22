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

using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Listenarr.Infrastructure.SystemDiagnostics.Backups
{
    /// <summary>
    /// Copies the live SQLite database to a standalone file using SQLite's online backup API.
    /// </summary>
    /// <remarks>
    /// Readarr hand-rolls the same thing at src/NzbDrone.Core/Backup/MakeDatabaseBackup.cs:22.
    /// The point of the online API is that it takes a transactionally consistent copy while the
    /// application keeps writing, so no part of startup or of a manual backup has to stop the app.
    /// </remarks>
    public static class SqliteOnlineDatabaseCopier
    {
        /// <summary>
        /// Copies the database behind <paramref name="sourceConnection"/> to
        /// <paramref name="destinationPath"/>.
        /// </summary>
        /// <param name="sourceConnection">
        /// A connection to the live database. It is opened if necessary and left in the state it
        /// was found in, because it may belong to the running application.
        /// </param>
        /// <param name="destinationPath">Absolute path of the file to write.</param>
        public static void CopyTo(DbConnection sourceConnection, string destinationPath)
        {
            ArgumentNullException.ThrowIfNull(sourceConnection);
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

            if (sourceConnection is not SqliteConnection sqliteSource)
            {
                throw new NotSupportedException(
                    $"Online backup requires a SQLite connection but the context supplied {sourceConnection.GetType().Name}.");
            }

            var openedHere = sqliteSource.State != System.Data.ConnectionState.Open;
            if (openedHere)
            {
                sqliteSource.Open();
            }

            try
            {
                // Pooling is disabled on the destination only. Readarr calls ClearAllPools() at
                // MakeDatabaseBackup.cs:56 for the same reason, but that is process-wide and would
                // disturb the live application's own connections, which we are explicitly not doing.
                var destination = new SqliteConnectionStringBuilder
                {
                    DataSource = destinationPath,
                    Pooling = false
                }.ToString();

                using var destinationConnection = new SqliteConnection(destination);
                destinationConnection.Open();

                sqliteSource.BackupDatabase(destinationConnection);

                // Normalises the journal mode of the copy rather than inheriting whatever the live
                // file carries. Closing the destination cleanly already removes any -wal sidecar,
                // so the archive is self-contained either way; what this buys is that the archived
                // file opens the same way whatever the source was, which matters because the source
                // mode here is not fixed. SqlitePragmaInitializer sets WAL but has no callers, so a
                // database created by this build is in the rollback-journal default and an older
                // one may still be in WAL, since the mode lives in the file header.
                //
                // Readarr normalises for the same reason at MakeDatabaseBackup.cs:35 and :51,
                // choosing truncate and then deleting the journal by hand (BackupService.cs:97);
                // DELETE does both jobs in one pragma.
                using var pragma = destinationConnection.CreateCommand();
                pragma.CommandText = "PRAGMA journal_mode=DELETE;";
                pragma.ExecuteNonQuery();
            }
            finally
            {
                if (openedHere)
                {
                    sqliteSource.Close();
                }
            }
        }
    }
}
