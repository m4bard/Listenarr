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

internal sealed partial class AudiobookContentMoveService
{
    public void FinalizeMove(
        AudiobookContentMoveRequest request,
        AudiobookContentMoveResult result)
    {
        if (!result.SourceCleanupCompleted)
        {
            throw new InvalidOperationException(
                "Move finalization cannot run before source cleanup completes.");
        }

        if (request.DeleteEmptySource && !Directory.Exists(result.Source))
        {
            var nearestExistingAncestor = FindNearestExistingAncestor(result.Source);
            var hasEmptyAncestorToPrune = nearestExistingAncestor != null
                && !IsFilesystemRoot(nearestExistingAncestor, request.SourceSemantics)
                && !Directory.EnumerateFileSystemEntries(nearestExistingAncestor).Any();
            if (hasEmptyAncestorToPrune
                && string.IsNullOrWhiteSpace(request.SourceCleanupBoundary))
            {
                throw new MoveNeedsAttentionException(
                    "Files were moved successfully, but empty source-parent cleanup could not be completed safely because no source cleanup boundary is available.");
            }

            if (!string.IsNullOrWhiteSpace(request.SourceCleanupBoundary))
            {
                // Keep the recovery marker until pruning succeeds so transient filesystem
                // failures remain retryable instead of leaving an orphaned empty folder.
                RemoveEmptySourceAncestors(
                    result.Source,
                    request.SourceCleanupBoundary,
                    request.SourceSemantics);
            }
        }

        var tempOwnership = TryValidatePublishedTempOwnership(
            result.Target,
            request,
            result.Source,
            result.Target);
        TryDeletePublishedTempOwnershipMarker(tempOwnership);

        if (File.Exists(result.RecoveryMarkerPath))
        {
            File.Delete(result.RecoveryMarkerPath);
        }
    }

    private static string? FindNearestExistingAncestor(string source)
    {
        var current = Path.GetDirectoryName(Path.GetFullPath(source));
        while (current != null)
        {
            if (Directory.Exists(current))
            {
                return current;
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    private static void RemoveEmptyDirectoryTree(
        string directory,
        string boundary,
        FileSystemPathSemantics semantics)
    {
        var current = directory;
        while (Directory.Exists(current)
            && !FileSystemPathIdentity.AreEquivalent(
                current,
                boundary,
                semantics)
            && !Directory.EnumerateFileSystemEntries(current).Any())
        {
            Directory.Delete(current, false);
            current = Path.GetDirectoryName(current) ?? boundary;
        }
    }

    private static bool IsSourceCleanupBoundary(
        string path,
        string? boundary,
        FileSystemPathSemantics semantics)
    {
        if (string.IsNullOrWhiteSpace(boundary))
        {
            return false;
        }

        try
        {
            return FileSystemPathIdentity.AreEquivalent(path, boundary, semantics);
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or NotSupportedException or PathTooLongException)
        {
            throw new MoveNeedsAttentionException(
                $"The source cleanup boundary is invalid: {exception.Message}");
        }
    }

    private static void RemoveEmptySourceAncestors(
        string source,
        string? boundary,
        FileSystemPathSemantics semantics)
    {
        if (string.IsNullOrWhiteSpace(boundary))
        {
            return;
        }

        var fullBoundary = Path.GetFullPath(boundary);
        var current = Path.GetDirectoryName(Path.GetFullPath(source));
        while (current != null
            && FileSystemPathIdentity.IsSameOrInside(current, fullBoundary, semantics))
        {
            if (FileSystemPathIdentity.AreEquivalent(current, fullBoundary, semantics))
            {
                return;
            }

            if (Directory.Exists(current))
            {
                RemoveEmptyDirectoryTree(current, fullBoundary, semantics);
                return;
            }

            current = Path.GetDirectoryName(current);
        }
    }
}
