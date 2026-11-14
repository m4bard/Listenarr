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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.HostedServices.Search
{
    /// <summary>
    /// The post-search half of an automatic search: scores the candidates, drops blocked releases,
    /// compares the winner with the quality already held and starts the download. Sends no indexer
    /// request.
    /// </summary>
    public sealed class AutomaticSearchAcquisition : IAutomaticSearchAcquisition
    {
        private readonly ILogger<AutomaticSearchAcquisition> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IQualityProfileService _qualityProfileService;
        private readonly IDownloadService _downloadService;
        private readonly IBlocklistService _blocklistService;
        private readonly AutomaticSearchPreChecks _preChecks;
        private readonly AutomaticSearchResultClassifier _resultClassifier;
        private readonly AutomaticSearchQualityEvaluator _qualityEvaluator;
        private readonly AutomaticSearchDownloadClientSelector _downloadClientSelector;

        public AutomaticSearchAcquisition(
            ILogger<AutomaticSearchAcquisition> logger,
            IServiceScopeFactory serviceScopeFactory,
            IDownloadRepository downloadRepository,
            IAudiobookFileRepository fileRepository,
            IQualityProfileService qualityProfileService,
            IDownloadService downloadService,
            IBlocklistService blocklistService)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _qualityProfileService = qualityProfileService;
            _downloadService = downloadService;
            _blocklistService = blocklistService;
            _preChecks = new AutomaticSearchPreChecks(downloadRepository, fileRepository, logger);
            _resultClassifier = new AutomaticSearchResultClassifier(logger);
            _qualityEvaluator = new AutomaticSearchQualityEvaluator(logger);
            _downloadClientSelector = new AutomaticSearchDownloadClientSelector(serviceScopeFactory, logger);
        }

        public async Task<AcquisitionResult> AcquireAsync(
            Audiobook audiobook,
            IReadOnlyList<IndexerSearchResult> candidates,
            SearchOrigin origin,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(audiobook);
            ArgumentNullException.ThrowIfNull(candidates);

            try
            {
                // The state can change while the searches run, so the pre-checks are repeated here.
                var need = await _preChecks.EvaluateAsync(audiobook, ct);
                if (!need.Needed)
                {
                    return new AcquisitionResult(BookSearchOutcome.NotNeeded, 0);
                }

                if (candidates.Count == 0)
                {
                    _logger.LogInformation("No search results found for audiobook '{Title}'", audiobook.Title);
                    return new AcquisitionResult(BookSearchOutcome.NoAcceptableResults, 0);
                }

                var searchResults = candidates.Select(SearchResultConverters.ToSearchResult).ToList();
                var topResult = await SelectTopResultAsync(audiobook, searchResults, need.BestExistingQuality);
                if (topResult == null)
                {
                    return new AcquisitionResult(BookSearchOutcome.NoAcceptableResults, 0);
                }

                _logger.LogInformation("Acquiring '{Title}' for a {Origin} search", audiobook.Title, origin);
                return await GrabAsync(audiobook, topResult);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Error processing audiobook '{Title}' (ID: {Id})", audiobook.Title, audiobook.Id);
                return new AcquisitionResult(BookSearchOutcome.BookFailed, 0);
            }
        }

        private async Task<QualityScore?> SelectTopResultAsync(
            Audiobook audiobook,
            List<SearchResult> searchResults,
            string? bestExistingQuality)
        {
            // Score results against quality profile
            var scoredResults = await _qualityProfileService.ScoreSearchResults(searchResults, audiobook.QualityProfile!);

            // Log all scored results for debugging
            _logger.LogInformation("Scored {Count} search results for audiobook '{Title}':", scoredResults.Count, audiobook.Title);

            await BroadcastScoredResultsAsync(audiobook, scoredResults);
            foreach (var scoredResult in scoredResults.OrderByDescending(s => s.TotalScore))
            {
                var status = scoredResult.IsRejected ? "REJECTED" : (scoredResult.TotalScore > 0 ? "ACCEPTABLE" : "LOW SCORE");
                _logger.LogInformation("  [{Status}] Score: {Score} | Title: {Title} | Source: {Source} | Size: {Size}MB | Seeders: {Seeders} | Quality: {Quality}",
                    status, scoredResult.TotalScore, scoredResult.SearchResult.Title, scoredResult.SearchResult.Source,
                    scoredResult.SearchResult.Size / 1024 / 1024, scoredResult.SearchResult.Seeders, scoredResult.SearchResult.Quality);

                if (scoredResult.IsRejected && scoredResult.RejectionReasons.Any())
                {
                    _logger.LogInformation("    Rejection reasons: {Reasons}", string.Join(", ", scoredResult.RejectionReasons));
                }
            }

            // Drop releases already blocked for this book before picking a winner.
            //
            // This is the path that actually re-grabs. The blocklist filter was wired into
            // SearchAndDownloadAsync, which covers the manual search endpoint and the retry that
            // follows a failure, but this service does not go through it: it scores results here and
            // calls StartDownloadAsync directly. So a release could fail, be blocked, and be grabbed
            // again by the next automatic pass a minute later, which is what a live install saw.
            var selectableResults = await BlockedReleaseFilter.ExcludeAsync(
                _blocklistService, audiobook.Id, scoredResults, _logger);

            var topResult = selectableResults
                .Where(s => !s.IsRejected) // Only non-rejected results
                .OrderByDescending(s => s.TotalScore)
                .FirstOrDefault(); // Pick only the top scoring result

            if (topResult == null)
            {
                _logger.LogInformation("No acceptable search results found for audiobook '{Title}' after quality filtering", audiobook.Title);
                return null;
            }

            _logger.LogInformation("Found top result for audiobook '{Title}': {ResultTitle} (Score: {Score}, Quality: {Quality})",
                audiobook.Title, topResult.SearchResult.Title, topResult.TotalScore, topResult.SearchResult.Quality);

            // Check if the found result is better quality than what we already have
            if (!string.IsNullOrEmpty(bestExistingQuality))
            {
                var resultIsBetter = _qualityEvaluator.IsQualityBetter(topResult.SearchResult.Quality, bestExistingQuality, audiobook.QualityProfile);
                if (!resultIsBetter)
                {
                    _logger.LogInformation("Top result quality '{ResultQuality}' is not better than existing quality '{ExistingQuality}' for audiobook '{Title}', skipping download",
                        topResult.SearchResult.Quality, bestExistingQuality, audiobook.Title);
                    return null;
                }
                _logger.LogInformation("Top result quality '{ResultQuality}' is better than existing quality '{ExistingQuality}', proceeding with download",
                    topResult.SearchResult.Quality, bestExistingQuality);
            }
            else
            {
                _logger.LogInformation("No existing files for audiobook '{Title}', proceeding with download", audiobook.Title);
            }

            return topResult;
        }

        // An acceptable release that could not be queued is GrabNotCompleted, never Satisfied: no
        // suitable download client, or the download start threw.
        private async Task<AcquisitionResult> GrabAsync(Audiobook audiobook, QualityScore topResult)
        {
            // Add score to the search result for tracking
            topResult.SearchResult.Score = topResult.TotalScore;

            try
            {
                // Determine appropriate download client for this result
                var isTorrent = _resultClassifier.IsTorrentResult(topResult.SearchResult);
                var downloadClientId = await _downloadClientSelector.GetAppropriateDownloadClientAsync(topResult.SearchResult, isTorrent);

                if (string.IsNullOrEmpty(downloadClientId))
                {
                    _logger.LogWarning("No suitable download client found for result type: {Type}", isTorrent ? "torrent" : "NZB");
                    return new AcquisitionResult(BookSearchOutcome.GrabNotCompleted, 0);
                }

                await _downloadService.StartDownloadAsync(topResult.SearchResult, downloadClientId, audiobook.Id);

                _logger.LogInformation("Queued download for audiobook '{Title}': {ResultTitle} (Score: {Score})",
                    audiobook.Title, topResult.SearchResult.Title, topResult.TotalScore);
                return new AcquisitionResult(BookSearchOutcome.Satisfied, 1);
            }
            catch (IndexerRequestDeferredException ex)
            {
                // The gate held back the .torrent or NZB fetch, which says nothing about the book:
                // retry once the deferral ends, without counting a failure.
                _logger.LogInformation(
                    "Download of '{ResultTitle}' for audiobook '{Title}' deferred until {Until:O} ({Reason})",
                    topResult.SearchResult.Title, audiobook.Title, ex.Deferral.UntilUtc, ex.Deferral.Reason);
                return new AcquisitionResult(BookSearchOutcome.GrabNotCompleted, 0, ex.Deferral);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Failed to queue download for audiobook '{Title}': {ResultTitle}",
                    audiobook.Title, topResult.SearchResult.Title);
                return new AcquisitionResult(BookSearchOutcome.GrabNotCompleted, 0);
            }
        }

        // Broadcast scored result summaries (score + rejection reasons) to aid debugging
        private async Task BroadcastScoredResultsAsync(Audiobook audiobook, List<QualityScore> scoredResults)
        {
            try
            {
                var scoredSummaries = scoredResults.Select(s => new
                {
                    title = s.SearchResult.Title,
                    asin = s.SearchResult.Asin,
                    totalScore = s.TotalScore,
                    isRejected = s.IsRejected,
                    rejectionReasons = s.RejectionReasons,
                    source = s.SearchResult.Source,
                    sizeMB = s.SearchResult.Size > 0 ? (s.SearchResult.Size / 1024 / 1024) : -1,
                    seeders = s.SearchResult.Seeders,
                    format = s.SearchResult.Format
                }).ToList();

                using var scope = _serviceScopeFactory.CreateScope();
                var hub = scope.ServiceProvider.GetRequiredService<IHubContext<DownloadHub>>();
                await hub.Clients.All.SendCoreAsync("SearchProgress", new object[] { new { message = $"Scored results for '{audiobook.Title}'", details = new { scoredCount = scoredResults.Count, scoredSamples = scoredSummaries }, type = "automatic", audiobookId = audiobook.Id } });
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogDebug(ex, "Failed to broadcast scored search results for audiobook {Id}", audiobook.Id);
            }
        }
    }
}
