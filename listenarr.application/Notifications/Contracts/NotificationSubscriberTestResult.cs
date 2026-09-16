/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */

namespace Listenarr.Application.Notifications.Contracts
{
    /// <summary>
    /// The outcome of exercising one configured subscriber, as returned by the Test button.
    /// </summary>
    public sealed record NotificationSubscriberTestResult
    {
        public required bool IsValid { get; init; }

        /// <summary>Human-readable reasons the test failed. Empty when <see cref="IsValid"/> is true.</summary>
        public IReadOnlyList<string> Failures { get; init; } = Array.Empty<string>();

        public static NotificationSubscriberTestResult Success() =>
            new() { IsValid = true };

        public static NotificationSubscriberTestResult Failure(params string[] failures) =>
            new() { IsValid = false, Failures = failures };
    }
}
