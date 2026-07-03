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
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.RootFolders
{
    public class RootFolderService : IRootFolderService
    {
        private readonly IRootFolderRepository _repo;
        private readonly ILogger<RootFolderService>? _logger;
        private readonly IMoveQueueService? _moveQueue;

        public RootFolderService(IRootFolderRepository repo, ILogger<RootFolderService>? logger, IMoveQueueService? moveQueue = null)
        {
            _repo = repo;
            _logger = logger;
            _moveQueue = moveQueue;
        }

        public async Task<RootFolder?> GetDefaultAsync()
        {
            return await _repo.GetDefaultAsync();
        }

        public async Task<RootFolder> CreateAsync(RootFolder root)
        {
            root.Name = root.Name?.Trim() ?? string.Empty;
            root.Path = NormalizeRootFolderPathForStorage(root.Path);

            if (string.IsNullOrWhiteSpace(root.Name)) throw new ArgumentException("Name is required");

            var conflict = await FindConflictingRootFolderAsync(root.Path);
            if (conflict != null)
            {
                throw new InvalidOperationException(BuildRootFolderConflictMessage(conflict));
            }

            if (root.IsDefault)
            {
                await _repo.ClearDefaultExceptAsync(excludeId: null);
            }

            await _repo.AddAsync(root);
            return root;
        }

        public async Task DeleteAsync(int id, int? reassignRootId = null)
        {
            var root = await _repo.GetByIdAsync(id);
            if (root == null) throw new KeyNotFoundException("Root folder not found");

            await EnsureNoActiveMoveJobsTouchRootAsync(root.Path);

            var hasReferenced = await _repo.HasAudiobooksUnderPathAsync(root.Path);
            if (hasReferenced && !reassignRootId.HasValue)
            {
                throw new InvalidOperationException("Root folder is in use by audiobooks; reassign before deletion or provide reassignRootId.");
            }

            if (hasReferenced)
            {
                var newRoot = await _repo.GetByIdAsync(reassignRootId!.Value);
                if (newRoot == null) throw new KeyNotFoundException("Reassign root not found");
                await EnsureNoActiveMoveJobsTouchRootAsync(newRoot.Path);
                await _repo.MigrateAudiobookPathsAsync(root.Path, newRoot.Path);
            }

            await _repo.RemoveAsync(id);
        }

        public async Task<List<RootFolder>> GetAllAsync() => await _repo.GetAllAsync();

        public async Task<RootFolder?> GetByIdAsync(int id) => await _repo.GetByIdAsync(id);

        public async Task<RootFolder> UpdateAsync(RootFolder root, bool moveFiles = false, bool deleteEmptySource = true)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            root.Name = root.Name?.Trim() ?? string.Empty;
            root.Path = NormalizeRootFolderPathForStorage(root.Path);

            if (string.IsNullOrWhiteSpace(root.Name)) throw new ArgumentException("Name is required");

            var existing = await _repo.GetByIdAsync(root.Id);
            if (existing == null) throw new KeyNotFoundException("Root folder not found");

            var pathChanged = !FileUtils.AreFilesystemPathsEquivalentForCurrentOs(existing.Path, root.Path);
            if (pathChanged)
            {
                var conflict = await FindConflictingRootFolderAsync(root.Path, root.Id);
                if (conflict != null)
                {
                    throw new InvalidOperationException(BuildRootFolderConflictMessage(conflict));
                }
            }

            if (root.IsDefault)
            {
                await _repo.ClearDefaultExceptAsync(excludeId: root.Id);
            }

            var oldPath = existing.Path;
            var newPath = root.Path;

            List<(int audiobookId, string original, string target)> moves = new();
            if (pathChanged)
            {
                await EnsureNoActiveMoveJobsTouchRootAsync(oldPath);
                await EnsureNoActiveMoveJobsTouchRootAsync(newPath);
                moves = await _repo.MigrateAudiobookPathsAsync(oldPath, newPath);

                try
                {
                    _logger?.LogInformation("Root rename from {OldPath} to {NewPath}: {Count} audiobooks affected", oldPath, newPath, moves.Count);
                    foreach (var m in moves)
                    {
                        _logger?.LogInformation("Root rename move prep: AudiobookId={AudiobookId} Original={Original} Target={Target}", m.audiobookId, m.original, m.target);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    _logger?.LogDebug(ex, "Failed to emit diagnostics for root rename");
                }
            }

            existing.Name = root.Name;
            existing.Path = root.Path;
            existing.IsDefault = root.IsDefault;
            existing.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(existing);

            if (moveFiles && _moveQueue != null)
            {
                foreach (var m in moves)
                {
                    try
                    {
                        await _moveQueue.EnqueueMoveAsync(
                            m.audiobookId,
                            m.target,
                            m.original,
                            deleteEmptySource);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                    {
                        _logger?.LogWarning(ex, "Failed to enqueue move for audiobook {AudiobookId} during root rename", m.audiobookId);
                        throw;
                    }
                }
            }

            return existing;
        }

        private async Task<RootFolderConflict?> FindConflictingRootFolderAsync(
            string normalizedPath,
            int? excludeId = null)
        {
            var roots = await _repo.GetAllAsync();
            foreach (var existingRoot in roots)
            {
                if (excludeId.HasValue && existingRoot.Id == excludeId.Value)
                {
                    continue;
                }

                if (FileUtils.AreFilesystemPathsEquivalentForCurrentOs(existingRoot.Path, normalizedPath))
                {
                    return new RootFolderConflict(existingRoot, RootFolderConflictType.Duplicate);
                }

                if (FileUtils.IsPathSameOrInside(normalizedPath, existingRoot.Path))
                {
                    return new RootFolderConflict(existingRoot, RootFolderConflictType.RequestedRootIsNestedInsideExistingRoot);
                }

                if (FileUtils.IsPathSameOrInside(existingRoot.Path, normalizedPath))
                {
                    return new RootFolderConflict(existingRoot, RootFolderConflictType.ExistingRootIsNestedInsideRequestedRoot);
                }
            }

            return null;
        }

        private async Task EnsureNoActiveMoveJobsTouchRootAsync(string rootPath)
        {
            if (_moveQueue == null)
            {
                return;
            }

            var activeJobsTask = _moveQueue.GetActiveJobsAsync();
            IReadOnlyList<MoveJob>? activeJobs = activeJobsTask == null
                ? Array.Empty<MoveJob>()
                : await activeJobsTask;
            activeJobs ??= Array.Empty<MoveJob>();

            var conflictingJob = activeJobs.FirstOrDefault(job =>
                IsActiveMoveJobStatus(job.Status) &&
                (IsMoveJobPathInsideRoot(job.SourcePath, rootPath) ||
                 IsMoveJobPathInsideRoot(job.RequestedPath, rootPath)));

            if (conflictingJob == null)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Root folder has active move job {conflictingJob.Id}; wait for queued or processing moves touching this root to finish before deleting or reassigning it.");
        }

        private static bool IsMoveJobPathInsideRoot(string? path, string rootPath)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return FileUtils.IsPathSameOrInside(path, rootPath);
        }

        private static bool IsActiveMoveJobStatus(string? status)
        {
            return string.Equals(status, "Queued", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "Processing", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildRootFolderConflictMessage(RootFolderConflict conflict)
        {
            return conflict.Type switch
            {
                RootFolderConflictType.Duplicate => "A root folder with that path already exists.",
                RootFolderConflictType.RequestedRootIsNestedInsideExistingRoot =>
                    $"Root folder cannot be nested inside existing root '{conflict.Root.Name}'.",
                RootFolderConflictType.ExistingRootIsNestedInsideRequestedRoot =>
                    $"Root folder cannot contain existing root '{conflict.Root.Name}'.",
                _ => "Root folder path conflicts with an existing root folder."
            };
        }

        private static string NormalizeRootFolderPathForStorage(string? path)
        {
            // Root folders may be filesystem boundaries, including UNC shares, but parent
            // traversal is still rejected so the stored boundary is explicit rather than
            // reached indirectly through ../ segments.
            if (!FileUtils.TryNormalizeUserProvidedDirectoryPathForCurrentOs(
                path,
                out var normalizedPath,
                out var validationReason,
                allowFileSystemRoot: true,
                rejectParentTraversal: true))
            {
                throw new ArgumentException($"Path is not valid for this operating system: {validationReason}");
            }

            return normalizedPath;
        }

        private sealed record RootFolderConflict(RootFolder Root, RootFolderConflictType Type);

        private enum RootFolderConflictType
        {
            Duplicate,
            RequestedRootIsNestedInsideExistingRoot,
            ExistingRootIsNestedInsideRequestedRoot
        }
    }
}
