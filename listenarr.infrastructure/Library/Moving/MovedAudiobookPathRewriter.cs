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
        int audiobookId,
        string source,
        string target,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics,
        IAudiobookRepository audiobookRepository,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audiobookRepository);
        ArgumentNullException.ThrowIfNull(logger);

        if (!await audiobookRepository.RewritePathReferencesAsync(
                audiobookId,
                source,
                target,
                sourceSemantics,
                targetSemantics,
                cancellationToken))
        {
            throw new MoveNeedsAttentionException(
                "The audiobook disappeared before its moved path references could be persisted.");
        }

        logger.LogInformation(
            "Rewrote stored path references for audiobook {AudiobookId} after physical move",
            audiobookId);
    }
}
