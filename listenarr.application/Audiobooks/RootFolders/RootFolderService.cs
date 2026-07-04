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
        private readonly IFileSystemSemanticsResolver? _semanticsResolver;
        private readonly IRootFolderRelocationService? _relocationService;

        public RootFolderService(
            IRootFolderRepository repo,
            ILogger<RootFolderService>? logger,
            IMoveQueueService? moveQueue = null,
            IFileSystemSemanticsResolver? semanticsResolver = null,
            IRootFolderRelocationService? relocationService = null)
        {
            _repo = repo;
            _logger = logger;
            _moveQueue = moveQueue;
            _semanticsResolver = semanticsResolver;
            _relocationService = relocationService;
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

            var resolution = await ResolveSemanticsAsync(root.Path, root.CaseSensitivityMode);
            ApplyIdentity(root, resolution);
            if (_relocationService != null
                && await _relocationService.IsBoundaryProtectedAsync(root.Path, resolution.Semantics))
            {
                throw new InvalidOperationException(
                    "Root folder path overlaps an active relocation boundary.");
            }

            var conflict = await FindConflictingRootFolderAsync(root.Path, resolution.Semantics);
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

            EnsureRootIdentityAvailable(root);
            await EnsureNoActiveRelocationAsync(root.Id);

            var sourceSemantics = await ResolveSemanticsAsync(root.Path, root.CaseSensitivityMode);
            await EnsureNoActiveMoveJobsTouchRootAsync(root.Path, sourceSemantics.Semantics);
            var hasReferenced = await _repo.HasAudiobooksUnderPathAsync(root.Path, sourceSemantics.Semantics);
            if (hasReferenced && !reassignRootId.HasValue)
            {
                throw new InvalidOperationException("Root folder is in use by audiobooks; reassign before deletion or provide reassignRootId.");
            }

            if (hasReferenced)
            {
                var newRoot = await _repo.GetByIdAsync(reassignRootId!.Value);
                if (newRoot == null) throw new KeyNotFoundException("Reassign root not found");
                EnsureRootIdentityAvailable(newRoot);
                var targetSemantics = await ResolveSemanticsAsync(newRoot.Path, newRoot.CaseSensitivityMode);
                await EnsureNoActiveMoveJobsTouchRootAsync(newRoot.Path, targetSemantics.Semantics);
                await _repo.MigrateAudiobookPathsAsync(
                    root.Path,
                    newRoot.Path,
                    sourceSemantics.Semantics,
                    targetSemantics.Semantics);
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
            await EnsureNoActiveRelocationAsync(existing.Id);

            var existingResolution = await ResolveSemanticsAsync(
                existing.Path,
                existing.CaseSensitivityMode);
            if (!FileSystemPathIdentity.AreEquivalent(
                existing.Path,
                root.Path,
                existingResolution.Semantics))
            {
                throw new InvalidOperationException(
                    "Root paths cannot be changed by metadata updates; use the path-changes endpoint.");
            }

            await EnsureNoActiveMoveJobsTouchRootAsync(existing.Path, existingResolution.Semantics);
            existing.Name = root.Name;
            existing.IsDefault = root.IsDefault;
            existing.CaseSensitivityMode = root.CaseSensitivityMode;
            var resolution = await ResolveSemanticsAsync(existing.Path, root.CaseSensitivityMode);
            var conflict = await FindConflictingRootFolderAsync(
                existing.Path,
                resolution.Semantics,
                existing.Id);
            if (conflict != null)
            {
                throw new InvalidOperationException(BuildRootFolderConflictMessage(conflict));
            }

            if (root.IsDefault)
            {
                await _repo.ClearDefaultExceptAsync(excludeId: root.Id);
            }

            ApplyIdentity(existing, resolution);
            existing.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(existing);
            return existing;
        }

        private async Task<RootFolderConflict?> FindConflictingRootFolderAsync(
            string normalizedPath,
            FileSystemPathSemantics requestedSemantics,
            int? excludeId = null)
        {
            var roots = await _repo.GetAllAsync();
            foreach (var existingRoot in roots)
            {
                if (excludeId.HasValue && existingRoot.Id == excludeId.Value)
                {
                    continue;
                }

                var semantics = existingRoot.ResolvedCaseSensitivity == FileSystemCaseSensitivity.Unknown
                    ? requestedSemantics
                    : new FileSystemPathSemantics(
                        requestedSemantics.Syntax,
                        existingRoot.ResolvedCaseSensitivity);
                if (FileSystemPathIdentity.AreEquivalent(existingRoot.Path, normalizedPath, semantics))
                {
                    return new RootFolderConflict(existingRoot, RootFolderConflictType.Duplicate);
                }

                if (FileSystemPathIdentity.IsSameOrInside(normalizedPath, existingRoot.Path, semantics))
                {
                    return new RootFolderConflict(existingRoot, RootFolderConflictType.RequestedRootIsNestedInsideExistingRoot);
                }

                if (FileSystemPathIdentity.IsSameOrInside(existingRoot.Path, normalizedPath, semantics))
                {
                    return new RootFolderConflict(existingRoot, RootFolderConflictType.ExistingRootIsNestedInsideRequestedRoot);
                }
            }

            return null;
        }

        private async Task EnsureNoActiveMoveJobsTouchRootAsync(
            string rootPath,
            FileSystemPathSemantics semantics)
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
                job.Status.IsActive() &&
                (IsMoveJobPathInsideRoot(job.SourcePath, rootPath, semantics) ||
                 IsMoveJobPathInsideRoot(job.RequestedPath, rootPath, semantics)));

            if (conflictingJob == null)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Root folder has active move job {conflictingJob.Id}; wait for queued or processing moves touching this root to finish before deleting or reassigning it.");
        }

        private static bool IsMoveJobPathInsideRoot(
            string? path,
            string rootPath,
            FileSystemPathSemantics semantics)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return FileSystemPathIdentity.IsSameOrInside(
                path,
                rootPath,
                semantics);
        }

        private async Task<FileSystemSemanticsResolution> ResolveSemanticsAsync(
            string path,
            FileSystemCaseSensitivityMode mode)
        {
            if (_semanticsResolver == null)
            {
                return new FileSystemSemanticsResolution(
                    FileSystemPathSemantics.CurrentHostDefault,
                    PathIdentityState.Valid,
                    Path.GetPathRoot(path) ?? path);
            }

            var resolution = await _semanticsResolver.ResolveAsync(path, mode);
            if (resolution.State != PathIdentityState.Valid)
            {
                throw new InvalidOperationException(
                    resolution.Reason ?? "Filesystem case sensitivity is unresolved; select an explicit override.");
            }

            return resolution;
        }

        private static void ApplyIdentity(
            RootFolder root,
            FileSystemSemanticsResolution resolution)
        {
            root.ResolvedCaseSensitivity = resolution.Semantics.CaseSensitivity;
            root.PathIdentityState = resolution.State;
            root.PathIdentityKey = FileSystemPathIdentity.CreateKey(
                "root",
                root.Path,
                resolution.Semantics);
        }

        private void EnsureRootIdentityAvailable(RootFolder root)
        {
            if (_semanticsResolver != null && root.PathIdentityState != PathIdentityState.Valid)
            {
                throw new InvalidOperationException(
                    "Root filesystem identity is unresolved or conflicted; select an explicit case-sensitivity override before destructive operations.");
            }
        }

        private async Task EnsureNoActiveRelocationAsync(int rootFolderId)
        {
            if (_relocationService != null
                && await _relocationService.GetActiveForRootAsync(rootFolderId) != null)
            {
                throw new InvalidOperationException(
                    "Root folder metadata and deletion are locked while a relocation is active.");
            }
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
