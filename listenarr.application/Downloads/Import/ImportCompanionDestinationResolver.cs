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

namespace Listenarr.Application.Downloads.Import;

/// <summary>
/// Decides where a companion file belongs relative to an audiobook base path.
///
/// An import batch can span disjoint trees. Archive extraction puts the archive's contents
/// under a temporary directory and leaves the files the release shipped loose in the download
/// directory, so the batch's common directory climbs to whatever those two have in common.
/// On the stock container layout that is the filesystem root, and relativizing against it
/// hands back each companion's own absolute path with the root stripped off, which no
/// containment check can reject; the source tree is then recreated inside the library folder.
/// On a host where the temporary directory and the download directory share a parent it is
/// that parent, which produces a shorter tree in the same place.
///
/// The batch never had one source structure to reproduce, so this resolver does not look for
/// one. Each extraction directory is its own root, the files that stayed in the download
/// directory share theirs, and a companion is mirrored only under the root it actually came
/// from. A companion that belongs to no root falls back to travelling with the audio file
/// imported out of its own directory, as
/// <c>ManualImportCompanionImporter.TryResolveCompanionDestination</c> does, and is refused if
/// there is no such file.
/// </summary>
public static class ImportCompanionDestinationResolver
{
    /// <summary>
    /// Resolves the companion's path relative to <paramref name="basePath"/>, or returns false
    /// when the companion cannot be placed without inventing structure for it.
    /// </summary>
    public static bool TryResolveRelativeDestination(
        IReadOnlyCollection<string> batchFiles,
        IReadOnlyCollection<string> extractionRoots,
        string companionFile,
        string? basePath,
        IReadOnlyCollection<ImportResult> results,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics destinationSemantics,
        out string relativePath)
    {
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(companionFile) || string.IsNullOrWhiteSpace(basePath))
        {
            return false;
        }

        try
        {
            var companion = FileSystemPathIdentity.ResolveNativeAbsolutePath(companionFile);
            var sourceRoot = ResolveSourceRoot(batchFiles, extractionRoots, companion, sourceSemantics);
            if (TryMirrorUnderSourceRoot(
                    sourceRoot,
                    companion,
                    sourceSemantics,
                    destinationSemantics,
                    out relativePath))
            {
                return true;
            }

            return TryPlaceBesideImportedAudio(
                companion,
                basePath,
                results,
                sourceSemantics,
                destinationSemantics,
                out relativePath);
        }
        catch (Exception exception) when (exception is not (
            OperationCanceledException or OutOfMemoryException or StackOverflowException))
        {
            relativePath = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// The root whose structure this companion's position is meaningful against: the archive
    /// it was extracted from, or the directory the batch's un-extracted files share.
    /// </summary>
    private static string? ResolveSourceRoot(
        IReadOnlyCollection<string> batchFiles,
        IReadOnlyCollection<string> extractionRoots,
        string companion,
        FileSystemPathSemantics sourceSemantics)
    {
        var owningExtractionRoot = extractionRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .FirstOrDefault(root => FileSystemPathIdentity.IsSameOrInside(
                companion,
                root,
                sourceSemantics));
        if (owningExtractionRoot != null)
        {
            return owningExtractionRoot;
        }

        return FileUtils.GetCommonDirectory(batchFiles.Where(file =>
            !string.IsNullOrWhiteSpace(file)
            && !extractionRoots.Any(root =>
                !string.IsNullOrWhiteSpace(root)
                && FileSystemPathIdentity.IsSameOrInside(file, root, sourceSemantics))));
    }

    /// <summary>
    /// Mirrors the companion's position under its own source root. A root that is a bare
    /// filesystem root describes no structure and is refused rather than mirrored; that is what
    /// <see cref="FileUtils.GetCommonPathForDirectories(IEnumerable{string})"/> returns for
    /// inputs in disjoint trees.
    /// </summary>
    private static bool TryMirrorUnderSourceRoot(
        string? sourceRootPath,
        string companion,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics destinationSemantics,
        out string relativePath)
    {
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(sourceRootPath)
            || FileUtils.IsFilesystemRoot(sourceRootPath, sourceSemantics))
        {
            return false;
        }

        if (!FileSystemPathIdentity.TryGetRelativePathWithinBase(
                FileSystemPathIdentity.ResolveNativeAbsolutePath(sourceRootPath),
                companion,
                sourceSemantics,
                out var withinRoot)
            || string.IsNullOrEmpty(withinRoot))
        {
            return false;
        }

        relativePath = FileSystemPathIdentity.ConvertRelativePathSyntax(
            withinRoot,
            sourceSemantics.Syntax,
            destinationSemantics.Syntax);
        return true;
    }

    /// <summary>
    /// Places the companion, by name only, in the destination directory of an audio file that
    /// was imported out of the same source directory. A companion with no such neighbour has
    /// nowhere to go and is refused.
    /// </summary>
    private static bool TryPlaceBesideImportedAudio(
        string companion,
        string basePath,
        IReadOnlyCollection<ImportResult> results,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics destinationSemantics,
        out string relativePath)
    {
        relativePath = string.Empty;
        var companionDirectory = Path.GetDirectoryName(companion);
        if (string.IsNullOrWhiteSpace(companionDirectory))
        {
            return false;
        }

        var neighbour = results.FirstOrDefault(result =>
            result.Success
            && !string.IsNullOrWhiteSpace(result.SourcePath)
            && !string.IsNullOrWhiteSpace(result.FinalPath)
            && FileUtils.IsAudioFile(result.SourcePath!)
            && sourceSemantics.Comparer.Equals(
                Path.GetDirectoryName(
                    FileSystemPathIdentity.ResolveNativeAbsolutePath(result.SourcePath!)),
                companionDirectory));
        if (neighbour == null)
        {
            return false;
        }

        var neighbourDirectory = Path.GetDirectoryName(
            FileSystemPathIdentity.ResolveNativeAbsolutePath(neighbour.FinalPath!));
        if (string.IsNullOrWhiteSpace(neighbourDirectory)
            || !FileSystemPathIdentity.TryGetRelativePathWithinBase(
                basePath,
                neighbourDirectory,
                destinationSemantics,
                out var relativeDirectory))
        {
            return false;
        }

        var fileName = Path.GetFileName(companion);
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        var separator = destinationSemantics.Syntax == FileSystemPathSyntax.Windows ? '\\' : '/';
        relativePath = string.IsNullOrEmpty(relativeDirectory)
            ? fileName
            : relativeDirectory.TrimEnd('/', '\\') + separator + fileName;
        return true;
    }
}
