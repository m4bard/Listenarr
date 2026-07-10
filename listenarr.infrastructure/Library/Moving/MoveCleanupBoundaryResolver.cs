/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Domain.Common;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed class MoveCleanupBoundaryResolver(
    IFileSystemSemanticsResolver semanticsResolver) : IMoveCleanupBoundaryResolver
{
    public async Task<MoveCleanupBoundaryResolution> ResolveAsync(
        string source,
        string target,
        IReadOnlyCollection<RootFolder> configuredRoots,
        string? persistedBoundary = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(configuredRoots);

        string sourceFullPath;
        string targetFullPath;
        try
        {
            sourceFullPath = FileSystemPathIdentity.ResolveNativeAbsolutePath(source);
            targetFullPath = FileSystemPathIdentity.ResolveNativeAbsolutePath(target);
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or NotSupportedException or PathTooLongException)
        {
            return Unavailable($"Move paths could not be normalized: {exception.Message}");
        }

        var sourceResolution = await semanticsResolver.ResolveAsync(
            sourceFullPath,
            cancellationToken: cancellationToken);
        if (sourceResolution.State != PathIdentityState.Valid)
        {
            return Unavailable(
                sourceResolution.Reason ?? "Source filesystem identity is unavailable.");
        }

        var semantics = sourceResolution.Semantics;
        var sourceParent = Path.GetDirectoryName(sourceFullPath);
        if (string.IsNullOrWhiteSpace(sourceParent))
        {
            return Unavailable("The source path has no removable parent directory.");
        }

        if (!string.IsNullOrWhiteSpace(persistedBoundary))
        {
            return ValidatePersistedBoundary(
                sourceParent,
                persistedBoundary,
                semantics);
        }

        var configuredBoundary = FindDeepestConfiguredBoundary(
            sourceFullPath,
            configuredRoots,
            semantics);
        if (configuredBoundary != null)
        {
            return new MoveCleanupBoundaryResolution(
                configuredBoundary,
                MoveCleanupBoundaryKind.ConfiguredRoot);
        }

        var commonAncestor = FindDeepestCommonAncestor(
            sourceFullPath,
            targetFullPath,
            semantics);
        if (commonAncestor != null)
        {
            return new MoveCleanupBoundaryResolution(
                commonAncestor,
                MoveCleanupBoundaryKind.CommonAncestor);
        }

        var volumeAnchor = FindSourceVolumeAnchor(
            sourceFullPath,
            sourceParent,
            semantics);
        if (volumeAnchor != null)
        {
            return new MoveCleanupBoundaryResolution(
                volumeAnchor,
                MoveCleanupBoundaryKind.VolumeAnchor);
        }

        return Unavailable(
            "No configured source root, safe common ancestor, or source volume anchor could be established.");
    }

    private static MoveCleanupBoundaryResolution ValidatePersistedBoundary(
        string sourceParent,
        string persistedBoundary,
        FileSystemPathSemantics semantics)
    {
        try
        {
            if (!Path.IsPathFullyQualified(persistedBoundary))
            {
                return Unavailable(
                    "The persisted source cleanup boundary is not an absolute path for this host.");
            }

            var boundary = Path.GetFullPath(persistedBoundary);
            if (!FileSystemPathIdentity.IsSameOrInside(sourceParent, boundary, semantics))
            {
                return Unavailable(
                    "The persisted source cleanup boundary no longer contains the source path.");
            }

            return new MoveCleanupBoundaryResolution(
                boundary,
                MoveCleanupBoundaryKind.Persisted);
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or NotSupportedException or PathTooLongException)
        {
            return Unavailable(
                $"The persisted source cleanup boundary is invalid: {exception.Message}");
        }
    }

    private static string? FindDeepestConfiguredBoundary(
        string source,
        IEnumerable<RootFolder> configuredRoots,
        FileSystemPathSemantics semantics)
    {
        var containingRoots = new List<(string Path, int CanonicalLength)>();
        foreach (var root in configuredRoots)
        {
            if (string.IsNullOrWhiteSpace(root.Path))
            {
                continue;
            }

            try
            {
                if (!FileSystemPathIdentity.IsSameOrInside(source, root.Path, semantics))
                {
                    continue;
                }

                containingRoots.Add((
                    Path.GetFullPath(root.Path),
                    FileSystemPathIdentity.Canonicalize(root.Path, semantics.Syntax).Length));
            }
            catch (Exception exception) when (exception is
                ArgumentException or InvalidOperationException or NotSupportedException or PathTooLongException)
            {
                // Stored roots from another host or filesystem syntax are ignored. They must
                // not block a valid move or broaden cleanup to an unrelated path.
            }
        }

        return containingRoots
            .OrderByDescending(root => root.CanonicalLength)
            .Select(root => root.Path)
            .FirstOrDefault();
    }

    private static string? FindDeepestCommonAncestor(
        string source,
        string target,
        FileSystemPathSemantics semantics)
    {
        var candidate = source;
        while (!string.IsNullOrWhiteSpace(candidate))
        {
            try
            {
                if (FileSystemPathIdentity.IsSameOrInside(target, candidate, semantics))
                {
                    return IsFilesystemRoot(candidate, semantics)
                        ? null
                        : candidate;
                }
            }
            catch (ArgumentException)
            {
                return null;
            }

            candidate = Path.GetDirectoryName(candidate);
        }

        return null;
    }

    private static string? FindSourceVolumeAnchor(
        string source,
        string sourceParent,
        FileSystemPathSemantics semantics)
    {
        var volumeRoot = ResolveVolumeRoot(source, semantics);
        if (string.IsNullOrWhiteSpace(volumeRoot))
        {
            return null;
        }

        if (semantics.Syntax == FileSystemPathSyntax.Unix
            && FileSystemPathIdentity.AreEquivalent(volumeRoot, "/", semantics))
        {
            // The host root is too broad to infer a user-owned library boundary safely.
            return null;
        }

        string relativePath;
        try
        {
            relativePath = Path.GetRelativePath(volumeRoot, source);
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var firstSegment = relativePath
            .Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(segment => segment is not "." and not "..");
        if (string.IsNullOrWhiteSpace(firstSegment))
        {
            return null;
        }

        var anchor = Path.Combine(volumeRoot, firstSegment);
        return FileSystemPathIdentity.IsSameOrInside(sourceParent, anchor, semantics)
            && !FileSystemPathIdentity.AreEquivalent(anchor, volumeRoot, semantics)
                ? anchor
                : null;
    }

    private static string? ResolveVolumeRoot(
        string source,
        FileSystemPathSemantics semantics)
    {
        if (semantics.Syntax == FileSystemPathSyntax.Windows)
        {
            return Path.GetPathRoot(source);
        }

        try
        {
            return new DriveInfo(source).RootDirectory.FullName;
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsFilesystemRoot(
        string path,
        FileSystemPathSemantics semantics)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return !string.IsNullOrWhiteSpace(root)
            && FileSystemPathIdentity.AreEquivalent(fullPath, root, semantics);
    }

    private static MoveCleanupBoundaryResolution Unavailable(string reason) =>
        new(null, MoveCleanupBoundaryKind.Unavailable, reason);
}
