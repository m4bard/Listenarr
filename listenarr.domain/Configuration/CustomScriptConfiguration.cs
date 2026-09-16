/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Domain.Notifications;

namespace Listenarr.Domain.Configuration
{
    /// <summary>
    /// One configured custom script: an executable Listenarr runs when an event on one of its
    /// enabled channels is published.
    /// </summary>
    /// <remarks>
    /// Settings are per-provider rather than shared, which is why this is its own type rather than
    /// more fields on <see cref="WebhookConfiguration"/>. A custom script has no URL and a webhook
    /// has no script path.
    /// <para>
    /// There is deliberately no Arguments field. Sonarr and Readarr both still carry one and both
    /// reject any value for it ("Arguments are no longer supported for custom scripts"); copying it
    /// would be copying a deprecation.
    /// </para>
    /// </remarks>
    public class CustomScriptConfiguration
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>Operator-chosen label, shown in the settings list.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Absolute path to the executable Listenarr runs.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// The channels this script is enabled for. A channel absent from this list is not delivered,
        /// which is the per-instance toggle; whether the provider could ever deliver it at all is
        /// the provider's own capability declaration.
        /// </summary>
        public List<NotificationChannel> Channels { get; set; } = new();

        public bool IsEnabled { get; set; } = true;
    }
}
