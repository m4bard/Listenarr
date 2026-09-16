/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Domain.Notifications;

namespace Listenarr.Application.Notifications.Contracts
{
    /// <summary>
    /// The audiobook an event concerns.
    /// </summary>
    public sealed record NotificationEventBook
    {
        public int Id { get; init; }
        public string Title { get; init; } = string.Empty;
        public string? Asin { get; init; }
        public IReadOnlyList<string> Authors { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Narrators { get; init; } = Array.Empty<string>();
        public string? Publisher { get; init; }
        public int? Year { get; init; }
    }

    /// <summary>
    /// The indexer release an event concerns, where one is involved.
    /// </summary>
    public sealed record NotificationEventRelease
    {
        public string? Title { get; init; }
        public string? Indexer { get; init; }
        public long? Size { get; init; }
        public string? Quality { get; init; }
        public string? Protocol { get; init; }
    }

    /// <summary>
    /// The download client transfer an event concerns, where one is involved.
    /// </summary>
    public sealed record NotificationEventDownload
    {
        public string? Id { get; init; }
        public string? Client { get; init; }
        public string? ClientType { get; init; }
        public string? ErrorMessage { get; init; }
    }

    /// <summary>
    /// A single notification, published once by the thing that caused it and delivered to every
    /// subscriber configured for its <see cref="Channel"/>.
    /// </summary>
    /// <remarks>
    /// The payload is typed rather than <c>object</c> because subscribers that are not posting JSON
    /// need named fields: a custom script has to turn this into environment variables, and cannot
    /// do that by reflecting over an anonymous type whose shape each publisher chooses for itself.
    /// </remarks>
    public sealed record NotificationEvent
    {
        public required NotificationChannel Channel { get; init; }
        public NotificationEventBook? Book { get; init; }
        public NotificationEventRelease? Release { get; init; }
        public NotificationEventDownload? Download { get; init; }

        /// <summary>Files added to the library by this event.</summary>
        public IReadOnlyList<string> AddedPaths { get; init; } = Array.Empty<string>();

        /// <summary>Where the audiobook's files were before a <see cref="NotificationChannel.Rename"/>.</summary>
        public string? SourcePath { get; init; }

        /// <summary>Where the audiobook's files are after a <see cref="NotificationChannel.Rename"/>.</summary>
        public string? DestinationPath { get; init; }

        /// <summary>Free text for channels that carry no structured subject.</summary>
        public string? Message { get; init; }

        public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    }
}
