/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Application.Common;
using Listenarr.Application.Common.Exceptions;
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Moving;

public sealed class AudiobookDestinationRewriteService : IAudiobookDestinationRewriteService
{
    private readonly IAudiobookRepository _repo;
    private readonly IConfigurationService _configService;
    private readonly IRootFolderService _rootFolderService;
    private readonly IFileSystem _fileSystem;
    private readonly IFileSystemSemanticsResolver _semanticsResolver;
    private readonly IRootFolderRelocationService? _relocationService;
    private readonly IFilesystemMutationCoordinator _mutationCoordinator;
    private readonly ILogger<AudiobookDestinationRewriteService> _logger;

    public AudiobookDestinationRewriteService(
        IAudiobookRepository repo,
        IConfigurationService configService,
        IRootFolderService rootFolderService,
        IFileSystem fileSystem,
        IFileSystemSemanticsResolver semanticsResolver,
        ILogger<AudiobookDestinationRewriteService> logger,
        IRootFolderRelocationService? relocationService = null,
        IFilesystemMutationCoordinator? mutationCoordinator = null)
    {
        _repo = repo;
        _configService = configService;
        _rootFolderService = rootFolderService;
        _fileSystem = fileSystem;
        _semanticsResolver = semanticsResolver;
        _logger = logger;
        _relocationService = relocationService;
        _mutationCoordinator = mutationCoordinator ?? new FilesystemMutationCoordinator();
    }

    public async Task<AudiobookDestinationRewriteResult> RewriteDestinationAsync(
        int audiobookId,
        string destinationPath,
        string? expectedSourcePath,
        CancellationToken cancellationToken = default)
    {
        _ = expectedSourcePath;
        var destination = await ResolveDestinationAsync(destinationPath, cancellationToken);

        return await _mutationCoordinator.ExecuteExclusiveAsync(async lockedCancellationToken =>
        {
            var currentAudiobook = await _repo.GetByIdAsync(audiobookId);
            if (currentAudiobook == null)
            {
                throw new ApplicationNotFoundException("audiobook_not_found", "Audiobook not found");
            }

            var sourceBasePath = currentAudiobook.BasePath;
            var sourceSemantics = destination.TargetBoundary.Semantics;
            if (!string.IsNullOrWhiteSpace(sourceBasePath))
            {
                var sourceBoundary = FindAllowedMoveRoot(sourceBasePath, destination.AllowedMoveRoots);
                if (sourceBoundary != null)
                {
                    sourceSemantics = sourceBoundary.Semantics;
                }
                else
                {
                    var sourceResolution = await _semanticsResolver.ResolveAsync(
                        sourceBasePath,
                        cancellationToken: lockedCancellationToken);
                    if (sourceResolution.State != PathIdentityState.Valid)
                    {
                        throw new ApplicationValidationException(
                            "source_filesystem_identity_unavailable",
                            sourceResolution.Reason ?? "Source filesystem identity is unavailable.");
                    }

                    sourceSemantics = sourceResolution.Semantics;
                }
            }

            if (_relocationService != null
                && (await _relocationService.IsBoundaryProtectedAsync(
                        destination.Path,
                        destination.TargetBoundary.Semantics,
                        lockedCancellationToken)
                    || (!string.IsNullOrWhiteSpace(sourceBasePath)
                        && await _relocationService.IsBoundaryProtectedAsync(
                            sourceBasePath,
                            sourceSemantics,
                            lockedCancellationToken))))
            {
                throw new ApplicationConflictException(
                    "move_relocation_conflict",
                    "Move source or target overlaps an active root folder relocation boundary.");
            }

            var rewritten = await _repo.RewritePathReferencesAsync(
                audiobookId,
                sourceBasePath,
                destination.Path,
                sourceSemantics,
                destination.TargetBoundary.Semantics,
                lockedCancellationToken);
            if (!rewritten)
            {
                throw new ApplicationNotFoundException("audiobook_not_found", "Audiobook not found");
            }

            _logger.LogInformation(
                "Updated BasePath for audiobook {AudiobookId} without moving files: {BasePath}",
                audiobookId,
                destination.Path);

            return new AudiobookDestinationRewriteResult(audiobookId, destination.Path, sourceBasePath);
        }, cancellationToken);
    }

    private async Task<ResolvedDestination> ResolveDestinationAsync(
        string? destinationPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(destinationPath))
        {
            throw new ApplicationValidationException(
                "destination_path_required",
                "DestinationPath is required");
        }

        // User-entered destination paths must be validated as Listenarr-owned paths.
        // This deliberately does not apply to download-client-reported source paths,
        // where leading/trailing whitespace can be part of the external filesystem identity.
        if (FileUtils.HasLeadingWhitespaceBeforeRootedPath(destinationPath))
        {
            throw new ApplicationValidationException(
                "destination_path_invalid",
                "DestinationPath is invalid: leading whitespace before an absolute path is not allowed.");
        }

