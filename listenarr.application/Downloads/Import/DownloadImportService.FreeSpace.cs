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

namespace Listenarr.Application.Downloads.Import
{
    public partial class DownloadImportService
    {
        /// <summary>
        /// Runs the free-space guard once for the whole import batch, before any file is
        /// copied or moved. Returns null when the import may proceed, or an
        /// <see cref="ImportResult"/> failure per source file when it may not.
        /// </summary>
        private List<ImportResult>? EvaluateFreeSpaceOrNull(
            int audiobookId,
            string destinationBasePath,
            List<string> sourceFiles,
            FileAction completedFileAction,
            ApplicationSettings settings)
        {
            // Measured directly off the files about to be copied or moved, not off any
            // AudiobookFiles row: that DB column is unreliable until upstream #821/#901 land
            // (every row currently records Size = 64, a /proc fd symlink stat rather than the
            // real file). A fresh FileInfo stat here is a different, independent read, so the
            // guard fires correctly regardless of that defect.
            var requiredBytes = sourceFiles.Sum(GetFileSizeOrZero);
            var freeSpaceCheck = freeSpaceImportGuard.Evaluate(
                destinationBasePath,
                requiredBytes,
                settings);
            if (freeSpaceCheck.IsAllowed)
            {
                return null;
            }

            logger.LogWarning(
                "Not enough free space to import audiobook {AudiobookId}: need {RequiredBytes} bytes, {FreeBytes} bytes free at destination",
                audiobookId,
                freeSpaceCheck.RequiredBytes,
                freeSpaceCheck.FreeBytes);

            var failures = new List<ImportResult>(sourceFiles.Count);
            foreach (var file in sourceFiles)
            {
                var failure = ImportResult.ImportFailure(completedFileAction, file, destinationBasePath);
                failure.Message = "Not enough free space";
                failures.Add(failure);
            }

            return failures;
        }

        private static long GetFileSizeOrZero(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Exists ? info.Length : 0;
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException or NotSupportedException
                    or System.Security.SecurityException)
            {
                return 0;
            }
        }
    }
}
