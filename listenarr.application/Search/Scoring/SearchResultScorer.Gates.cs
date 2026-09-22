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
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Search.Scoring
{
    // The gates that can reject a release outright, kept here so SearchResultScorer.cs stays
    // clear of the 500-line architecture cap, the same way SearchResultScorer.Helpers.cs and
    // QualityMatcher.Internals.cs already are.
    //
    // The seam is not arbitrary. Everything in this file either rejects the release and stops,
    // or contributes nothing to the score; everything left in Score() adjusts the score and
    // rejects nothing. The two concerns also share exactly two values, isNzb and the release
    // age, which is what GateOutcome carries back.
    public partial class SearchResultScorer
    {
        /// <summary>
        /// What the gates decided, and the two values the scoring below them still needs.
        /// </summary>
        /// <remarks>
        /// A rejected release has already had its reason and its sentinel score written onto the
        /// <see cref="QualityScore"/> that was passed in, so the caller only has to stop.
        /// </remarks>
        private readonly record struct GateOutcome(bool Rejected, bool IsNzb, double AgeDays)
        {
            public static GateOutcome Reject() => new(true, false, 0);

            public static GateOutcome Pass(bool isNzb, double ageDays) => new(false, isNzb, ageDays);
        }

        /// <summary>
        /// Resolves the indexer, settles whether this is a Usenet result, and applies every gate
        /// that can refuse the release: the indexer's size ceiling, the profile's size bounds,
        /// the minimum seeders, the indexer retention and the profile's maximum age.
        /// </summary>
        private async Task<GateOutcome> ApplyRejectionGates(
            SearchResult searchResult,
            QualityProfile profile,
            QualityScore score)
        {
            // Detect NZB/Usenet more broadly
            var isNzb = IsNzbResult(searchResult);

            // The indexer is read before the size and age gates because all three depend on it.
            // It also corrects isNzb from the indexer's own type, and that correction used to
            // happen after the size gate had already run, so a Usenet result recognised only by
            // its indexer type was size-checked despite the exemption just below.
            int indexerRetention = 0;
            int indexerMaximumSizeMb = 0;
            int indexerMinimumAgeMinutes = 0;
            if (searchResult.IndexerId.HasValue
                && (_resolvedIndexers != null || _indexerRepository != null))
            {
                try
                {
                    var idx = _resolvedIndexers != null
                        ? (_resolvedIndexers.TryGetValue(searchResult.IndexerId.Value, out var preresolved)
                            ? preresolved
                            : null)
                        : await _indexerRepository!.GetByIdAsync(searchResult.IndexerId.Value);
                    if (idx != null)
                    {
                        indexerRetention = idx.Retention;
                        indexerMaximumSizeMb = idx.MaximumSize;
                        indexerMinimumAgeMinutes = idx.MinimumAge;
                        // Captured for tie-break purposes only (see QualityScoreComparer) - never
                        // added into TotalScore, so indexer choice cannot override release quality.
                        score.IndexerPriority = idx.Priority;
                        if (!isNzb && !string.IsNullOrWhiteSpace(idx.Type) && string.Equals(idx.Type, "Usenet", StringComparison.OrdinalIgnoreCase))
                        {
                            isNzb = true;
                            _logger.LogDebug("Indexer {IndexerId} type '{Type}' detected as Usenet", searchResult.IndexerId.Value, idx.Type);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    _logger.LogDebug(ex, "Failed to fetch indexer settings for IndexerId {Id}", searchResult.IndexerId.Value);
                }
            }

            if (indexerMaximumSizeMb > 0 && searchResult.Size > (long)indexerMaximumSizeMb * 1024 * 1024)
            {
                score.RejectionReasons.Add($"File too large for indexer (> {indexerMaximumSizeMb} MB)");
                score.TotalScore = -1;
                return GateOutcome.Reject();
            }

            // Size checks. MinimumSize and MaximumSize are operator settings on the profile,
            // not properties of a protocol, so they apply to every result that reports a size.
            // They used to sit behind !isNzb, which meant a size ceiling and a size floor did
            // nothing at all over Usenet.
            if (searchResult.Size > 0)
            {
                // (long) before the multiply, not after. MinimumSize and MaximumSize are int MB
                // and the settings form puts no ceiling on either, so 2048 or more overflows int
                // and wraps negative: the maximum gate then rejects every release as too large,
                // and the minimum gate stops rejecting anything at all.
                if (profile.MinimumSize > 0 && searchResult.Size < (long)profile.MinimumSize * 1024 * 1024)
                {
                    score.RejectionReasons.Add($"File too small (< {profile.MinimumSize} MB)");
                    score.TotalScore = -1;
                    return GateOutcome.Reject();
                }
                if (profile.MaximumSize > 0 && searchResult.Size > (long)profile.MaximumSize * 1024 * 1024)
                {
                    score.RejectionReasons.Add($"File too large (> {profile.MaximumSize} MB)");
                    score.TotalScore = -1;
                    return GateOutcome.Reject();
                }
            }

            // Seeders requirement (treat null as 0).
            //
            // Case-insensitive on purpose. Every indexer parser writes this field capitalised,
            // "Torrent", and an ordinal `==` against a lowercase literal is always false, so the
            // configured MinimumSeeders never applied to a real torrent. Every other protocol
            // comparison in this codebase already compares case-insensitively, including the nzb
            // and usenet check further down this same method.
            if (string.Equals(searchResult.DownloadType, "torrent", StringComparison.OrdinalIgnoreCase)
                && (searchResult.Seeders ?? 0) < profile.MinimumSeeders)
            {
                var seedersValue = (searchResult.Seeders.HasValue) ? searchResult.Seeders.Value.ToString() : "(none)";
                score.RejectionReasons.Add($"Not enough seeders ({seedersValue} < {profile.MinimumSeeders})");
                score.TotalScore = -1;
                return GateOutcome.Reject();
            }

            double ageDays = 0;

            // Parsed to UTC explicitly, in TryParsePublishedDateUtc, so the subtraction from
            // DateTime.UtcNow below is between two UTC instants whatever the host's offset is.
            if (TryParsePublishedDateUtc(searchResult.PublishedDate, out var publishDate))
            {
                ageDays = (DateTime.UtcNow - publishDate).TotalDays;

                // Usenet only, and the reason is propagation rather than preference: a post that
                // has not finished propagating downloads as an incomplete or failed grab. Sonarr
                // and Radarr expose the same per-indexer minimum for the same reason.
                if (isNzb && indexerMinimumAgeMinutes > 0)
                {
                    var ageMinutes = (DateTime.UtcNow - publishDate).TotalMinutes;
                    if (ageMinutes < indexerMinimumAgeMinutes)
                    {
                        score.RejectionReasons.Add($"Too new ({(int)ageMinutes} minutes < indexer minimum age {indexerMinimumAgeMinutes} minutes)");
                        score.TotalScore = -1;
                        return GateOutcome.Reject();
                    }
                }

                if (isNzb)
                {
                    if (indexerRetention > 0 && ageDays > indexerRetention)
                    {
                        score.RejectionReasons.Add($"Too old ({(int)ageDays} days > indexer retention {indexerRetention} days)");
                        score.TotalScore = -1;
                        return GateOutcome.Reject();
                    }
                    if (profile.MaximumAge > 0 && ageDays > profile.MaximumAge)
                    {
                        score.RejectionReasons.Add($"Too old ({(int)ageDays} days > profile maximum age {profile.MaximumAge} days)");
                        score.TotalScore = -1;
                        return GateOutcome.Reject();
                    }
                }
                else
                {
                    if (indexerRetention > 0)
                    {
                        if (ageDays > indexerRetention)
                        {
                            score.RejectionReasons.Add($"Too old ({(int)ageDays} days > indexer retention {indexerRetention} days)");
                            score.TotalScore = -1;
                            return GateOutcome.Reject();
                        }
                    }
                    else if (profile.MaximumAge > 0 && ageDays > profile.MaximumAge)
                    {
                        score.RejectionReasons.Add($"Too old ({(int)ageDays} days > profile maximum age {profile.MaximumAge} days)");
                        score.TotalScore = -1;
                        return GateOutcome.Reject();
                    }
                }
            }

            return GateOutcome.Pass(isNzb, ageDays);
        }
    }
}