        var settings = await _configService.GetApplicationSettingsAsync();
        var rootFolders = await _rootFolderService.GetAllAsync();

        var allowedMoveRoots = new List<MoveRootBoundary>();
        var normalizedOutputPath = TryNormalizeMoveRoot(settings.OutputPath, "configured output path");
        await AddAllowedMoveRootAsync(
            allowedMoveRoots,
            normalizedOutputPath,
            FileSystemCaseSensitivityMode.Auto,
            cancellationToken);

        string? defaultRootPath = null;
        foreach (var rootFolder in rootFolders)
        {
            var normalizedRootPath = TryNormalizeMoveRoot(rootFolder.Path, $"root folder {rootFolder.Id}");
            if (normalizedRootPath == null)
            {
                continue;
            }

            await AddAllowedMoveRootAsync(
                allowedMoveRoots,
                normalizedRootPath,
                rootFolder.CaseSensitivityMode,
                cancellationToken);
            if (rootFolder.IsDefault && defaultRootPath == null)
            {
                defaultRootPath = normalizedRootPath;
            }
        }

        if (allowedMoveRoots.Count == 0)
        {
            throw new ApplicationValidationException(
                "destination_path_outside_roots",
                "DestinationPath must be inside a configured root folder or output path");
        }

        var destinationIsRooted = Path.IsPathRooted(destinationPath);
        var relativeMoveBase = normalizedOutputPath ?? defaultRootPath ?? allowedMoveRoots.FirstOrDefault()?.Path;
        if (!destinationIsRooted && string.IsNullOrEmpty(relativeMoveBase))
        {
            throw new ApplicationValidationException(
                "destination_path_requires_root",
                "DestinationPath requires a configured root folder or output path");
        }

        var destinationCandidate = destinationIsRooted
            ? destinationPath
            : FileUtils.CombineWithOptionalBase(relativeMoveBase, destinationPath);
        if (!FileUtils.TryNormalizeUserProvidedDirectoryPathForCurrentOs(
            destinationCandidate,
            out var final,
            out var validationReason,
            rejectParentTraversal: true))
        {
            throw new ApplicationValidationException(
                "destination_path_invalid",
                $"DestinationPath is invalid: {validationReason}");
        }

        if (!_fileSystem.TryValidateMutationTarget(final, allowedMoveRoots.Select(root => root.Path), out final, out var finalReason))
        {
            _logger.LogWarning(
                "Blocked metadata-only destination rewrite: {Destination}. Reason: {Reason}",
                LogRedaction.SanitizeFilePath(final),
                finalReason);
            throw new ApplicationValidationException(
                "destination_path_outside_roots",
                "DestinationPath must be inside a configured root folder or output path");
        }

        var targetBoundary = FindAllowedMoveRoot(final, allowedMoveRoots);
        if (targetBoundary == null)
        {
            throw new ApplicationValidationException(
                "destination_filesystem_identity_unavailable",
                "Destination filesystem identity is unavailable.");
        }

        return new ResolvedDestination(final, targetBoundary, allowedMoveRoots);
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
        FileSystemCaseSensitivityMode caseSensitivityMode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(normalizedRoot))
        {
            return;
        }

        var resolution = await _semanticsResolver.ResolveAsync(
            normalizedRoot,
            caseSensitivityMode,
            cancellationToken);
        if (resolution.State != PathIdentityState.Valid)
        {
            _logger.LogWarning(
                "Skipping move boundary {Root}: {Reason}",
                LogRedaction.SanitizeFilePath(normalizedRoot),
                resolution.Reason ?? "filesystem identity unavailable");
            return;
        }

        var existingIndex = allowedRoots.FindIndex(root => FileSystemPathIdentity.AreEquivalent(
            root.Path,
            normalizedRoot,
            resolution.Semantics));
        if (existingIndex >= 0)
        {
            // A configured root-folder override is authoritative when the same path was
            // already contributed by the legacy output-path setting in Auto mode.
            if (caseSensitivityMode != FileSystemCaseSensitivityMode.Auto
                && allowedRoots[existingIndex].CaseSensitivityMode == FileSystemCaseSensitivityMode.Auto)
            {
                allowedRoots[existingIndex] = new MoveRootBoundary(
                    normalizedRoot,
                    resolution.Semantics,
                    caseSensitivityMode);
            }

            return;
        }

        allowedRoots.Add(new MoveRootBoundary(
            normalizedRoot,
            resolution.Semantics,
            caseSensitivityMode));
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

    private sealed record MoveRootBoundary(
        string Path,
        FileSystemPathSemantics Semantics,
        FileSystemCaseSensitivityMode CaseSensitivityMode);

    private sealed record ResolvedDestination(
        string Path,
        MoveRootBoundary TargetBoundary,
        IReadOnlyCollection<MoveRootBoundary> AllowedMoveRoots);
}
