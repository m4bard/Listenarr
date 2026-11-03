/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Downloads.Submission
{
    /// <summary>
    /// Drops candidates identical to a grab of this book that the download client declined as a
    /// duplicate and that is still held as one (see <see cref="ClientDuplicateHold" />).
    ///
    /// This is what stops the re-grab loop without a blocklist entry. Grabbing the same release
    /// again only gets the same duplicate answer from the client, and the blocklist is the wrong
    /// tool because the release is not broken. The exclusion lasts only as long as the held
    /// download exists, so clearing it makes the release eligible again.
    /// </summary>
    public static class ClientDuplicateReleaseFilter
    {
        public static List<QualityScore> Exclude(
            IEnumerable<Download> downloadsForBook,
            List<QualityScore> scoredResults,
            ILogger logger) =>
            Exclude(downloadsForBook, scoredResults, scored => scored.SearchResult, logger);

        /// <summary>
        /// The same filter for raw search results, for callers that assess and select in one
        /// step and so have to drop held releases before the assessment rather than after it.
        /// </summary>
        public static List<SearchResult> Exclude(
            IEnumerable<Download> downloadsForBook,
            List<SearchResult> searchResults,
            ILogger logger) =>
            Exclude(downloadsForBook, searchResults, result => result, logger);

        private static List<T> Exclude<T>(
            IEnumerable<Download> downloadsForBook,
            List<T> candidates,
            Func<T, SearchResult> searchResultOf,
            ILogger logger)
        {
            if (candidates.Count == 0)
            {
                return candidates;
            }

            var held = ClientDuplicateHold.HeldReleaseIdentifiers(downloadsForBook);
            if (held.Count == 0)
            {
                return candidates;
            }

            var heldSet = new HashSet<string>(held, StringComparer.OrdinalIgnoreCase);
            var kept = candidates
                .Where(candidate => !BlockedReleaseFilter.IsBlocked(searchResultOf(candidate), heldSet))
                .ToList();

            var dropped = candidates.Count - kept.Count;
            if (dropped > 0)
            {
                logger.LogInformation(
                    "Skipped {Dropped} search result(s) the download client already skipped as a duplicate for this book",
                    dropped);
            }

            return kept;
        }
    }
}
