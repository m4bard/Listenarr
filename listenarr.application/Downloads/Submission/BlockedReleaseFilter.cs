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
    /// Drops candidates already blocked for this book, before one of them is selected.
    ///
    /// Filtering here rather than at display time is what stops the loop: a search that
    /// only turns up releases already known to fail for this book ends at the existing
    /// "no acceptable search results" answer, which is a terminal state the pipeline
    /// already has, rather than a new error.
    /// </summary>
    public static class BlockedReleaseFilter
    {
        public static async Task<List<QualityScore>> ExcludeAsync(
            IBlocklistService blocklistService,
            int audiobookId,
            List<QualityScore> scoredResults,
            ILogger logger)
        {
            if (scoredResults.Count == 0)
            {
                return scoredResults;
            }

            var blocked = await blocklistService.GetForAudiobookAsync(audiobookId);
            if (blocked.Count == 0)
            {
                return scoredResults;
            }

            var kept = scoredResults
                .Where(scored => !IsBlocked(scored.SearchResult, blocked))
                .ToList();

            var dropped = scoredResults.Count - kept.Count;
            if (dropped > 0)
            {
                logger.LogInformation(
                    "Skipped {Dropped} search result(s) already blocked for audiobook {AudiobookId}",
                    dropped,
                    audiobookId);
            }

            return kept;
        }

        private static bool IsBlocked(SearchResult result, IReadOnlyList<BlockedRelease> blocked)
        {
            // Deliberately no field-picking here. ReleaseIdentity owns both which parts of a
            // search result make up a key and what counts as the same release, and the grab side
            // asks it the same question about the same object, so the two cannot answer
            // differently. Every defect this feature has had so far came from two sides picking
            // fields for themselves.
            //
            // A row at a time rather than a set lookup, because a match is not string equality:
            // an info-hash key and a title-plus-size key are both live on every row, and the size
            // carries tolerance. Bounded by the per-book scoping, which is what makes a linear
            // pass the right shape here.
            for (var index = 0; index < blocked.Count; index++)
            {
                if (ReleaseIdentity.Matches(blocked[index], result))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
