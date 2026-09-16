/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Notifications.Delivery
{
    public partial class NotificationService : INotificationService
    {
        /// <summary>
        /// Fans one published event out to every registered subscriber that supports its channel.
        /// </summary>
        /// <remarks>
        /// Every subscriber is called exactly once per event. There is no shared control flow between
        /// subscribers and therefore nothing for a handled event to fall through into, which is the
        /// structural difference between this and the URL-matching chain in the webhook path.
        /// <para>
        /// One subscriber failing does not stop the others, and no subscriber failure reaches the
        /// caller. A notification is a side effect of an operation and may not break it.
        /// </para>
        /// </remarks>
        public async Task PublishAsync(NotificationEvent notification, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(notification);

            foreach (var subscriber in _subscribers.Where(candidate => candidate.Supports(notification.Channel)))
            {
                try
                {
                    await subscriber.NotifyAsync(notification, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                // Intentional broad catch: one target's failure must not suppress the others, and
                // must not propagate to whatever raised the event.
#pragma warning disable CA1031
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    _logger.LogError(
                        ex,
                        "Notification subscriber {Subscriber} failed handling {Channel}",
                        subscriber.Name,
                        notification.Channel);
                }
#pragma warning restore CA1031
            }
        }

        /// <summary>
        /// Maps a download onto the typed event shape. Kept beside the dispatcher so that the fields
        /// a subscriber can rely on are defined in one place rather than per publisher.
        /// </summary>
        internal static NotificationEvent FromDownload(NotificationChannel channel, Download download)
        {
            ArgumentNullException.ThrowIfNull(download);

            return new NotificationEvent
            {
                Channel = channel,
                Book = new NotificationEventBook
                {
                    Id = download.AudiobookId ?? 0,
                    Title = download.Title,
                    Asin = download.Asin,
                    Authors = string.IsNullOrWhiteSpace(download.Artist)
                        ? Array.Empty<string>()
                        : new[] { download.Artist },
                    Publisher = download.Publisher,
                },
                Download = new NotificationEventDownload
                {
                    Id = download.Id,
                    Client = download.DownloadClientId,
                    ErrorMessage = download.ErrorMessage,
                },
                AddedPaths = string.IsNullOrWhiteSpace(download.FinalPath)
                    ? Array.Empty<string>()
                    : new[] { download.FinalPath },
            };
        }
    }
}
