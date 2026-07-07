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
using Listenarr.Application.Common;
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.RootFolders
{
    public class RootFolderService : IRootFolderService
    {
        private readonly IRootFolderRepository _repo;
        private readonly ILogger<RootFolderService>? _logger;
        private readonly IMoveQueueService? _moveQueue;
        private readonly IFileSystemSemanticsResolver _semanticsResolver;
        private readonly IRootFolderRelocationService? _relocationService;
        private readonly IFilesystemMutationCoordinator _mutationCoordinator;

        public RootFolderService(
            IRootFolderRepository repo,
            ILogger<RootFolderService>? logger,
            IFileSystemSemanticsResolver semanticsResolver,
            IMoveQueueService? moveQueue = null,
            IRootFolderRelocationService? relocationService = null,
            IFilesystemMutationCoordinator? mutationCoordinator = null)
        {
            _repo = repo;
            _logger = logger;
            _semanticsResolver = semanticsResolver ?? throw new ArgumentNullException(nameof(semanticsResolver));
            _mutationCoordinator = mutationCoordinator ?? new FilesystemMutationCoordinator();
            _moveQueue = moveQueue;
            _relocationService = relocationService;
        }

        public async Task<RootFolder?> GetDefaultAsync()
        {
            return await _repo.GetDefaultAsync();
        }

        public Task<RootFolder> CreateAsync(RootFolder root) =>
            _mutationCoordinator.ExecuteExclusiveAsync(_ => CreateCoreAsync(root));

        private async Task<RootFolder> CreateCoreAsync(RootFolder root)
        {
            root.Name = root.Name?.Trim() ?? string.Empty;
            root.Path = FileUtils.NormalizeRootFolderPathForStorage(root.Path);

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

        public Task DeleteAsync(int id, int? reassignRootId = null) =>
            _mutationCoordinator.ExecuteExclusiveAsync(_ => DeleteCoreAsync(id, reassignRootId));

        private async Task DeleteCoreAsync(int id, int? reassignRootId)
        {
            var root = await _repo.GetByIdAsync(id);
            if (root == null) throw new KeyNotFoundException("Root folder not found");

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
                await EnsureNoActiveRelocationAsync(newRoot.Id);
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

        public Task<RootFolder> UpdateAsync(RootFolder root, bool moveFiles = false, bool deleteEmptySource = true) =>
            _mutationCoordinator.ExecuteExclusiveAsync(
                _ => UpdateCoreAsync(root, moveFiles, deleteEmptySource));

        private async Task<RootFolder> UpdateCoreAsync(
            RootFolder root,
            bool moveFiles,
            bool deleteEmptySource)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            root.Name = root.Name?.Trim() ?? string.Empty;
            root.Path = FileUtils.NormalizeRootFolderPathForStorage(root.Path);

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

                var existingSemantics = FileSystemPathIdentity.ResolveComparisonSemantics(
                    existingRoot.ResolvedCaseSensitivity,
                    requestedSemantics);
                if (PathsAreEquivalentUnderEitherSemantics(existingRoot.Path, normalizedPath, requestedSemantics, existingSemantics))
                {
                    return new RootFolderConflict(existingRoot, RootFolderConflictType.Duplicate);
                }

                if (PathIsInsideUnderEitherSemantics(normalizedPath, existingRoot.Path, requestedSemantics, existingSemantics))
                {
                    return new RootFolderConflict(existingRoot, RootFolderConflictType.RequestedRootIsNestedInsideExistingRoot);
                }

                if (PathIsInsideUnderEitherSemantics(existingRoot.Path, normalizedPath, requestedSemantics, existingSemantics))
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

        private async Task EnsureNoActiveRelocationAsync(int rootFolderId)
        {
            if (_relocationService != null
                && await _relocationService.GetActiveForRootAsync(rootFolderId) != null)
            {
                throw new InvalidOperationException(
                    "Root folder metadata and deletion are locked while a relocation is active.");
            }
        }

        private static bool PathsAreEquivalentUnderEitherSemantics(
            string left,
            string right,
            FileSystemPathSemantics requestedSemantics,
            FileSystemPathSemantics existingSemantics) =>
            FileSystemPathIdentity.AreEquivalent(left, right, requestedSemantics)
            || FileSystemPathIdentity.AreEquivalent(left, right, existingSemantics);

        private static bool PathIsInsideUnderEitherSemantics(
            string candidate,
            string root,
            FileSystemPathSemantics requestedSemantics,
            FileSystemPathSemantics existingSemantics) =>
            FileSystemPathIdentity.IsSameOrInside(candidate, root, requestedSemantics)
            || FileSystemPathIdentity.IsSameOrInside(candidate, root, existingSemantics);

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

        private sealed record RootFolderConflict(RootFolder Root, RootFolderConflictType Type);

        private enum RootFolderConflictType
        {
            Duplicate,
            RequestedRootIsNestedInsideExistingRoot,
            ExistingRootIsNestedInsideRequestedRoot
        }
    }
}
