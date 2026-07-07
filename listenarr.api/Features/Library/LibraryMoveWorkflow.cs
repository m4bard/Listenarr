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
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library
{
    public sealed class LibraryMoveWorkflow
    {
        private readonly IAudiobookRepository _repo;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IMoveQueueService? _moveQueueService;
        private readonly IFileSystem _fileSystem;
        private readonly IFileSystemSemanticsResolver _semanticsResolver;
        private readonly IRootFolderRelocationService? _relocationService;
        private readonly ILogger<LibraryMoveWorkflow> _logger;

        public LibraryMoveWorkflow(
            IAudiobookRepository repo,
            IServiceScopeFactory scopeFactory,
            IFileSystem fileSystem,
            ILogger<LibraryMoveWorkflow> logger,
            IFileSystemSemanticsResolver semanticsResolver,
            IMoveQueueService? moveQueueService = null,
            IRootFolderRelocationService? relocationService = null)
        {
            _repo = repo;
            _scopeFactory = scopeFactory;
            _fileSystem = fileSystem;
            _logger = logger;
            _semanticsResolver = semanticsResolver;
            _moveQueueService = moveQueueService;
            _relocationService = relocationService;
        }

        public async Task<IActionResult> EnqueueAsync(int id, LibraryController.MoveRequest request)
        {
            if (_moveQueueService == null) return new NotFoundObjectResult(new { message = "Move queue not available" });
            var audiobook = await _repo.GetByIdAsync(id);
            if (audiobook == null) return new NotFoundObjectResult(new { message = "Audiobook not found" });
            if (request == null) return new BadRequestObjectResult(new { message = "Request body is required" });

            if (string.IsNullOrEmpty(request.DestinationPath))
            {
                return new BadRequestObjectResult(new { message = "DestinationPath is required" });
            }

            // Preserve valid Unix path-segment whitespace, but reject values that only become
            // absolute after trimming accidental leading whitespace. Otherwise move would treat
            // " /books/Title" as a relative child folder under the configured destination root.
            if (FileUtils.HasLeadingWhitespaceBeforeRootedPath(request.DestinationPath))
            {
                return new BadRequestObjectResult(new { message = "DestinationPath is invalid: leading whitespace before an absolute path is not allowed." });
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
                var rootFolderService = scope.ServiceProvider.GetRequiredService<IRootFolderService>();
                var settings = await configService.GetApplicationSettingsAsync();
                var rootFolders = await rootFolderService.GetAllAsync();

                var allowedMoveRoots = new List<MoveRootBoundary>();
                var normalizedOutputPath = TryNormalizeMoveRoot(settings.OutputPath, "configured output path");
                await AddAllowedMoveRootAsync(allowedMoveRoots, normalizedOutputPath, FileSystemCaseSensitivityMode.Auto);

                string? defaultRootPath = null;
                foreach (var rootFolder in rootFolders)
                {
                    var normalizedRootPath = TryNormalizeMoveRoot(rootFolder.Path, $"root folder {rootFolder.Id}");
                    if (normalizedRootPath == null)
                    {
                        continue;
                    }

                    await AddAllowedMoveRootAsync(allowedMoveRoots, normalizedRootPath, rootFolder.CaseSensitivityMode);
                    if (rootFolder.IsDefault && defaultRootPath == null)
                    {
                        defaultRootPath = normalizedRootPath;
                    }
                }

                if (allowedMoveRoots.Count == 0)
                {
                    return new BadRequestObjectResult(new { message = "DestinationPath must be inside a configured root folder or output path" });
                }

                var destinationIsRooted = Path.IsPathRooted(request.DestinationPath!);
                var relativeMoveBase = normalizedOutputPath ?? defaultRootPath ?? allowedMoveRoots.FirstOrDefault()?.Path;
                if (!destinationIsRooted && string.IsNullOrEmpty(relativeMoveBase))
                {
                    return new BadRequestObjectResult(new { message = "DestinationPath requires a configured root folder or output path" });
                }

                var destinationCandidate = destinationIsRooted
                    ? request.DestinationPath!
                    : FileUtils.CombineWithOptionalBase(relativeMoveBase, request.DestinationPath!);
                if (!FileUtils.TryNormalizeUserProvidedDirectoryPathForCurrentOs(
                    destinationCandidate,
                    out var final,
                    out var validationReason,
                    rejectParentTraversal: true))
                {
                    return new BadRequestObjectResult(new { message = $"DestinationPath is invalid: {validationReason}" });
                }
                if (!_fileSystem.TryValidateMutationTarget(final, allowedMoveRoots.Select(root => root.Path), out final, out var finalReason))
                {
                    _logger.LogWarning(
                        "Blocked move destination for audiobook {AudiobookId}: {Destination}. Reason: {Reason}",
                        id,
                        final,
                        finalReason);
                    return new BadRequestObjectResult(new { message = "DestinationPath must be inside a configured root folder or output path" });
                }

                var targetBoundary = FindAllowedMoveRoot(final, allowedMoveRoots)
                    ?? throw new InvalidOperationException("Destination filesystem identity is unavailable.");

                if (request.MoveFiles == false)
                {
                    try
                    {
                        var sourceBasePath = audiobook.BasePath;
                        var sourceSemantics = targetBoundary.Semantics;
                        if (!string.IsNullOrWhiteSpace(sourceBasePath))
                        {
                            var sourceBoundary = FindAllowedMoveRoot(sourceBasePath, allowedMoveRoots);
                            if (sourceBoundary != null)
                            {
                                sourceSemantics = sourceBoundary.Semantics;
                            }
                            else
                            {
                                var sourceResolution = await _semanticsResolver.ResolveAsync(sourceBasePath);
                                if (sourceResolution.State != PathIdentityState.Valid)
                                {
                                    return new BadRequestObjectResult(new
                                    {
                                        message = sourceResolution.Reason
                                            ?? "Source filesystem identity is unavailable."
                                    });
                                }

                                sourceSemantics = sourceResolution.Semantics;
                            }
                        }

                        if (_relocationService != null
                            && (await _relocationService.IsBoundaryProtectedAsync(final, targetBoundary.Semantics)
                                || (!string.IsNullOrWhiteSpace(sourceBasePath)
                                    && await _relocationService.IsBoundaryProtectedAsync(sourceBasePath, sourceSemantics))))
                        {
                            return new ConflictObjectResult(new
                            {
                                message = "Move source or target overlaps an active root folder relocation boundary."
                            });
                        }

                        var rewritten = await _repo.RewritePathReferencesAsync(
                            audiobook.Id,
                            sourceBasePath,
                            final,
                            sourceSemantics,
                            targetBoundary.Semantics);
                        if (!rewritten)
                        {
                            return new NotFoundObjectResult(new { message = "Audiobook not found" });
                        }

                        _logger.LogInformation("Updated BasePath for audiobook {AudiobookId} without moving files: {BasePath}", id, final);
                        return new OkObjectResult(new { message = "Destination updated" });
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                    {
                        _logger.LogError(ex, "Failed to update BasePath for audiobook {AudiobookId}", id);
                        return new ObjectResult(new { message = "Failed to update BasePath", error = ex.Message })
                        {
                            StatusCode = StatusCodes.Status500InternalServerError
                        };
                    }
                }

                var sourcePath = !string.IsNullOrEmpty(request.SourcePath)
                    ? request.SourcePath
                    : audiobook.BasePath;

                if (string.IsNullOrEmpty(sourcePath))
                {
                    return new BadRequestObjectResult(new { message = "Source path not provided. Supply current source path in the Move request or ensure audiobook has a valid BasePath." });
                }

                if (FileUtils.IsPathInvalidForCurrentOs(sourcePath))
                {
                    return new BadRequestObjectResult(new { message = "Source path is not valid for this operating system." });
                }

                if (!_fileSystem.DirectoryExists(sourcePath))
                {
                    return new BadRequestObjectResult(new { message = "Source path does not exist. Ensure the audiobook's current BasePath exists or provide a valid SourcePath in the request." });
                }

                var targetParent = Path.GetDirectoryName(final);
                if (string.IsNullOrEmpty(targetParent))
                {
                    return new BadRequestObjectResult(new { message = "Invalid target path" });
                }

                try
                {
                    if (!_fileSystem.DirectoryExists(targetParent)) _fileSystem.CreateDirectory(targetParent);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    _logger.LogWarning(ex, "Failed to access or create target parent {TargetParent}", targetParent);
                    return new BadRequestObjectResult(new { message = "Target parent path is not writable or unavailable" });
                }

                try
                {
                    var srcFull = Path.GetFullPath(sourcePath);
                    var tgtFull = Path.GetFullPath(final);
                    if (FileSystemPathIdentity.AreEquivalent(srcFull, tgtFull, targetBoundary.Semantics))
                    {
                        return new BadRequestObjectResult(new { message = "Source and target paths are identical; nothing to move." });
                    }
                }
                catch (Exception normalizeEx) when (
                    normalizeEx is ArgumentException
                    || normalizeEx is NotSupportedException
                    || normalizeEx is PathTooLongException
                    || normalizeEx is System.Security.SecurityException)
                {
                    _logger.LogDebug(normalizeEx, "Unable to normalize move paths for audiobook {AudiobookId}", id);
                }

                var jobId = await _moveQueueService.EnqueueMoveAsync(
                    id,
                    final,
                    sourcePath,
                    request.DeleteEmptySource ?? true);
                await BroadcastQueuedAsync(jobId, id);

                return new AcceptedResult(string.Empty, new { message = "Move enqueued", jobId });
            }
            catch (PersistenceException ex)
            {
                _logger.LogError(ex, "Move queue persistence failed while enqueueing move job for audiobook {AudiobookId}", id);
                return new ObjectResult(new
                {
                    message = "Move queue persistence is unavailable. Check database migrations.",
                    code = "move_queue_persistence_unavailable"
                })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
            catch (MoveRelocationConflictException ex)
            {
                _logger.LogWarning(ex, "Blocked move for audiobook {AudiobookId} during an active root folder relocation", id);
                return new ConflictObjectResult(new { message = ex.Message, code = "move_relocation_conflict" });
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Failed to enqueue move job for audiobook {AudiobookId}", id);
                return new ObjectResult(new
                {
                    message = "Failed to enqueue move job",
                    code = "move_enqueue_failed"
                })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        public async Task<IActionResult> GetStatusAsync(
            string jobId,
            CancellationToken cancellationToken = default)
        {
            if (_moveQueueService == null) return new NotFoundObjectResult(new { message = "Move queue not available" });
            if (!Guid.TryParse(jobId, out var gid)) return new BadRequestObjectResult(new { message = "Invalid jobId" });
            var job = await _moveQueueService.GetJobAsync(gid, cancellationToken);
            if (job != null)
            {
                _logger.LogInformation("Queried move job {JobId} status: {Status}", gid, job!.Status);
                return new OkObjectResult(job);
            }

            return new NotFoundObjectResult(new { message = "Job not found" });
        }

        public async Task<IActionResult> RequeueAsync(string jobId)
        {
            if (_moveQueueService == null) return new NotFoundObjectResult(new { message = "Move queue not available" });
            if (!Guid.TryParse(jobId, out var gid)) return new BadRequestObjectResult(new { message = "Invalid jobId" });

            Guid? newJobId;
            try
            {
                newJobId = await _moveQueueService.RequeueMoveAsync(gid);
            }
            catch (MoveRelocationConflictException ex)
            {
                return new ConflictObjectResult(new { message = ex.Message, code = "move_relocation_conflict" });
            }
            if (newJobId == null)
            {
                return new BadRequestObjectResult(new { message = "Unable to requeue job (not found or invalid status)" });
            }

            await BroadcastQueuedAsync(newJobId.Value, audiobookId: null);
            return new AcceptedResult(string.Empty, new { message = "Requeued move job", jobId = newJobId });
        }

        private string? TryNormalizeMoveRoot(string? path, string description)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (FileUtils.TryNormalizeUserProvidedDirectoryPathForCurrentOs(
                path,
                out var normalizedPath,
                out var validationReason,
                allowFileSystemRoot: true,
                rejectParentTraversal: true))
            {
                return normalizedPath;
            }

            _logger.LogWarning(
                "Skipping invalid move boundary from {Description}: {Reason}",
                description,
                validationReason);
            return null;
        }

        private async Task AddAllowedMoveRootAsync(
            List<MoveRootBoundary> allowedRoots,
            string? normalizedRoot,
            FileSystemCaseSensitivityMode caseSensitivityMode)
        {
            if (string.IsNullOrEmpty(normalizedRoot))
            {
                return;
            }

            var resolution = await _semanticsResolver.ResolveAsync(normalizedRoot, caseSensitivityMode);
            if (resolution.State != PathIdentityState.Valid)
            {
                _logger.LogWarning(
                    "Skipping move boundary {Root}: {Reason}",
                    LogRedaction.SanitizeFilePath(normalizedRoot),
                    resolution.Reason ?? "filesystem identity unavailable");
                return;
            }

            if (allowedRoots.Any(root => FileSystemPathIdentity.AreEquivalent(
                root.Path,
                normalizedRoot,
                resolution.Semantics)))
            {
                return;
            }

            allowedRoots.Add(new MoveRootBoundary(normalizedRoot, resolution.Semantics));
        }

        private static MoveRootBoundary? FindAllowedMoveRoot(
            string path,
            IReadOnlyCollection<MoveRootBoundary> allowedRoots)
        {
            return allowedRoots.FirstOrDefault(root => FileSystemPathIdentity.IsSameOrInside(
                path,
                root.Path,
                root.Semantics));
        }

        private sealed record MoveRootBoundary(string Path, FileSystemPathSemantics Semantics);

        private async Task BroadcastQueuedAsync(Guid jobId, int? audiobookId)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var hub = scope.ServiceProvider.GetRequiredService<IHubBroadcaster>();
                var job = new { jobId = jobId.ToString(), audiobookId, status = "Queued", enqueuedAt = DateTime.UtcNow };
                await hub.BroadcastAsync(RealtimeHubTarget.Downloads, "MoveJobUpdate", job);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogWarning(ex, "Failed to broadcast MoveJobUpdate for job {JobId}", jobId);
            }
        }
    }
}
