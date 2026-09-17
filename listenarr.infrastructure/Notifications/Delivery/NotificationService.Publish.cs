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

            if (!await SubscriberDeliveryIsEnabledAsync())
            {
                return;
            }

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
        /// Reads the EnableNotifications master switch, which gates the whole fan-out rather than
        /// any one subscriber.
        /// </summary>
        /// <remarks>
        /// A failed read suppresses delivery rather than assuming the switch is on. A subscriber here
        /// runs an operator-authored executable, so the cost of guessing wrong in one direction is a
        /// dropped notification and in the other is running a process the operator may have asked us
        /// not to run. The read failure is swallowed for the same reason a subscriber failure is: a
        /// notification is a side effect of an operation and may not break it.
        /// </remarks>
        private async Task<bool> SubscriberDeliveryIsEnabledAsync()
        {
            try
            {
                var settings = await _configurationService.GetApplicationSettingsAsync();
                if (settings?.EnableNotifications == true)
                {
                    return true;
                }

                _logger.LogDebug("Notifications are disabled, so no subscriber will be called for this event");
                return false;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            // Intentional broad catch: see the remarks above. Suppressing delivery is the chosen
            // behaviour on an unreadable setting, and the caller must not learn about it.
#pragma warning disable CA1031
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogDebug(ex, "Could not read the notification settings, so no subscriber will be called for this event");
                return false;
            }
#pragma warning restore CA1031
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
