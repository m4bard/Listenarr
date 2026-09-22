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

namespace Listenarr.Application.Library.RecycleBin
{
    /// <summary>
    /// Why a proposed recycle bin path was refused. An empty path is always valid and
    /// means the feature is off.
    /// </summary>
    public enum RecycleBinPathRejection
    {
        None,
        NotAbsolute,
        FilesystemRoot,
        InsideRootFolder,
        ContainsRootFolder,
        Unusable
    }

    public sealed record RecycleBinPathValidation(
        RecycleBinPathRejection Rejection,
        string Message)
    {
        public bool IsValid => Rejection == RecycleBinPathRejection.None;

        public static RecycleBinPathValidation Valid { get; } =
            new(RecycleBinPathRejection.None, string.Empty);
    }

    public sealed record RecycleBinSweepResult(int FilesRemoved, int DirectoriesRemoved)
    {
        public static RecycleBinSweepResult Empty { get; } = new(0, 0);
    }

    public interface IRecycleBinService
    {
        /// <summary>
        /// Check a proposed bin path before it is saved. The bin must not sit inside a root
        /// folder and must not contain one: a bin under a root would be walked by the
        /// library scanner and re-imported, which would resurrect every file the operator
        /// just deleted.
        /// </summary>
        Task<RecycleBinPathValidation> ValidatePathAsync(
            string? recycleBinPath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Remove everything in the bin now, regardless of age.
        /// </summary>
        Task<RecycleBinSweepResult> EmptyAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Remove bin entries whose recycle timestamp is older than the configured
        /// retention. A retention of zero keeps everything until the bin is emptied by hand.
        /// </summary>
        Task<RecycleBinSweepResult> CleanupAsync(CancellationToken cancellationToken = default);
    }
}
