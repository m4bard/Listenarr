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

namespace Listenarr.Application.Search.Scoring
{
    /// <summary>
    /// What the indexer contributes to scoring a result: its retention, its own size ceiling, its
    /// minimum age, and whether it tells us the result is Usenet when the result itself did not.
    ///
    /// LOCAL ONLY. This split exists because #863 and #921 each grow SearchResultScorer.cs and
    /// together push it past the 500 line cap ActiveProductionSourceFiles_RemainFocused enforces.
    /// Each passes that test alone; only the combination fails. It is deliberately not part of
    /// either pull request, so neither is distorted by how our local stack happens to combine them.
    /// </summary>
    public partial class SearchResultScorer
    {
        private readonly record struct IndexerContext(
            bool IsNzb,
            int RetentionDays,
            int MaximumSizeMb,
            int MinimumAgeMinutes);

        /// <summary>
        /// Read the indexer once, before the size and age gates, because all three depend on it.
        /// This is also where isNzb is corrected from the indexer's own type.
        /// </summary>
        private async Task<IndexerContext> ResolveIndexerContextAsync(SearchResult searchResult, bool isNzb)
        {
            var retention = 0;
            var maximumSizeMb = 0;
            var minimumAgeMinutes = 0;
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
                        retention = idx.Retention;
                        maximumSizeMb = idx.MaximumSize;
                        minimumAgeMinutes = idx.MinimumAge;
                        if (!isNzb && !string.IsNullOrWhiteSpace(idx.Type) && string.Equals(idx.Type, "Usenet", StringComparison.OrdinalIgnoreCase))
                        {
                            isNzb = true;
                            _logger.LogDebug("Indexer {IndexerId} type '{Type}' detected as Usenet; applying NZB/Usenet exemptions", searchResult.IndexerId.Value, idx.Type);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    _logger.LogDebug(ex, "Failed to fetch indexer settings for IndexerId {Id}", searchResult.IndexerId.Value);
                }
            }

            return new IndexerContext(isNzb, retention, maximumSizeMb, minimumAgeMinutes);
        }

        private static bool IsNzbResult(SearchResult r)
        {
            bool hasNzbUrl = !string.IsNullOrEmpty(r.NzbUrl);
            bool isNzbType = string.Equals(r.DownloadType, "nzb", StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.DownloadType, "usenet", StringComparison.OrdinalIgnoreCase);
            bool indexerIndicatesNzb = !string.IsNullOrEmpty(r.IndexerImplementation)
                && (r.IndexerImplementation.IndexOf("nzb", StringComparison.OrdinalIgnoreCase) >= 0
                    || r.IndexerImplementation.IndexOf("usenet", StringComparison.OrdinalIgnoreCase) >= 0);
            bool sourceIndicatesNzb = !string.IsNullOrEmpty(r.Source)
                && r.Source.IndexOf("usenet", StringComparison.OrdinalIgnoreCase) >= 0;
            bool urlIndicatesNzb = !string.IsNullOrEmpty(r.ResultUrl)
                && (r.ResultUrl.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase)
                    || r.ResultUrl.IndexOf("/nzb", StringComparison.OrdinalIgnoreCase) >= 0);
            bool torrentIndicatesNzb = !string.IsNullOrEmpty(r.TorrentUrl)
                && r.TorrentUrl.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase);
            return hasNzbUrl || isNzbType || indexerIndicatesNzb || sourceIndicatesNzb || urlIndicatesNzb || torrentIndicatesNzb;
        }
    }
}
