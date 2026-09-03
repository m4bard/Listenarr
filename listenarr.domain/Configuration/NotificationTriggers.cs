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

namespace Listenarr.Domain.Configuration
{
    /// <summary>
    /// The canonical webhook trigger vocabulary.
    /// <para>
    /// Every producer of a trigger name and every consumer that filters on one draws from this
    /// class. The names are single-word PascalCase to match the webhook event vocabulary this
    /// project inherits from Readarr, where the event name is a bare noun with no separators and
    /// no entity prefix.
    /// </para>
    /// <para>
    /// The trigger name is not merely a settings key. It is written into the outbound body as the
    /// <c>event</c> field and into the NTFY <c>Tags</c> header, so it is part of the contract with
    /// whatever receives the webhook.
    /// </para>
    /// </summary>
    public static class NotificationTriggers
    {
        /// <summary>An audiobook was added to the library.</summary>
        public const string Added = "Added";

        /// <summary>A download was handed to a download client.</summary>
        public const string Downloading = "Downloading";

        /// <summary>A library scan registered new files for an audiobook.</summary>
        public const string Available = "Available";

        /// <summary>
        /// Declared so that a stored selection survives normalisation, and offered by no channel.
        /// No production call site emits this trigger; it exists in stored configuration only
        /// because the settings UI has always offered it. Whether it should gain an emitter or be
        /// withdrawn from the UI is a product decision and is deliberately not made here.
        /// </summary>
        public const string Completed = "Completed";

        /// <summary>A download completed and its files were imported.</summary>
        public const string Imported = "Imported";

        /// <summary>A download failed.</summary>
        public const string Failed = "Failed";

        /// <summary>An audiobook's files were moved between root folders.</summary>
        public const string Moved = "Moved";

        /// <summary>An application-level message not tied to a single audiobook.</summary>
        public const string System = "System";

        /// <summary>A manually requested delivery used to verify a webhook's configuration.</summary>
        public const string Test = "Test";

        /// <summary>
        /// Triggers a production call site actually emits. Every one of these reaches both the
        /// legacy single webhook and every subscribed entry of
        /// <see cref="ApplicationSettings.Webhooks"/>.
        /// </summary>
        public static IReadOnlyList<string> Emitted { get; } =
            new[] { Added, Downloading, Available, Imported, Failed, Moved };

        /// <summary>
        /// Canonical names that no production call site emits, kept so that a stored selection
        /// survives normalisation instead of being silently discarded.
        /// <list type="bullet">
        /// <item><see cref="Completed"/> has never had an emitter.</item>
        /// <item>
        /// <see cref="System"/> is emitted only by <c>SendSystemNotificationAsync</c>, which has
        /// no caller in production or in the test suite.
        /// </item>
        /// </list>
        /// Giving either an emitter, or withdrawing it from the settings UI, is a product
        /// decision and is deliberately not made here.
        /// </summary>
        public static IReadOnlyList<string> DeclaredWithoutEmitter { get; } =
            new[] { Completed, System };

        /// <summary>Every canonical trigger name, emitted or not.</summary>
        public static IReadOnlyList<string> All { get; } =
            new[] { Added, Downloading, Available, Completed, Imported, Failed, Moved, System, Test };

        private static readonly HashSet<string> CanonicalSet =
            new(All, StringComparer.Ordinal);

        /// <summary>
        /// The pre-canonical kebab-case names the settings UI wrote, mapped one to one onto their
        /// canonical spelling. This is a rename and nothing more: no stored selection changes
        /// which event it subscribes to.
        /// </summary>
        public static IReadOnlyDictionary<string, string> LegacyNames { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["book-added"] = Added,
                ["book-downloading"] = Downloading,
                ["book-available"] = Available,
                ["book-completed"] = Completed
            };

        /// <summary>
        /// True when <paramref name="trigger"/> is already a canonical name.
        /// </summary>
        public static bool IsCanonical(string? trigger) =>
            trigger != null && CanonicalSet.Contains(trigger);

        /// <summary>
        /// Returns the canonical spelling of <paramref name="trigger"/>, or the value unchanged
        /// when it is already canonical or is not recognised. Never returns null for a non-null
        /// input, so an unrecognised value survives normalisation byte for byte.
        /// </summary>
        /// <param name="trigger">The stored trigger name.</param>
        public static string Normalize(string trigger)
        {
            ArgumentNullException.ThrowIfNull(trigger);

            if (CanonicalSet.Contains(trigger))
            {
                return trigger;
            }

            return LegacyNames.TryGetValue(trigger, out var canonical) ? canonical : trigger;
        }

        /// <summary>
        /// Normalises a stored selection, preserving order and dropping duplicates. Applying
        /// this to its own output is a no-op.
        /// </summary>
        public static List<string> NormalizeAll(IEnumerable<string>? triggers)
        {
            var normalized = new List<string>();
            if (triggers == null)
            {
                return normalized;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var trigger in triggers)
            {
                if (trigger == null)
                {
                    continue;
                }

                var canonical = Normalize(trigger);
                if (seen.Add(canonical))
                {
                    normalized.Add(canonical);
                }
            }

            return normalized;
        }
    }
}
