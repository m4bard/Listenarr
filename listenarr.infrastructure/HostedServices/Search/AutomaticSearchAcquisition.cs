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
    /// The post-search half of an automatic search: drops blocked releases, assesses the candidates,
    /// compares the winner with the quality already held and starts the download while the
    /// assessment is still current. Sends no indexer request.
    /// </summary>
    public sealed class AutomaticSearchAcquisition : IAutomaticSearchAcquisition
    {
        private readonly ILogger<AutomaticSearchAcquisition> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IAutomaticSearchScoringService _automaticScoring;
        private readonly IDownloadService _downloadService;
        private readonly IBlocklistService _blocklistService;
        private readonly AutomaticSearchPreChecks _preChecks;
        private readonly AutomaticSearchResultClassifier _resultClassifier;
        private readonly AutomaticSearchQualityEvaluator _qualityEvaluator;
        private readonly DownloadClientSelector _downloadClientSelector;

        public AutomaticSearchAcquisition(
            ILogger<AutomaticSearchAcquisition> logger,
            IServiceScopeFactory serviceScopeFactory,
            IDownloadRepository downloadRepository,
            IAudiobookFileRepository fileRepository,
            IAutomaticSearchScoringService automaticScoring,
            IDownloadService downloadService,
            IBlocklistService blocklistService,
            DownloadClientSelector downloadClientSelector)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _automaticScoring = automaticScoring;
            _downloadService = downloadService;
            _blocklistService = blocklistService;
            _preChecks = new AutomaticSearchPreChecks(downloadRepository, fileRepository, logger);
            _resultClassifier = new AutomaticSearchResultClassifier(logger);
            _qualityEvaluator = new AutomaticSearchQualityEvaluator(logger);
            _downloadClientSelector = downloadClientSelector;
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
                var selection = await SelectTopResultAsync(audiobook, searchResults, need.BestExistingQuality, ct);
                if (selection == null)
                {
                    return new AcquisitionResult(BookSearchOutcome.NoAcceptableResults, 0);
                }

                _logger.LogInformation("Acquiring '{Title}' for a {Origin} search", audiobook.Title, origin);
                return await GrabAsync(audiobook, selection.Value.Assessment, selection.Value.TopResult, ct);
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

        private async Task<(AutomaticSearchAssessment Assessment, AutomaticCandidateScore TopResult)?> SelectTopResultAsync(
            Audiobook audiobook,
            List<SearchResult> searchResults,
            string? bestExistingQuality,
            CancellationToken ct)
        {
            // Drop releases already blocked for this book before they are assessed.
            //
            // This is the path that actually re-grabs: it selects here and calls StartDownloadAsync
            // directly rather than going through SearchAndDownloadAsync, so without this a release
            // could fail, be blocked, and be grabbed again by the next automatic pass.
            searchResults = await BlockedReleaseFilter.ExcludeAsync(
                _blocklistService, audiobook.Id, searchResults, _logger);

            var assessment = await _automaticScoring.ScoreAsync(audiobook, searchResults, audiobook.QualityProfile!, ct);
            var scoredResults = assessment.Candidates;

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

            var topResult = _automaticScoring.SelectBest(assessment);

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

            return (assessment, topResult);
        }

        // An acceptable release that could not be queued is GrabNotCompleted, never Satisfied: no
        // suitable download client, a book or candidate that changed after it was assessed, or the
        // download start threw.
        private async Task<AcquisitionResult> GrabAsync(
            Audiobook audiobook,
            AutomaticSearchAssessment assessment,
            AutomaticCandidateScore topResult,
            CancellationToken ct)
        {
            // Add score to the search result for tracking
            topResult.SearchResult.Score = topResult.TotalScore;

            try
            {
                // Determine appropriate download client for this result
                var protocol = _resultClassifier.ResolveProtocol(topResult.SearchResult);
                var downloadClientId = await _downloadClientSelector.GetAppropriateDownloadClientAsync(
                    protocol, topResult.SearchResult.IndexerId);

                if (string.IsNullOrEmpty(downloadClientId))
                {
                    _logger.LogWarning("No suitable download client found for protocol: {Protocol}", protocol);
                    return new AcquisitionResult(BookSearchOutcome.GrabNotCompleted, 0);
                }

                if (!await _automaticScoring.IsCurrentAsync(assessment, audiobook.Id, ct))
                {
                    _logger.LogInformation("Search context for audiobook '{Title}' changed after its results were assessed, not starting the download",
                        audiobook.Title);
                    return new AcquisitionResult(BookSearchOutcome.GrabNotCompleted, 0);
                }

                await _downloadService.StartDownloadAsync(topResult.SearchResult, downloadClientId, audiobook.Id);

                _logger.LogInformation("Queued download for audiobook '{Title}': {ResultTitle} (Score: {Score})",
                    audiobook.Title, topResult.SearchResult.Title, topResult.TotalScore);
                return new AcquisitionResult(BookSearchOutcome.Satisfied, 1);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Failed to queue download for audiobook '{Title}': {ResultTitle}",
                    audiobook.Title, topResult.SearchResult.Title);
                return new AcquisitionResult(BookSearchOutcome.GrabNotCompleted, 0);
            }
        }

        // Broadcast scored result summaries (score + rejection reasons) to aid debugging
        private async Task BroadcastScoredResultsAsync(Audiobook audiobook, IReadOnlyList<AutomaticCandidateScore> scoredResults)
        {
            try
            {
                var scoredSummaries = scoredResults.Select(s => new
                {
                    title = s.SearchResult.Title,
                    asin = s.SearchResult.Asin,
                    totalScore = s.TotalScore,
                    identityStatus = s.Identity.Status.ToString(),
                    identityReason = s.Identity.Reason,
                    compatibilityReasons = s.Compatibility.Reasons,
                    languageEvidence = s.Compatibility.Languages,
                    editionEvidence = s.Compatibility.Editions,
                    policyVersion = AutomaticSearchAssessment.PolicyVersion,
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
