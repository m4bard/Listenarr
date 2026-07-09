/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Moving;

internal static class MovedAudiobookPathRewriter
{
    public static Task RewriteAsync(
        Audiobook audiobook,
        string source,
        string target,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics,
        IAudiobookRepository audiobookRepository,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(audiobook);
        ArgumentNullException.ThrowIfNull(audiobookRepository);
        ArgumentNullException.ThrowIfNull(logger);

        AudiobookPathReferenceRewriter.Rewrite(
            audiobook,
            source,
            target,
            sourceSemantics,
            targetSemantics);

        logger.LogInformation(
            "Rewrote stored path references for audiobook {AudiobookId} after physical move",
            audiobook.Id);

        return Task.CompletedTask;
    }
}
