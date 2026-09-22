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

namespace Listenarr.Infrastructure.SystemDiagnostics.Backups
{
    /// <summary>
    /// Keeps backup directories and archives readable only by the account Listenarr runs as.
    /// </summary>
    /// <remarks>
    /// An archive concentrates the API key, the SSL certificate password, indexer keys, download
    /// client credentials and the admin password hash into one portable file, which is a different
    /// proposition from the live database sitting at the process umask.
    /// </remarks>
    public static class BackupFilePermissions
    {
        private const UnixFileMode PrivateDirectory =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        private const UnixFileMode PrivateFile =
            UnixFileMode.UserRead | UnixFileMode.UserWrite;

        private const UnixFileMode GroupAndOther =
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

        /// <summary>
        /// Creates <paramref name="path"/> and every level of it below <paramref name="configRoot"/>,
        /// each one private.
        /// </summary>
        /// <remarks>
        /// Every level, because the mode argument to Directory.CreateDirectory applies to the leaf
        /// only: creating config/backups/manual in one call leaves config/backups at the umask.
        /// The config root itself is left alone, since Listenarr does not own what an operator
        /// mounted there.
        /// </remarks>
        public static void CreateProtectedDirectory(string path, string configRoot)
        {
            var levels = new List<string>();

            for (var current = path;
                 current is not null
                    && current.StartsWith(configRoot, StringComparison.Ordinal)
                    && !string.Equals(current, configRoot, StringComparison.Ordinal);
                 current = Path.GetDirectoryName(current))
            {
                levels.Add(current);
            }

            levels.Reverse();

            foreach (var level in levels)
            {
                Directory.CreateDirectory(level);
                Apply(level, PrivateDirectory);
            }
        }

        /// <summary>Drops group and other permissions from a file and confirms they are gone.</summary>
        public static void MakeFilePrivate(string path) => Apply(path, PrivateFile);

        private static void Apply(string path, UnixFileMode mode)
        {
            if (OperatingSystem.IsWindows())
            {
                // Windows inherits the parent ACL and has no mode bits to drop, so an archive there
                // gets whatever the config directory already grants, the same as the database does.
                return;
            }

            File.SetUnixFileMode(path, mode);

            // Read back rather than assumed. This is exactly the kind of call whose behaviour is
            // easy to get wrong, and the cost of being wrong is credentials somewhere readable.
            // Same check as FileSystem/CompatibilitySourceCleanupCoordinator.Quarantine.cs.
            if ((File.GetUnixFileMode(path) & GroupAndOther) != 0)
            {
                throw new UnauthorizedAccessException(
                    $"Backup permissions on '{Path.GetFileName(path)}' could not be made private. "
                    + "Refusing rather than leaving credentials somewhere readable.");
            }
        }
    }
}
