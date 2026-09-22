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

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Listenarr.Infrastructure.SystemDiagnostics.Backups
{
    /// <summary>
    /// Names backup archives and recognises the ones Listenarr produced.
    /// </summary>
    /// <remarks>
    /// The shape follows the *arr family so an operator who has restored a Sonarr or Readarr
    /// backup recognises the file at a glance: Readarr writes
    /// <c>readarr_backup_v{version}_{yyyy.MM.dd_HH.mm.ss}.zip</c> and matches it back with an
    /// equivalent regex (src/NzbDrone.Core/Backup/BackupService.cs:42 and :80).
    ///
    /// Recognition matters for more than tidiness: the retention sweep deletes files, so it may
    /// only ever consider files this code is known to have written. Anything an operator dropped
    /// into the directory by hand is left alone.
    /// </remarks>
    public static partial class BackupArchiveNaming
    {
        private const string Prefix = "listenarr_backup";

        /// <summary>The archive extension, including the leading dot.</summary>
        public const string Extension = ".zip";

        /// <summary>
        /// Builds the file name for an archive taken at <paramref name="timestampUtc"/> by a build
        /// reporting <paramref name="version"/>.
        /// </summary>
        public static string BuildFileName(string? version, DateTime timestampUtc)
        {
            var sanitized = SanitizeVersion(version);
            var stamp = timestampUtc.ToString("yyyy.MM.dd_HH.mm.ss", CultureInfo.InvariantCulture);

            return sanitized is null
                ? $"{Prefix}_{stamp}{Extension}"
                : $"{Prefix}_v{sanitized}_{stamp}{Extension}";
        }

        /// <summary>
        /// Returns whether <paramref name="fileName"/> is an archive this code produced.
        /// </summary>
        public static bool IsBackupArchive(string? fileName)
            => !string.IsNullOrEmpty(fileName) && ArchivePattern().IsMatch(fileName);

        /// <summary>
        /// Reduces a version string to characters that are safe in a file name on every supported
        /// platform. Returns <see langword="null"/> when nothing usable survives, so the caller
        /// falls back to an unversioned name rather than emitting an empty <c>v_</c> segment.
        /// </summary>
        private static string? SanitizeVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            var builder = new StringBuilder(version.Length);
            foreach (var character in version)
            {
                if (char.IsAsciiLetterOrDigit(character) || character is '.' or '-')
                {
                    builder.Append(character);
                }
            }

            var sanitized = builder.ToString().Trim('.', '-');
            return sanitized.Length == 0 ? null : sanitized;
        }

        [GeneratedRegex(
            @"^listenarr_backup_(?:v[0-9A-Za-z.\-]+_)?[0-9]{4}\.[0-9]{2}\.[0-9]{2}_[0-9]{2}\.[0-9]{2}\.[0-9]{2}\.zip$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex ArchivePattern();
    }
}
