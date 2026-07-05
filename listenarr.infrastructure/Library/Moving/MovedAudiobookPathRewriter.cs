/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Moving;

internal static class MovedAudiobookPathRewriter
{
    public static async Task RewriteAsync(
        Audiobook audiobook,
        string source,
        string target,
        FileSystemPathSemantics semantics,
        IAudiobookRepository audiobookRepository,
        ILogger logger)
    {
        await RewriteImagePathAsync(audiobook, source, target, semantics, audiobookRepository, logger);
        await RewriteLegacyFilePathAsync(audiobook, source, target, semantics, audiobookRepository, logger);
    }

    private static async Task RewriteImagePathAsync(
        Audiobook audiobook,
        string source,
        string target,
        FileSystemPathSemantics semantics,
        IAudiobookRepository audiobookRepository,
        ILogger logger)
    {
        try
        {
            var imageUrl = audiobook.ImageUrl;
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return;
            }

            var looksLikeFileSystemPath = Path.IsPathRooted(imageUrl)
                || IsSameOrInside(imageUrl, source, semantics)
                || IsSameOrInside(
                    imageUrl.Replace('/', Path.DirectorySeparatorChar),
                    source,
                    semantics);
            if (!looksLikeFileSystemPath)
            {
                return;
            }

            var fullImagePath = Path.IsPathRooted(imageUrl)
                ? Path.GetFullPath(imageUrl)
                : Path.GetFullPath(Path.Join(source, imageUrl));
            if (!IsSameOrInside(fullImagePath, source, semantics))
            {
                return;
            }

            if (FileSystemPathIdentity.TryGetRelativePathWithinBase(source, fullImagePath, semantics, out var relativePath)
                && FileSystemPathIdentity.TryResolveRelativePathWithinBase(target, relativePath, semantics, out var newImagePath)
                && File.Exists(newImagePath))
            {
                audiobook.ImageUrl = newImagePath;
                await audiobookRepository.UpdateAsync(audiobook);
                logger.LogInformation(
                    "Updated ImageUrl for audiobook {AudiobookId} to new path after move",
                    audiobook.Id);
            }
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
        {
            logger.LogDebug(
                exception,
                "Non-fatal: failed to update ImageUrl after move for audiobook {AudiobookId}",
                audiobook.Id);
        }
    }

    private static async Task RewriteLegacyFilePathAsync(
        Audiobook audiobook,
        string source,
        string target,
        FileSystemPathSemantics semantics,
        IAudiobookRepository audiobookRepository,
        ILogger logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(audiobook.FilePath))
            {
                return;
            }

            var fullFilePath = Path.IsPathRooted(audiobook.FilePath)
                ? Path.GetFullPath(audiobook.FilePath)
                : Path.GetFullPath(Path.Join(source, audiobook.FilePath));
            if (!IsSameOrInside(fullFilePath, source, semantics))
            {
                return;
            }

            if (FileSystemPathIdentity.TryGetRelativePathWithinBase(source, fullFilePath, semantics, out var relativePath)
                && FileSystemPathIdentity.TryResolveRelativePathWithinBase(target, relativePath, semantics, out var newFilePath)
                && File.Exists(newFilePath))
            {
                audiobook.FilePath = newFilePath;
                await audiobookRepository.UpdateAsync(audiobook);
                logger.LogInformation(
                    "Updated FilePath for audiobook {AudiobookId} to new path after move",
                    audiobook.Id);
            }
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
        {
            logger.LogDebug(
                exception,
                "Non-fatal: failed to update FilePath after move for audiobook {AudiobookId}",
                audiobook.Id);
        }
    }

    private static bool IsSameOrInside(
        string path,
        string rootPath,
        FileSystemPathSemantics semantics)
    {
        return !string.IsNullOrWhiteSpace(path)
            && !string.IsNullOrWhiteSpace(rootPath)
            && FileSystemPathIdentity.IsSameOrInside(path, rootPath, semantics);
    }
}
