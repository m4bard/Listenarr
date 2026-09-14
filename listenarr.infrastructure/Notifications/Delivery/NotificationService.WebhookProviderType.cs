/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
namespace Listenarr.Infrastructure.Notifications.Delivery
{
    public partial class NotificationService : INotificationService
    {
        private enum WebhookProviderType
        {
            Generic,
            Discord,
            Ntfy,
            Pushover,
            Telegram,
            Pushbullet,
            Slack,
        }

        /// <summary>
        /// Picks the webhook provider to dispatch to. A recognized, non-blank
        /// WebhookConfiguration.Type is authoritative; a blank/legacy or unrecognized Type
        /// (including the single ApplicationSettings.WebhookUrl path, which has no Type at all)
        /// falls back to sniffing the URL, which is how every webhook was dispatched before Type
        /// was read at all.
        /// </summary>
        private static WebhookProviderType ResolveWebhookProviderType(string? webhookType, string webhookUrl)
        {
            if (!string.IsNullOrWhiteSpace(webhookType))
            {
                switch (webhookType.Trim().ToLowerInvariant())
                {
                    case "discord":
                        return WebhookProviderType.Discord;
                    case "ntfy":
                        return WebhookProviderType.Ntfy;
                    case "pushover":
                        return WebhookProviderType.Pushover;
                    case "telegram":
                        return WebhookProviderType.Telegram;
                    case "pushbullet":
                        return WebhookProviderType.Pushbullet;
                    case "slack":
                        return WebhookProviderType.Slack;
                    case "zapier":
                        // Zapier has no provider-specific handling; it always used the generic
                        // fallback, even back when dispatch only ever sniffed the URL.
                        return WebhookProviderType.Generic;
                }
                // Recognized-but-unmatched falls through to URL sniffing below, same as blank.
            }

            if (webhookUrl.Contains("discord.com/api/webhooks", StringComparison.OrdinalIgnoreCase))
                return WebhookProviderType.Discord;
            if (webhookUrl.IndexOf("ntfy", StringComparison.OrdinalIgnoreCase) >= 0)
                return WebhookProviderType.Ntfy;
            if (webhookUrl.IndexOf("api.pushover.net/1/messages.json", StringComparison.OrdinalIgnoreCase) >= 0)
                return WebhookProviderType.Pushover;
            if (webhookUrl.IndexOf("api.telegram.org/bot", StringComparison.OrdinalIgnoreCase) >= 0)
                return WebhookProviderType.Telegram;
            if (webhookUrl.IndexOf("api.pushbullet.com/v2/pushes", StringComparison.OrdinalIgnoreCase) >= 0 || webhookUrl.StartsWith("pushbullet://", StringComparison.OrdinalIgnoreCase))
                return WebhookProviderType.Pushbullet;
            if (webhookUrl.IndexOf("hooks.slack.com/services", StringComparison.OrdinalIgnoreCase) >= 0)
                return WebhookProviderType.Slack;

            return WebhookProviderType.Generic;
        }
    }
}
