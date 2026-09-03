/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Domain.Configuration
{
    /// <summary>
    /// The trigger names that the two notification paths actually use.
    ///
    /// Per-webhook rows (<see cref="WebhookConfiguration.Triggers"/>) are gated on
    /// <see cref="DispatchGated"/>, of which <see cref="Offered"/> are the names a
    /// production call site actually raises. A row subscribed to anything else is inert.
    ///
    /// The legacy single webhook (<see cref="ApplicationSettings.WebhookUrl"/>, gated on
    /// <see cref="ApplicationSettings.EnabledNotificationTriggers"/>) is raised with
    /// <see cref="LegacyGlobal"/> instead. The two lists are disjoint and neither path
    /// reads the other's names.
    ///
    /// Settings UI builds before this type wrote <see cref="LegacyGlobal"/> names onto
    /// webhook rows, where no dispatch site reads them. <see cref="Canonicalize"/> repairs
    /// rows persisted by those builds.
    /// </summary>
    public static class WebhookTriggerVocabulary
    {
        /// <summary>Raised when a download reaches the imported state.</summary>
        public const string ImportedTrigger = "Imported";

        /// <summary>Raised when a download fails or is blocked from importing.</summary>
        public const string FailedTrigger = "Failed";

        /// <summary>Raised when a move job finishes relocating an audiobook.</summary>
        public const string MovedTrigger = "Moved";

        /// <summary>Raised for operator-facing system messages.</summary>
        public const string SystemTrigger = "System";

        /// <summary>Legacy global-webhook name for a book being added to the library.</summary>
        public const string LegacyBookAdded = "book-added";

        /// <summary>Legacy global-webhook name for a download being handed to a client.</summary>
        public const string LegacyBookDownloading = "book-downloading";

        /// <summary>Legacy global-webhook name for a book becoming available in the library.</summary>
        public const string LegacyBookAvailable = "book-available";

        /// <summary>
        /// Offered by earlier settings UI builds and present in the pre-existing settings
        /// default, but no production call site raises it on either path.
        /// </summary>
        public const string LegacyBookCompleted = "book-completed";

        /// <summary>
        /// Trigger names the per-webhook dispatch sites gate on. A webhook row can only ever
        /// receive one of these, and only these reach the webhook list at all: the legacy
        /// names are posted to the single <see cref="ApplicationSettings.WebhookUrl"/> and
        /// never iterate <see cref="ApplicationSettings.Webhooks"/>.
        /// </summary>
        public static IReadOnlyList<string> DispatchGated { get; } =
        [
            ImportedTrigger,
            FailedTrigger,
            MovedTrigger,
            SystemTrigger
        ];

        /// <summary>
        /// The subset of <see cref="DispatchGated"/> that a production call site actually
        /// raises. These are what the settings UI offers, because subscribing to a gated
        /// name that nothing raises promises a notification that cannot arrive.
        /// </summary>
        public static IReadOnlyList<string> Offered { get; } =
        [
            ImportedTrigger,
            FailedTrigger,
            MovedTrigger
        ];

        /// <summary>
        /// Gated but never raised. <c>SendSystemNotificationAsync</c> has no production
        /// caller, so a webhook subscribed to <see cref="SystemTrigger"/> is as inert as one
        /// subscribed to <see cref="LegacyBookCompleted"/> was. Wiring up a caller should
        /// move this name into <see cref="Offered"/>.
        /// </summary>
        public static IReadOnlyList<string> GatedWithoutEmitter { get; } =
        [
            SystemTrigger
        ];

        /// <summary>
        /// Trigger names raised against the legacy single webhook. Every entry has a
        /// production emitter; <see cref="LegacyBookCompleted"/> is deliberately absent
        /// because nothing raises it.
        /// </summary>
        public static IReadOnlyList<string> LegacyGlobal { get; } =
        [
            LegacyBookAdded,
            LegacyBookDownloading,
            LegacyBookAvailable
        ];

        /// <summary>
        /// Retired webhook-row names whose stored meaning carries over onto a raised
        /// name. Both entries were labelled as "the book finished and landed" in the UI
        /// that wrote them, which is the event <see cref="ImportedTrigger"/> raises.
        /// </summary>
        public static IReadOnlyDictionary<string, string> RetiredAliases { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [LegacyBookAvailable] = ImportedTrigger,
                [LegacyBookCompleted] = ImportedTrigger
            };

        /// <summary>
        /// Retired webhook-row names with no counterpart that is ever raised. Both describe points
        /// in the pipeline that raise nothing against a webhook row, so a subscription to
        /// either has never delivered anything and cannot be honoured by renaming it.
        /// </summary>
        public static IReadOnlyList<string> RetiredWithoutCounterpart { get; } =
        [
            LegacyBookAdded,
            LegacyBookDownloading
        ];

        /// <summary>Whether a dispatch site gates a webhook row on this name.</summary>
        public static bool IsDispatchGated(string? trigger) =>
            trigger is not null && DispatchGated.Contains(trigger, StringComparer.Ordinal);

        /// <summary>Whether a webhook row subscribed to this name can actually receive an event.</summary>
        public static bool IsOffered(string? trigger) =>
            trigger is not null && Offered.Contains(trigger, StringComparer.Ordinal);

        /// <summary>Whether this name was written onto webhook rows by a settings UI build that predates <see cref="DispatchGated"/>.</summary>
        public static bool IsRetired(string? trigger) =>
            trigger is not null
            && (RetiredAliases.ContainsKey(trigger)
                || RetiredWithoutCounterpart.Contains(trigger, StringComparer.Ordinal));

        /// <summary>
        /// Rewrites a stored webhook trigger list onto the vocabulary the dispatch reads.
        /// Retired names with a counterpart are remapped, retired names without one are
        /// dropped, and any name this type does not recognise is passed through untouched
        /// rather than discarded.
        /// </summary>
        public static WebhookTriggerCanonicalizationResult Canonicalize(IEnumerable<string>? stored)
        {
            var triggers = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var remapped = new List<KeyValuePair<string, string>>();
            var dropped = new List<string>();

            foreach (var raw in stored ?? [])
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var trigger = raw.Trim();

                if (RetiredAliases.TryGetValue(trigger, out var replacement))
                {
                    remapped.Add(new KeyValuePair<string, string>(trigger, replacement));
                    trigger = replacement;
                }
                else if (RetiredWithoutCounterpart.Contains(trigger, StringComparer.Ordinal))
                {
                    dropped.Add(trigger);
                    continue;
                }

                if (seen.Add(trigger))
                {
                    triggers.Add(trigger);
                }
            }

            return new WebhookTriggerCanonicalizationResult
            {
                Triggers = triggers,
                Remapped = remapped,
                Dropped = dropped
            };
        }
    }

    /// <summary>
    /// The outcome of rewriting one webhook row's stored trigger list, carrying enough
    /// detail for the caller to tell the operator exactly what changed.
    /// </summary>
    public sealed class WebhookTriggerCanonicalizationResult
    {
        /// <summary>The rewritten trigger list, deduplicated and in first-seen order.</summary>
        public required IReadOnlyList<string> Triggers { get; init; }

        /// <summary>Retired names that were replaced, as stored-name to deliverable-name pairs.</summary>
        public required IReadOnlyList<KeyValuePair<string, string>> Remapped { get; init; }

        /// <summary>Retired names removed because no webhook event raises them.</summary>
        public required IReadOnlyList<string> Dropped { get; init; }

        /// <summary>Whether the stored list needed rewriting at all.</summary>
        public bool Changed => Remapped.Count > 0 || Dropped.Count > 0;
    }
}
