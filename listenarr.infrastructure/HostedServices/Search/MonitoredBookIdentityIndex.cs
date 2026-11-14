/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Application.Search.Discovery;

namespace Listenarr.Infrastructure.HostedServices.Search
{
    internal enum MonitoredBookMatchOutcome
    {
        NoMatch,
        Matched,
        Ambiguous
    }

    internal readonly record struct MonitoredBookMatch(MonitoredBookMatchOutcome Outcome, int? AudiobookId);

    /// <summary>
    /// Monitored books keyed by normalized title, so a release is compared with the few books
    /// that share its title instead of the whole library. The comparison itself is
    /// <see cref="ReleaseTitleIdentity"/>'s.
    /// </summary>
    internal sealed class MonitoredBookIdentityIndex
    {
        private readonly Dictionary<string, List<MonitoredBookIdentity>> _booksByTitleKey;

        public MonitoredBookIdentityIndex(IEnumerable<MonitoredBookIdentity> books)
        {
            _booksByTitleKey = books
                .Where(book => !string.IsNullOrWhiteSpace(book.Title))
                .GroupBy(book => ReleaseTitleIdentity.NormalizeIdentity(book.Title), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        }

        public MonitoredBookMatch Match(SearchResult release)
        {
            if (!ReleaseTitleIdentity.TryParse(release.Title, out _, out var titleKey)
                || !_booksByTitleKey.TryGetValue(titleKey, out var candidates))
            {
                return new MonitoredBookMatch(MonitoredBookMatchOutcome.NoMatch, null);
            }

            var matches = candidates
                .Where(book => ReleaseTitleIdentity.IsBook(release, book.Title, book.Authors?.ToList(), book.Asin))
                .ToList();

            return matches.Count switch
            {
                0 => new MonitoredBookMatch(MonitoredBookMatchOutcome.NoMatch, null),
                1 => new MonitoredBookMatch(MonitoredBookMatchOutcome.Matched, matches[0].Id),
                _ => new MonitoredBookMatch(MonitoredBookMatchOutcome.Ambiguous, null)
            };
        }
    }
}
