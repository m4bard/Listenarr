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

            // A bin that is a filesystem root is never what anyone meant, and the sweep
            // would walk the whole filesystem. The delete path already refuses the
            // equivalent for its own target at
            // AudiobookFilesystemDeleteService.Folders.cs:40.
            if (IsFilesystemRoot(normalizedBin))
            {
                return new RecycleBinPathValidation(
                    RecycleBinPathRejection.FilesystemRoot,
                    "The recycle bin path must not be a filesystem root.");
            }

            // The bin is opened with a no-follow pinned hierarchy walk at recycle time,
            // so any link along the path makes every single delete fail with a message
            // naming an exception type. A layout like /config/recyclebin pointing at
            // /mnt/disk/recyclebin is an ordinary NAS and container arrangement, so this
            // is caught at save time where the operator can act on it.
            if (TryFindSymbolicLinkComponent(normalizedBin, out var linkedComponent))
            {
                return new RecycleBinPathValidation(
                    RecycleBinPathRejection.ContainsSymbolicLink,
                    $"The recycle bin path must not contain a symbolic link, and '{Path.GetFileName(linkedComponent)}' is one. Use the path it points at instead.");
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
            foreach (var file in EnumerateBinFilesWithoutFollowingLinks(
                         normalizedBin,
                         cancellationToken))
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
        /// Walk the bin without ever descending through a link.
        ///
        /// Directory.EnumerateFiles with SearchOption.AllDirectories follows directory
        /// symlinks, so a link inside the bin pointing at a root folder would have the
        /// sweep delete library content. That was measured, not assumed: before this
        /// walk existed, EmptyAsync_BinContainsALinkToTheLibrary_DoesNotFollowIt removed
        /// two files instead of one and took the library file with it.
        ///
        /// A link that is itself a file is still yielded, because deleting it unlinks the
        /// link and leaves its target alone. It is only the recursion that is dangerous.
        /// This mirrors what the library scanner does at
        /// ScanFileDiscovery.Enumeration.cs:142.
        /// </summary>
        private IEnumerable<string> EnumerateBinFilesWithoutFollowingLinks(
            string binRoot,
            CancellationToken cancellationToken)
        {
            var pending = new Stack<string>();
            pending.Push(binRoot);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = pending.Pop();

                string[] entries;
                try
                {
                    entries = Directory.GetFileSystemEntries(directory);
                }
                catch (Exception exception) when (exception is IOException
                    or UnauthorizedAccessException or DirectoryNotFoundException)
                {
                    logger.LogWarning(
                        exception,
                        "Could not read a recycle bin directory during the sweep: {Path}",
                        LogRedaction.SanitizeFilePath(directory));
                    continue;
                }

                foreach (var entry in entries)
                {
                    FileSystemInfo info;
                    try
                    {
                        info = Directory.Exists(entry)
                            ? new DirectoryInfo(entry)
                            : new FileInfo(entry);
                    }
                    catch (Exception exception) when (exception is IOException
                        or UnauthorizedAccessException)
                    {
                        continue;
                    }

                    if (info is DirectoryInfo)
                    {
                        if (info.LinkTarget != null)
                        {
                            logger.LogWarning(
                                "Skipped a linked directory inside the recycle bin rather than sweeping through it: {Path}",
                                LogRedaction.SanitizeFilePath(entry));
                            continue;
                        }

                        pending.Push(entry);
                        continue;
                    }

                    yield return entry;
                }
            }
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
            // Same reason as the file walk: EnumerateDirectories with AllDirectories
            // follows links, and pruning "empty" directories on the far side of one would
            // be reaching into the library.
            var directories = EnumerateBinDirectoriesWithoutFollowingLinks(
                    binRoot,
                    cancellationToken)
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


        private IEnumerable<string> EnumerateBinDirectoriesWithoutFollowingLinks(
            string binRoot,
            CancellationToken cancellationToken)
        {
            var pending = new Stack<string>();
            pending.Push(binRoot);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = pending.Pop();

                string[] children;
                try
                {
                    children = Directory.GetDirectories(directory);
                }
                catch (Exception exception) when (exception is IOException
                    or UnauthorizedAccessException or DirectoryNotFoundException)
                {
                    continue;
                }

                foreach (var child in children)
                {
                    DirectoryInfo info;
                    try
                    {
                        info = new DirectoryInfo(child);
                    }
                    catch (Exception exception) when (exception is IOException
                        or UnauthorizedAccessException)
                    {
                        continue;
                    }

                    // A linked directory is not descended into. It is also not returned,
                    // so the prune never removes a link the operator put there; only
                    // real empty directories the sweep itself created are cleaned up.
                    if (info.LinkTarget != null)
                    {
                        continue;
                    }

                    pending.Push(child);
                    yield return child;
                }
            }
        }

        /// <summary>
        /// Containment test for the bin against a root folder, checked both ordinally and
        /// case-insensitively.
        ///
        /// This is a validator, so it has to fail closed in the direction of REFUSING a
        /// bin path. An ordinal-only test would accept a bin at "/MNT/library/recycled"
        /// against a root of "/mnt/library" on the case-insensitive filesystems Windows
        /// and macOS usually present, and that bin would then be walked by the library
        /// scan and its contents re-imported. Refusing a path that only collides under
        /// case folding costs the operator a rename; accepting one costs them the deletes
        /// the bin was holding.
        ///
        /// Note this is the opposite choice from FileSystemSafety.TryValidateMutationTarget,
        /// which is ordinal on purpose. There, failing closed means refusing to mutate, so
        /// the strict comparison is the safe one. Here it is the loose one.
        /// </summary>


        /// <summary>
        /// Find the first component of the path that exists and is a link. Components
        /// that do not exist yet are fine: the recycle creates them, and it creates real
        /// directories.
        /// </summary>
        private static bool TryFindSymbolicLinkComponent(
            string normalizedPath,
            out string linkedComponent)
        {
            linkedComponent = string.Empty;
            var current = normalizedPath;
            while (!string.IsNullOrEmpty(current))
            {
                try
                {
                    if (Directory.Exists(current)
                        && new DirectoryInfo(current).LinkTarget != null)
                    {
                        linkedComponent = current;
                        return true;
                    }

                    if (File.Exists(current)
                        && new FileInfo(current).LinkTarget != null)
                    {
                        linkedComponent = current;
                        return true;
                    }
                }
                catch (Exception exception) when (exception is IOException
                    or UnauthorizedAccessException)
                {
                    return false;
                }

                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent)
                    || string.Equals(parent, current, StringComparison.Ordinal))
                {
                    return false;
                }

                current = parent;
            }

            return false;
        }

        private static bool IsFilesystemRoot(string normalizedPath)
        {
            var root = Path.GetPathRoot(normalizedPath);
            return !string.IsNullOrEmpty(root)
                && string.Equals(
                    Path.TrimEndingDirectorySeparator(root),
                    Path.TrimEndingDirectorySeparator(normalizedPath),
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWithin(string candidate, string basePath) =>
            IsWithinUsing(candidate, basePath, StringComparison.Ordinal)
            || IsWithinUsing(candidate, basePath, StringComparison.OrdinalIgnoreCase);

        private static bool IsWithinUsing(
            string candidate,
            string basePath,
            StringComparison comparison)
        {
            if (string.Equals(candidate, basePath, comparison))
            {
                return true;
            }

            // The trailing separator is what stops "/a/bc" reading as inside "/a/b".
            // A filesystem root already ends in one, and appending a second would make
            // the boundary "//", which nothing starts with, so every containment test
            // against a root would answer false.
            var boundary = Path.EndsInDirectorySeparator(basePath)
                ? basePath
                : basePath + Path.DirectorySeparatorChar;
            return candidate.StartsWith(boundary, comparison);
        }
    }
}
