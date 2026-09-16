/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Domain.Notifications;

namespace Listenarr.Application.Notifications.Contracts
{
    /// <summary>
    /// One notification target implementation. Subscribers are registered once and are handed every
    /// published event whose <see cref="NotificationEvent.Channel"/> they declare support for.
    /// </summary>
    /// <remarks>
    /// A subscriber is the implementation, not the configured instance. An operator may have several
    /// configured instances of the same subscriber (three custom scripts, two Discord webhooks), each
    /// with its own settings and its own set of enabled channels; the subscriber owns that lookup and
    /// fans out across its own configured instances inside <see cref="NotifyAsync"/>.
    /// <para>
    /// <see cref="Supports"/> is a statement about the implementation, not about any one instance.
    /// It answers "could this target ever handle this channel", which is what the settings UI needs
    /// in order to grey out a toggle. Whether a particular configured instance is enabled for that
    /// channel is the instance's business.
    /// </para>
    /// </remarks>
    public interface INotificationSubscriber
    {
        /// <summary>The operator-visible name of this target, for example "Custom Script".</summary>
        string Name { get; }

        /// <summary>Whether this implementation can ever deliver the given channel.</summary>
        bool Supports(NotificationChannel channel);

        /// <summary>
        /// Deliver one event to every configured instance of this subscriber that is enabled for the
        /// event's channel. Must not throw: a failing notification target may not break the operation
        /// that produced the event.
        /// </summary>
        Task NotifyAsync(NotificationEvent notification, CancellationToken cancellationToken = default);

        /// <summary>
        /// Exercise one configured instance of this subscriber and report whether it works.
        /// </summary>
        /// <param name="configurationId">The configured instance to test.</param>
        /// <param name="cancellationToken">Cancels the test.</param>
        Task<NotificationSubscriberTestResult> TestAsync(string configurationId, CancellationToken cancellationToken = default);
    }
}
