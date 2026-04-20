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
using static Listenarr.Api.Services.FileMover;

namespace Listenarr.Api.Services
{
    public interface IFileMover
    {
        Task<bool> MoveFileAsync(string sourceFile, string destFile);
        Task<bool> CopyFileAsync(string sourceFile, string destFile);
        Task<bool> HardlinkFileAsync(string sourceFile, string destFile);
        Task<bool> MoveDirectoryAsync(string sourceDir, string destDir);
        Task<bool> CopyDirectoryAsync(string sourceDir, string destDir);

        /// <summary>
        /// Perform the given action on the given file
        /// </summary>
        /// <param name="action">What we want to do with the file</param>
        /// <param name="source">File</param>
        /// <param name="destination">Optional destination of the action</param>
        /// <param name="usedDestinations">List of file name already assigned during a given operation to make sure no collision happens while creating multiple files at the same time</param>
        Task PerformActionOn(FileAction action, string source, string? destination = null, HashSet<string>? usedDestinations = null);
    }
}
