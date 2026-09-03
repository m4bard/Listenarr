/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Common
{
    /// <summary>
    /// Startup repair for webhook rows written by a settings UI that offered legacy
    /// global-webhook trigger names. Those rows are gated on names no dispatch site reads,
    /// so without this the webhook is stored, enabled, and permanently silent.
    /// </summary>
    public class WebhookTriggerMigrator : IWebhookTriggerMigrator
    {
        private readonly IConfigurationService _configurationService;
        private readonly ILogger<WebhookTriggerMigrator> _logger;

        public WebhookTriggerMigrator(IConfigurationService configurationService, ILogger<WebhookTriggerMigrator> logger)
        {
            _configurationService = configurationService;
            _logger = logger;
        }

        public async Task MigrateAsync()
        {
            try
            {
                var settings = await _configurationService.GetApplicationSettingsAsync();
                if (settings?.Webhooks == null || settings.Webhooks.Count == 0)
                {
                    _logger.LogDebug("No webhooks configured; skipping webhook trigger migration");
                    return;
                }

                var migratedAny = false;
                foreach (var webhook in settings.Webhooks)
                {
                    var result = WebhookTriggerVocabulary.Canonicalize(webhook.Triggers);
                    if (!result.Changed)
                    {
                        continue;
                    }

                    migratedAny = true;
                    webhook.Triggers = [.. result.Triggers];
                    ReportRewrite(webhook, result);
                }

                if (!migratedAny)
                {
                    _logger.LogDebug("All webhook triggers already use dispatch trigger names; nothing to migrate");
                    return;
                }

                await _configurationService.SaveApplicationSettingsAsync(settings);
                _logger.LogInformation(
                    "Migrated webhook trigger subscriptions onto the trigger names notification dispatch reads ({Deliverable})",
                    string.Join(", ", WebhookTriggerVocabulary.Offered));
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogWarning(ex, "Failed to migrate webhook trigger subscriptions; webhooks may not fire until their triggers are re-selected");
            }
        }

        private void ReportRewrite(WebhookConfiguration webhook, WebhookTriggerCanonicalizationResult result)
        {
            foreach (var pair in result.Remapped)
            {
                _logger.LogWarning(
                    "Webhook '{Webhook}' subscribed to retired trigger '{Retired}', which no dispatch site raises; migrated it to '{Deliverable}'",
                    webhook.Name,
                    pair.Key,
                    pair.Value);
            }

            if (result.Dropped.Count > 0)
            {
                _logger.LogWarning(
                    "Webhook '{Webhook}' subscribed to trigger(s) {Dropped}, which raise nothing against a webhook and have no equivalent; removed them",
                    webhook.Name,
                    string.Join(", ", result.Dropped));
            }

            if (webhook.Triggers.Count == 0)
            {
                _logger.LogWarning(
                    "Webhook '{Webhook}' has no deliverable triggers left and will not fire until triggers are re-selected under Settings then Notifications",
                    webhook.Name);
            }
        }
    }
}
