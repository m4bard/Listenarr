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

using Listenarr.Application.Search.Scheduling;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Downloads.Submission
{
    public partial class DownloadService
    {
        public async Task<SearchAndDownloadResult> SearchAndDownloadAsync(int audiobookId, SearchOrigin origin = SearchOrigin.Interactive)
        {
            // Get the audiobook
            var audiobook = await audiobookRepository.GetByIdAsync(audiobookId);
            if (audiobook == null)
            {
                return new SearchAndDownloadResult
                {
                    Success = false,
                    Message = "Audiobook not found"
                };
            }

            if (audiobook.QualityProfile == null)
            {
                logger.LogWarning("Audiobook '{Title}' has no quality profile assigned", audiobook.Title);
                return new SearchAndDownloadResult { Success = false, Message = "Audiobook has no quality profile assigned" };
            }

            // Check if an active download already exists before querying indexers (Issue #936)
            try
            {
                if (await DownloadDuplicateGuard.HasActiveDownloadAsync(audiobookId, configurationService, downloadRepository))
                {
                    logger.LogInformation("Skipping search for audiobook {AudiobookId} — active download exists. Title: '{Title}'", audiobookId, audiobook.Title);
                    return new SearchAndDownloadResult { Success = false, Message = "An active download already exists for this audiobook" };
                }
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
            {
                logger.LogWarning(ex, "Failed to check for duplicate downloads for audiobook {AudiobookId}", audiobookId);
            }

            // Build search query from audiobook metadata
            var searchQuery = DownloadSearchQueryBuilder.Build(audiobook);
            logger.LogInformation("Searching for audiobook '{Title}' with query: {Query}", LogRedaction.SanitizeText(audiobook.Title), LogRedaction.SanitizeText(searchQuery));

            // A manual search ignores the book's due time but not the request gate: it asks every
            // indexer enabled for automatic search (no Amazon/Audible scraping) and sees what each
            // answered. The origin, not the automatic flag, says who caused the search.
            var report = await searchService.SearchIndexersDetailedAsync(
                searchQuery,
                new IndexerSearchContext(origin, CycleAllowance: null),
                indexerIds: null,
                isAutomaticSearch: true,
                request: null,
                CancellationToken.None);
            var searchResults = report.Outcomes
                .SelectMany(outcome => outcome.Results)
                .OrderByDescending(result => result.Seeders ?? 0)
                .ThenByDescending(result => result.PublishedDate)
                .Select(SearchResultConverters.ToSearchResult)
                .ToList();
            var completedIndexerIds = report.Outcomes
                .Where(outcome => outcome.Status == IndexerSearchStatus.Completed)
                .Select(outcome => outcome.IndexerId)
                .ToList();

            if (searchResults.Count == 0)
            {
                return NoSearchResults(report);
            }

            // Score results against quality profile
            var scoredResults = await qualityProfileService.ScoreSearchResults(searchResults, audiobook.QualityProfile);

            // Log all scored results for debugging
            logger.LogInformation("Scored {Count} search results for audiobook '{Title}':", scoredResults.Count, LogRedaction.SanitizeText(audiobook.Title));
            foreach (var scoredResult in scoredResults.OrderByDescending(s => s.TotalScore))
            {
                var status = scoredResult.IsRejected ? "REJECTED" : (scoredResult.TotalScore > 0 ? "ACCEPTABLE" : "LOW SCORE");
                logger.LogInformation("  [{Status}] Score: {Score} | Title: {Title} | Source: {Source} | Size: {Size}MB | Seeders: {Seeders} | Quality: {Quality}",
                    status, scoredResult.TotalScore, LogRedaction.SanitizeText(scoredResult.SearchResult.Title), LogRedaction.SanitizeText(scoredResult.SearchResult.Source),
                    scoredResult.SearchResult.Size / 1024 / 1024, scoredResult.SearchResult.Seeders, scoredResult.SearchResult.Quality);
                if (scoredResult.IsRejected && scoredResult.RejectionReasons.Any())
                {
                    logger.LogInformation("    Rejection reasons: {Reasons}", string.Join(", ", scoredResult.RejectionReasons));
                }
            }

            // Only consider non-rejected, score > 0 results that are not already blocked
            var topResult = (await BlockedReleaseFilter.ExcludeAsync(blocklistService, audiobookId, scoredResults, logger))
                .Where(s => !s.IsRejected && s.TotalScore > 0)
                .OrderByDescending(s => s.TotalScore)
                .FirstOrDefault();

            if (topResult == null)
            {
                logger.LogWarning("No acceptable search results found for audiobook '{Title}' after quality filtering", audiobook.Title);
                return new SearchAndDownloadResult { Success = false, Message = "No acceptable search results found" };
            }

            // Assign score to SearchResult
            topResult.SearchResult.Score = topResult.TotalScore;

            var candidate = TrustedDownloadCandidateFactory.Create(topResult.SearchResult);
            var isTorrent = candidate.SourceDescriptor.Protocol == DownloadProtocol.Torrent;
            var downloadClientId = await downloadClientSelector.GetAppropriateDownloadClientAsync(isTorrent);

            if (downloadClientId == null)
            {
                logger.LogWarning("No suitable download client found for type: {Type}", isTorrent ? "Torrent" : "NZB");
                return new SearchAndDownloadResult { Success = false, Message = $"No suitable download client found for {(isTorrent ? "torrent" : "NZB")} results" };
            }

            // Send to download client with audiobookId for proper metadata linking
            var downloadId2 = await SendToDownloadClientAsync(candidate, downloadClientId, audiobookId);

            if (string.IsNullOrEmpty(downloadId2))
            {
                logger.LogInformation("Download was skipped (duplicate or client rejected) for audiobook {AudiobookId}", audiobookId);
                return new SearchAndDownloadResult { Success = false, Message = "Download skipped — an active download already exists", DownloadClientUsed = downloadClientId, SearchResult = topResult.SearchResult };
            }

            // Log to history
            await LogDownloadHistory(audiobook, "Search", topResult.SearchResult);
            await RecordSatisfiedPairsAsync(audiobookId, completedIndexerIds);

            return new SearchAndDownloadResult
            {
                Success = true,
                Message = $"Successfully sent to download client",
                DownloadId = downloadId2,
                IndexerUsed = "Search",
                DownloadClientUsed = downloadClientId,
                SearchResult = topResult.SearchResult
            };
        }

        // Nothing came back. A request the gate held back is the reason when there is one; otherwise
        // the indexers answered with nothing or failed.
        private static SearchAndDownloadResult NoSearchResults(IndexerSearchReport report)
        {
            var deferrals = report.Outcomes
                .Where(outcome => outcome.Status == IndexerSearchStatus.Deferred && outcome.Deferral != null)
                .Select(outcome => outcome.Deferral!.Value)
                .ToList();
            if (deferrals.Count == 0)
            {
                return new SearchAndDownloadResult { Success = false, Message = "No search results found" };
            }

            var earliest = deferrals.MinBy(deferral => deferral.UntilUtc);
            return new SearchAndDownloadResult
            {
                Success = false,
                Message = $"Search deferred until {earliest.UntilUtc:u} ({earliest.Reason}); no search results yet",
                Deferral = earliest
            };
        }

        // A manual search records only a grab, on every indexer that answered, through the schedule store.
        // Finding nothing is not recorded: the manual query is narrower than the sweep's (no series, no
        // fallback), so it must not push back the sweep's retries. A failed or deferred indexer is not
        // recorded either: the failure ladder and the backoff belong to the sweep.
        private async Task RecordSatisfiedPairsAsync(int audiobookId, IReadOnlyList<int> indexerIds)
        {
            if (indexerIds.Count == 0)
            {
                return;
            }

            try
            {
                var settings = await configurationService.GetApplicationSettingsAsync();
                var cooldown = AutomaticSearchBounds.Cooldown(settings.AutomaticSearchCooldownHours);
                var nowUtc = clock.GetUtcNow().UtcDateTime;
                foreach (var indexerId in indexerIds)
                {
                    await scheduleStore.PersistPairAsync(new PairScheduleUpdate(audiobookId, indexerId, PairScheduleResult.Satisfied), nowUtc, cooldown);
                }
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
            {
                logger.LogWarning(ex, "Failed to record the manual search's indexer outcomes for audiobook {AudiobookId}", audiobookId);
            }
        }
    }
}
