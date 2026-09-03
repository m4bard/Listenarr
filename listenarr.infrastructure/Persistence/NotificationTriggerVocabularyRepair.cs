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
namespace Listenarr.Infrastructure.Persistence;

/// <summary>
/// Rewrites stored webhook trigger selections into the canonical vocabulary.
/// <para>
/// This is the data half of the vocabulary change. It is a startup primitive rather than an EF
/// migration because <c>MigrationProvenanceArchitectureTests</c> requires every migration added
/// after the #717 frontier to remain a direct EF scaffold, and directs data repair here.
/// </para>
/// <para>
/// The transform is a rename and nothing more: no stored selection changes which event it
/// subscribes to. Values that are already canonical and values that are not recognised at all are
/// left exactly as they were found, so the repair converges after one pass and applying it to its
/// own output changes nothing.
/// </para>
/// </summary>
internal static class NotificationTriggerVocabularyRepair
{
    public static NotificationTriggerVocabularyRepairResult Normalize(ListenArrDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var settingsRows = context.ApplicationSettings.ToList();
        if (settingsRows.Count == 0)
        {
            return default;
        }

        var legacyChannelRepaired = 0;
        var perWebhookChannelRepaired = 0;

        foreach (var settings in settingsRows)
        {
            var enabled = NotificationTriggers.NormalizeAll(settings.EnabledNotificationTriggers);
            if (!SameSequence(settings.EnabledNotificationTriggers, enabled))
            {
                settings.EnabledNotificationTriggers = enabled;
                legacyChannelRepaired++;
            }

            foreach (var webhook in settings.Webhooks ?? [])
            {
                var triggers = NotificationTriggers.NormalizeAll(webhook.Triggers);
                if (SameSequence(webhook.Triggers, triggers))
                {
                    continue;
                }

                webhook.Triggers = triggers;
                perWebhookChannelRepaired++;
            }
        }

        if (legacyChannelRepaired == 0 && perWebhookChannelRepaired == 0)
        {
            return default;
        }

        using var transaction = context.Database.BeginTransaction();
        context.SaveChanges();
        transaction.Commit();

        return new NotificationTriggerVocabularyRepairResult(
            legacyChannelRepaired,
            perWebhookChannelRepaired);
    }

    private static bool SameSequence(List<string>? current, List<string> normalized)
    {
        if (current == null)
        {
            return normalized.Count == 0;
        }

        return current.SequenceEqual(normalized, StringComparer.Ordinal);
    }
}

internal readonly record struct NotificationTriggerVocabularyRepairResult(
    int LegacyChannelSelectionsRepaired,
    int PerWebhookSelectionsRepaired);
