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
        /// Prepare the bin for a recursive folder delete. Returns null when the delete must
        /// be refused; in that case nothing in the library has been touched and a warning
        /// says why.
        ///
        /// Like the per-file branch, this never falls back to a permanent delete. An
        /// operator who configured a bin asked for deletes to be recoverable.
        /// </summary>
        private RecycleFolderContainer? TryPrepareFolderRecycle(
            DeleteFolderTarget deleteTarget,
            PinnedDirectoryCreation.PinnedDirectoryAnchor targetAuthorization,
            RecycleBinPolicy policy,
            AudiobookFilesystemDeleteResult result)
        {
            if (policy.ConfigurationUnavailable)
            {
                result.Warnings.Add(
                    "Could not recycle the audiobook folder because the recycle bin configuration could not be read or is no longer valid. The folder was left in place.");
                return null;
            }

            RecycleFolderPreparationOutcome outcome;
            RecycleFolderContainer? container;
            string reason;
            try
            {
                outcome = FileSystemSafety.TryPrepareFolderRecycle(
                    targetAuthorization,
                    deleteTarget.FolderPath,
                    policy.BinPath,
                    ResolveRecycleSubfolder(
                        deleteTarget.FolderPath,
                        policy.Roots,
                        deleteTarget.Semantics),
                    TimeProvider.System,
                    out container,
                    out reason);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException or InvalidOperationException
                or ArgumentException or System.ComponentModel.Win32Exception)
            {
                outcome = RecycleFolderPreparationOutcome.Blocked;
                container = null;
                reason = $"Preparing the recycle bin failed safely: {exception.GetType().Name}.";
            }

            if (outcome == RecycleFolderPreparationOutcome.Ready && container != null)
            {
                return container;
            }

            result.Warnings.Add(outcome switch
            {
                RecycleFolderPreparationOutcome.Overlaps =>
                    "Could not recycle the audiobook folder because the recycle bin is inside it, or it is inside the recycle bin. The folder was left in place.",
                RecycleFolderPreparationOutcome.CrossVolume =>
                    "Could not recycle the audiobook folder because the recycle bin is on a different filesystem from the library. The folder was left in place.",
                _ => "Could not recycle the audiobook folder. The folder was left in place."
            });
            _logger.LogWarning(
                "Blocked recycling of audiobook folder {FolderPath}: {Reason}",
                LogRedaction.SanitizeFilePath(deleteTarget.FolderPath),
                LogRedaction.SanitizeText(reason));
            return null;
        }

        private void ReportFolderRecycled(
            DeleteFolderTarget deleteTarget,
            RecycleFolderContainer container,
            bool completed,
            AudiobookFilesystemDeleteResult result)
        {
            if (!completed)
            {
                // Every file is in exactly one place, because each move is a rename.
                // Saying where is what makes the partial state recoverable by hand.
                result.Warnings.Add(
                    "Could not finish moving the audiobook folder to the recycle bin. Files already moved are in the recycle bin and the rest were left in place.");
            }

            if (container.UnstampedFiles > 0)
            {
                result.Warnings.Add(
                    "Some files were moved to the recycle bin, but their recycle time could not be recorded, so they may be removed by the next retention sweep.");
            }

            if (container.RecycledFiles > 0)
            {
                _logger.LogInformation(
                    "Recycled {FileCount} files from audiobook folder {FolderPath} to {RecycledPath}",
                    container.RecycledFiles,
                    LogRedaction.SanitizeFilePath(deleteTarget.FolderPath),
                    LogRedaction.SanitizeFilePath(container.FullPath));
            }

            container.RemoveIfEmpty();
        }
    }
}
