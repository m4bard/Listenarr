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

using Listenarr.Domain.Common;
using Listenarr.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Moving
{
    public sealed partial class AudiobookFilesystemDeleteService
    {
        /// <summary>
        /// The recycle bin configuration for one delete, resolved once so that a settings
        /// change part-way through a multi-file delete cannot send half an audiobook to the
        /// bin and unlink the other half.
        /// </summary>
        private sealed record RecycleBinPolicy(
            string BinPath,
            IReadOnlyCollection<string> Roots,
            bool ConfigurationUnavailable = false)
        {
            /// <summary>
            /// True when this delete must not unlink. That covers a configured bin and
            /// also an unreadable configuration, because guessing "no bin" from a failed
            /// settings read would make the delete permanent on exactly the installs
            /// where we know least.
            /// </summary>
            public bool Enabled => ConfigurationUnavailable || !string.IsNullOrWhiteSpace(BinPath);
        }

        private async Task<RecycleBinPolicy> ResolveRecycleBinPolicyAsync(
            IReadOnlyCollection<string> protectedRoots,
            CancellationToken cancellationToken)
        {
            try
            {
                var settings = await _configurationService.GetApplicationSettingsAsync();
                cancellationToken.ThrowIfCancellationRequested();
                return new RecycleBinPolicy(
                    settings?.RecycleBinPath ?? string.Empty,
                    protectedRoots);
            }
            catch (Exception exception) when (exception is not (
                OperationCanceledException or OutOfMemoryException or StackOverflowException))
            {
                // A settings read that fails must not silently downgrade a configured bin
                // into a permanent delete, so treat it as "bin unavailable" and let the
                // caller refuse rather than unlink.
                _logger.LogWarning(
                    exception,
                    "Could not read the recycle bin configuration; audiobook file deletion will be refused rather than made permanent");
                return new RecycleBinPolicy(
                    string.Empty,
                    protectedRoots,
                    ConfigurationUnavailable: true);
            }
        }

        /// <summary>
        /// Move one tracked file to the recycle bin. Returns false when the caller should
        /// refuse the delete outright.
        ///
        /// It deliberately does NOT fall back to a permanent delete. An operator who
        /// configured a bin asked for deletes to be recoverable, and quietly unlinking the
        /// file because the bin was unreachable would do the exact thing the setting exists
        /// to prevent. Refusing leaves the file and the library row intact, which
        /// LibraryDeleteWorkflow already reports as a retryable failure.
        /// </summary>
        private bool TryRecycleTrackedFile(
            string path,
            string? expectedPhysicalObjectIdentity,
            AudiobookFilesystemDeleteResult result,
            RecycleBinPolicy policy,
            IEnumerable<string> allowedRoots,
            FileSystemPathSemantics semantics)
        {
            if (policy.ConfigurationUnavailable)
            {
                result.Warnings.Add(
                    $"Could not recycle '{Path.GetFileName(path)}' because the recycle bin configuration could not be read. The file was left in place.");
                return false;
            }

            var observedExists = File.Exists(path);
            var outcome = FileSystemSafety.TryRecycleFile(
                path,
                allowedRoots,
                expectedPhysicalObjectIdentity,
                policy.BinPath,
                ResolveRecycleSubfolder(path, policy.Roots, semantics),
                TimeProvider.System,
                out var recycledPath,
                out var reason);

            switch (outcome)
            {
                case RecycleFileOutcome.Recycled:
                    result.DeletedFiles++;
                    _logger.LogInformation(
                        "Recycled audiobook file {Path} to {RecycledPath}",
                        LogRedaction.SanitizeFilePath(path),
                        LogRedaction.SanitizeFilePath(recycledPath));
                    return true;

                case RecycleFileOutcome.AlreadyGone:
                    if (observedExists)
                    {
                        result.DeletedFiles++;
                    }

                    return true;

                case RecycleFileOutcome.CrossVolume:
                    result.Warnings.Add(
                        $"Could not recycle '{Path.GetFileName(path)}' because the recycle bin is on a different filesystem from the library. The file was left in place.");
                    _logger.LogWarning(
                        "Blocked audiobook file recycle for {Path}: recycle bin is on a different filesystem",
                        LogRedaction.SanitizeFilePath(path));
                    return false;

                default:
                    result.Warnings.Add(
                        $"Could not recycle '{Path.GetFileName(path)}'. The file was left in place.");
                    _logger.LogWarning(
                        "Blocked audiobook file recycle for {Path}: {Reason}",
                        LogRedaction.SanitizeFilePath(path),
                        LogRedaction.SanitizeText(reason));
                    return false;
            }
        }

        /// <summary>
        /// Mirror the library layout inside the bin by reusing the file's path relative to
        /// whichever root contains it. A chapter-per-file audiobook is dozens of files whose
        /// names repeat across books, so a flat bin would collide constantly and every
        /// collision costs a numbered suffix that makes the bin harder to read.
        /// </summary>
        private static string ResolveRecycleSubfolder(
            string path,
            IReadOnlyCollection<string> roots,
            FileSystemPathSemantics semantics)
        {
            foreach (var root in roots.Where(root => !string.IsNullOrWhiteSpace(root)))
            {
                if (FileSystemPathIdentity.TryGetRelativePathWithinBase(
                        root,
                        path,
                        semantics,
                        out var relativePath)
                    && !string.IsNullOrWhiteSpace(relativePath))
                {
                    var relativeFolder = Path.GetDirectoryName(relativePath);
                    if (!string.IsNullOrWhiteSpace(relativeFolder))
                    {
                        return relativeFolder;
                    }
                }
            }

            return string.Empty;
        }
    }
}
