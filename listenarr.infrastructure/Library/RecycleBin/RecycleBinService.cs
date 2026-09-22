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

using Listenarr.Application.Library.RecycleBin;
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.RecycleBin
{
    public sealed class RecycleBinService(
        IConfigurationService configurationService,
        IRootFolderService rootFolderService,
        TimeProvider timeProvider,
        ILogger<RecycleBinService> logger) : IRecycleBinService
    {
        public async Task<RecycleBinPathValidation> ValidatePathAsync(
            string? recycleBinPath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(recycleBinPath))
            {
                return RecycleBinPathValidation.Valid;
            }

            if (!Path.IsPathFullyQualified(recycleBinPath))
            {
                return new RecycleBinPathValidation(
                    RecycleBinPathRejection.NotAbsolute,
                    "The recycle bin path must be an absolute path.");
            }

            string normalizedBin;
            try
            {
                normalizedBin = Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(recycleBinPath));
            }
            catch (Exception exception) when (exception is ArgumentException
                or NotSupportedException or PathTooLongException)
            {
                return new RecycleBinPathValidation(
                    RecycleBinPathRejection.Unusable,
                    "The recycle bin path could not be read as a filesystem path.");
            }

            var roots = await rootFolderService.GetAllAsync();
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root?.Path)
                    || !Path.IsPathFullyQualified(root.Path))
                {
                    continue;
                }

                var normalizedRoot = Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(root.Path));

                // A bin inside a root is the dangerous case. The library scanner walks
                // every root, so recycled files would be discovered and re-imported, which
                // would undo the deletion the operator asked for.
                if (IsWithin(normalizedBin, normalizedRoot))
                {
                    return new RecycleBinPathValidation(
                        RecycleBinPathRejection.InsideRootFolder,
                        "The recycle bin must sit outside every root folder, otherwise the library scan would find the recycled files and import them again.");
                }

                // The reverse also has to be refused: emptying a bin that contains a root
                // folder would delete the library.
                if (IsWithin(normalizedRoot, normalizedBin))
                {
                    return new RecycleBinPathValidation(
                        RecycleBinPathRejection.ContainsRootFolder,
                        "The recycle bin must not contain a root folder, otherwise emptying it would delete the library.");
                }
            }

            return RecycleBinPathValidation.Valid;
        }

        public Task<RecycleBinSweepResult> EmptyAsync(
            CancellationToken cancellationToken = default) =>
            SweepAsync(olderThanUtc: null, cancellationToken);

        public async Task<RecycleBinSweepResult> CleanupAsync(
            CancellationToken cancellationToken = default)
        {
            var settings = await configurationService.GetApplicationSettingsAsync();
            var retentionDays = settings?.RecycleBinCleanupDays ?? 0;
            if (retentionDays <= 0)
            {
                // Zero means keep until emptied by hand, so the sweep is a no-op rather
                // than an immediate purge. Getting this backwards would delete the bin's
                // whole contents on the first cycle after an upgrade.
                return RecycleBinSweepResult.Empty;
            }

            var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-retentionDays);
            return await SweepAsync(cutoff, cancellationToken);
        }

        private async Task<RecycleBinSweepResult> SweepAsync(
            DateTime? olderThanUtc,
            CancellationToken cancellationToken)
        {
            var settings = await configurationService.GetApplicationSettingsAsync();
            var binPath = settings?.RecycleBinPath;
            if (string.IsNullOrWhiteSpace(binPath))
            {
                return RecycleBinSweepResult.Empty;
            }

            // Revalidating on every sweep is deliberate. The bin path and the root folders
            // are edited independently, so a bin that was outside every root when it was
            // saved can end up inside one later. A sweep that ran anyway would delete
            // library content.
            var validation = await ValidatePathAsync(binPath, cancellationToken);
            if (!validation.IsValid)
            {
                logger.LogWarning(
                    "Skipped the recycle bin sweep because the configured path is no longer valid: {Reason}",
                    validation.Message);
                return RecycleBinSweepResult.Empty;
            }

            var normalizedBin = Path.TrimEndingDirectorySeparator(Path.GetFullPath(binPath));
            if (!Directory.Exists(normalizedBin))
            {
                return RecycleBinSweepResult.Empty;
            }

            var filesRemoved = 0;
            foreach (var file in Directory.EnumerateFiles(
                         normalizedBin,
                         "*",
                         SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (olderThanUtc.HasValue
                        && File.GetLastWriteTimeUtc(file) > olderThanUtc.Value)
                    {
                        continue;
                    }

                    File.Delete(file);
                    filesRemoved++;
                }
                catch (Exception exception) when (exception is IOException
                    or UnauthorizedAccessException)
                {
                    logger.LogWarning(
                        exception,
                        "Could not remove a recycled file during the recycle bin sweep: {Path}",
                        LogRedaction.SanitizeFilePath(file));
                }
            }

            var directoriesRemoved = RemoveEmptyDirectories(normalizedBin, cancellationToken);
            if (filesRemoved > 0 || directoriesRemoved > 0)
            {
                logger.LogInformation(
                    "Recycle bin sweep removed {FileCount} files and {DirectoryCount} empty directories",
                    filesRemoved,
                    directoriesRemoved);
            }

            return new RecycleBinSweepResult(filesRemoved, directoriesRemoved);
        }

        /// <summary>
        /// Prune directories the sweep emptied, deepest first, without ever removing the
        /// bin root itself. Leaving the root in place keeps the configured path valid for
        /// the next delete.
        /// </summary>
        private int RemoveEmptyDirectories(
            string binRoot,
            CancellationToken cancellationToken)
        {
            var removed = 0;
            var directories = Directory
                .EnumerateDirectories(binRoot, "*", SearchOption.AllDirectories)
                .OrderByDescending(path => path.Length)
                .ToList();

            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        continue;
                    }

                    Directory.Delete(directory, recursive: false);
                    removed++;
                }
                catch (Exception exception) when (exception is IOException
                    or UnauthorizedAccessException or DirectoryNotFoundException)
                {
                    logger.LogDebug(
                        exception,
                        "Could not remove an empty recycle bin directory: {Path}",
                        LogRedaction.SanitizeFilePath(directory));
                }
            }

            return removed;
        }

        private static bool IsWithin(string candidate, string basePath)
        {
            if (string.Equals(candidate, basePath, StringComparison.Ordinal))
            {
                return true;
            }

            var boundary = basePath + Path.DirectorySeparatorChar;
            return candidate.StartsWith(boundary, StringComparison.Ordinal);
        }
    }
}
