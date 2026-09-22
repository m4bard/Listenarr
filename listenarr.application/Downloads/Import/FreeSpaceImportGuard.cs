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
        IFileSystem fileSystem,
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

            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                return new FreeSpaceCheckResult(true, null, requiredBytes, null);
            }

            var probePath = GetProbePath(destinationPath);

            if (!diskSpaceProbe.TryGetDiskSpace(probePath, out _, out var freeBytes))
            {
                // A network filesystem, or a destination whose every ancestor up to a
                // filesystem root is missing, can make the probe unable to answer. Fail
                // open, same as Readarr's specification: a check that cannot measure
                // anything must not block every import.
                logger.LogDebug(
                    "Free space could not be determined for {Path}; allowing import",
                    LogRedaction.SanitizeFilePath(probePath));
                return new FreeSpaceCheckResult(
                    true,
                    null,
                    requiredBytes,
                    "Free space could not be determined");
            }

            // A stored negative value (never possible through the shipped UI, which clamps
            // to zero, but reachable through a direct API write) must not shrink the
            // required-bytes threshold below the raw file size.
            var minimumBytes = Math.Max(0, settings.MinimumFreeSpaceWhenImporting) * 1024L * 1024L;
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

        // Readarr checks the parent of the author path (the .NET parent-of-path lookup
        // applied to item.Author.Path), not the book folder itself, because the book folder
        // may not exist yet at import time. In Readarr's shallower Author/Book layout that
        // parent is reliably the already-existing root folder. Listenarr's default
        // FolderNamingPattern is {Author}/{Series}/{Title} (ApplicationSettings.cs), so the
        // immediate parent of a new audiobook's BasePath is itself frequently missing too (a
        // new author, or a new series under an existing author): DiskSpaceProbe's underlying
        // probe requires the target folder to already exist on disk, and reports that it
        // could not measure a path that is missing, which would make the guard fail open on
        // exactly the imports it exists to check. Walking up to the nearest ancestor that
        // already exists reaches the same guarantee Readarr's shallower layout gets for free,
        // and terminates at the root folder in the worst case, since a root folder's own
        // existence is enforced when it is configured.
        private string GetProbePath(string destinationPath)
        {
            try
            {
                var current = destinationPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
                current = Path.GetDirectoryName(current) is { } immediateParent
                    && !string.IsNullOrWhiteSpace(immediateParent)
                        ? immediateParent
                        : destinationPath;

                var iterations = 0;
                while (!fileSystem.DirectoryExists(current) && iterations++ < 64)
                {
                    var ancestor = Path.GetDirectoryName(current);
                    if (string.IsNullOrWhiteSpace(ancestor)
                        || string.Equals(ancestor, current, StringComparison.Ordinal))
                    {
                        break;
                    }

                    current = ancestor;
                }

                return current;
            }
            catch (ArgumentException)
            {
                return destinationPath;
            }
        }
    }
}
