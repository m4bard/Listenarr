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
    public readonly record struct FreeSpaceCheckResult(
        bool IsAllowed,
        long? FreeBytes,
        long RequiredBytes,
        string? Reason);

    /// <summary>
    /// Guards an import against writing into a destination that does not have enough free
    /// space left for the files about to be copied or moved there.
    ///
    /// Mirrors Readarr's FreeSpaceSpecification
    /// (src/NzbDrone.Core/MediaFiles/BookImport/Specifications/FreeSpaceSpecification.cs:25-60):
    /// free space is measured on the parent of the destination folder, the check compares it
    /// against the size of the files being imported plus a configurable margin
    /// (MinimumFreeSpaceWhenImporting, default 100 MB, ConfigService.cs:195), and the whole
    /// check is skippable (SkipFreeSpaceCheckWhenImporting, default false, ConfigService.cs:188)
    /// because a network filesystem can report free space Listenarr has no way to trust. That
    /// escape hatch is part of the feature, not an optional extra: without it, a false "not
    /// enough space" reading on such storage would block every import with no operator
    /// recourse short of editing the database.
    /// </summary>
    public interface IFreeSpaceImportGuard
    {
        FreeSpaceCheckResult Evaluate(
            string destinationPath,
            long requiredBytes,
            ApplicationSettings settings);
    }

    public class FreeSpaceImportGuard(
        IDiskSpaceProbe diskSpaceProbe,
        ILogger<FreeSpaceImportGuard> logger) : IFreeSpaceImportGuard
    {
        public FreeSpaceCheckResult Evaluate(
            string destinationPath,
            long requiredBytes,
            ApplicationSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            if (settings.SkipFreeSpaceCheckWhenImporting)
            {
                return new FreeSpaceCheckResult(
                    true,
                    null,
                    requiredBytes,
                    "Free space check skipped by configuration");
            }

            if (requiredBytes <= 0 || string.IsNullOrWhiteSpace(destinationPath))
            {
                return new FreeSpaceCheckResult(true, null, requiredBytes, null);
            }

            var probePath = GetProbePath(destinationPath);

            if (!diskSpaceProbe.TryGetDiskSpace(probePath, out _, out var freeBytes))
            {
                // A network filesystem or a not-yet-created destination folder can make the
                // probe unable to answer. Fail open, same as Readarr's specification: a check
                // that cannot measure anything must not block every import.
                logger.LogDebug(
                    "Free space could not be determined for {Path}; allowing import",
                    LogRedaction.SanitizeFilePath(probePath));
                return new FreeSpaceCheckResult(
                    true,
                    null,
                    requiredBytes,
                    "Free space could not be determined");
            }

            var minimumBytes = settings.MinimumFreeSpaceWhenImporting * 1024L * 1024L;
            if (freeBytes < requiredBytes + minimumBytes)
            {
                logger.LogWarning(
                    "Not enough free space to import: {FreeBytes} bytes free at {Path}, need {RequiredBytes} bytes plus a {MinimumMb}MB margin",
                    freeBytes,
                    LogRedaction.SanitizeFilePath(probePath),
                    requiredBytes,
                    settings.MinimumFreeSpaceWhenImporting);
                return new FreeSpaceCheckResult(false, freeBytes, requiredBytes, "Not enough free space");
            }

            return new FreeSpaceCheckResult(true, freeBytes, requiredBytes, null);
        }

        // Readarr checks the parent of the author path, not the book folder itself, because
        // the book folder may not exist yet at import time. The same reasoning applies here:
        // audiobook.BasePath is the destination folder for this book and may be about to be
        // created by the import that is asking the question.
        private static string GetProbePath(string destinationPath)
        {
            try
            {
                var trimmed = destinationPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
                var parent = Path.GetDirectoryName(trimmed);
                return string.IsNullOrWhiteSpace(parent) ? destinationPath : parent;
            }
            catch (ArgumentException)
            {
                return destinationPath;
            }
        }
    }
}
