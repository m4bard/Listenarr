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

using Listenarr.Application.Search.Scoring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.HostedServices.Search
{
    public class AutomaticSearchService(
        ILogger<AutomaticSearchService> logger,
        IAutomaticSearchProcessor processor,
        IWorkerCycleRunner cycleRunner,
        IServiceScopeFactory scopeFactory) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("AutomaticSearchService started. Sweep interval and per-book cooldown are read from settings each cycle");

            await cycleRunner.RunPeriodicAsync(
                nameof(AutomaticSearchService),
                initialDelay: TimeSpan.FromMinutes(5),
                intervalProvider: GetSweepInterval,
                runCycle: processor.RunCycleAsync,
                stoppingToken,
                // Searching on demand is the whole point of a search button, and the
                // family exposes the equivalent commands. It does grab releases, so it is
                // the one entry here with a real side effect; the exclusion gate stops a
                // second press from overlapping the first.
                manualTrigger: ScheduledTaskManualTrigger.Allowed);

            logger.LogInformation("AutomaticSearchService stopped");
        }

        // Read each cycle so a settings change takes effect without a restart. The sweep
        // cadence only decides how often the worker looks; eligibility is the cooldown's job.
        private TimeSpan GetSweepInterval()
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var configurationService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
                var settings = configurationService.GetApplicationSettingsAsync().GetAwaiter().GetResult();
                return AutomaticSearchTiming.FromSettings(settings).SweepInterval;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogWarning(ex, "Could not read automatic-search timing settings; sweeping every {Hours} hour(s)", AutomaticSearchTiming.Default.SweepInterval.TotalHours);
                return AutomaticSearchTiming.Default.SweepInterval;
            }
        }
    }

    public partial class AutomaticSearchProcessor : IAutomaticSearchProcessor
    {
        private readonly ILogger<AutomaticSearchProcessor> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly AutomaticSearchResultClassifier _resultClassifier;
        private readonly AutomaticSearchQualityEvaluator _qualityEvaluator;
        private readonly TimeProvider _timeProvider;

        public AutomaticSearchProcessor(
            ILogger<AutomaticSearchProcessor> logger,
            IServiceScopeFactory serviceScopeFactory,
            TimeProvider? timeProvider = null)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _resultClassifier = new AutomaticSearchResultClassifier(_logger);
            _qualityEvaluator = new AutomaticSearchQualityEvaluator(_logger);
        }

        public Task RunCycleAsync(CancellationToken cancellationToken) => PerformAutomaticSearchesAsync(cancellationToken);

        private async Task PerformAutomaticSearchesAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting automatic search cycle for monitored audiobooks");

            using var scope = _serviceScopeFactory.CreateScope();
            var audiobookRepository = scope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
            var downloadRepository = scope.ServiceProvider.GetRequiredService<IDownloadRepository>();
            var fileRepository = scope.ServiceProvider.GetRequiredService<IAudiobookFileRepository>();
            var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();
            var downloadService = scope.ServiceProvider.GetRequiredService<IDownloadService>();
            var configurationService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var automaticScoring = scope.ServiceProvider.GetRequiredService<IAutomaticSearchScoringService>();
            var downloadClientSelector = scope.ServiceProvider.GetRequiredService<DownloadClientSelector>();

            var timing = AutomaticSearchTiming.FromSettings(await configurationService.GetApplicationSettingsAsync());

            // A book is eligible once per cooldown, however often the sweep itself runs.
            var cutoffTime = timing.EligibilityCutoff(DateTime.UtcNow);
            var monitoredAudiobooks = await audiobookRepository.GetMonitoredAudiobooksForSearchAsync(cutoffTime, stoppingToken);

            _logger.LogInformation("Found {Count} monitored audiobooks eligible for automatic search", monitoredAudiobooks.Count);

            if (!monitoredAudiobooks.Any())
            {
                _logger.LogInformation("No audiobooks require automatic search at this time");
                return;
            }

            var processedCount = 0;
            var downloadsQueued = 0;

            // Pause between books that query the indexers so a large wanted list does not hammer them.
            // A book skipped before its search (cutoff met, active download) sends no indexer request,
            // so it does not wait; otherwise a run of skipped books holds up the next real search.
            var hasSearchedThisCycle = false;
            async Task<bool> WaitBeforeIndexerSearchAsync()
            {
                var waited = hasSearchedThisCycle && timing.BookDelay > TimeSpan.Zero;
                if (waited)
                {
                    await Task.Delay(timing.BookDelay, _timeProvider, stoppingToken);
                }
                hasSearchedThisCycle = true;
                return waited;
            }

            foreach (var audiobook in monitoredAudiobooks)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                try
                {
                    var downloadsQueuedForBook = await ProcessAudiobookAsync(
                        audiobook, searchService, downloadService, automaticScoring, downloadClientSelector, audiobookRepository, downloadRepository, fileRepository, WaitBeforeIndexerSearchAsync, stoppingToken);

                    downloadsQueued += downloadsQueuedForBook;
                    processedCount++;

                    // Update last search time
                    audiobook.LastSearchTime = DateTime.UtcNow;
                    await audiobookRepository.UpdateAsync(audiobook);

                    _logger.LogInformation("Processed audiobook '{Title}' - queued {QueuedCount} downloads",
                        audiobook.Title, downloadsQueuedForBook);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogWarning(ex, "Automatic search processing canceled/timed out for audiobook '{Title}' (ID: {Id})", audiobook.Title, audiobook.Id);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    _logger.LogError(ex, "Error processing audiobook '{Title}' (ID: {Id})", audiobook.Title, audiobook.Id);
                }
            }

            _logger.LogInformation("Automatic search cycle completed. Processed {ProcessedCount} audiobooks, queued {DownloadsQueued} total downloads",
                processedCount, downloadsQueued);
        }

        private async Task<int> ProcessAudiobookAsync(
            Audiobook audiobook,
            ISearchService searchService,
            IDownloadService downloadService,
            IAutomaticSearchScoringService automaticScoring,
            DownloadClientSelector downloadClientSelector,
            IAudiobookRepository audiobookRepository,
            IDownloadRepository downloadRepository,
            IAudiobookFileRepository fileRepository,
            Func<Task<bool>> waitBeforeIndexerSearchAsync,
            CancellationToken stoppingToken)
        {
            if (audiobook.QualityProfile == null)
            {
                _logger.LogWarning("Audiobook '{Title}' has no quality profile assigned", audiobook.Title);
                return 0;
            }

            var (skipSearch, bestExistingQuality, allDownloads) = await ShouldSkipSearchAsync(audiobook, downloadRepository, fileRepository, stoppingToken);
            if (skipSearch)
            {
                return 0;
            }

            // The checks above can be a whole book delay old once the wait ends. Repeat them so a
            // download started or an import finished meanwhile is not grabbed a second time.
            if (await waitBeforeIndexerSearchAsync())
            {
                (skipSearch, bestExistingQuality, allDownloads) = await ShouldSkipSearchAsync(audiobook, downloadRepository, fileRepository, stoppingToken);
                if (skipSearch)
                {
                    return 0;
                }
            }

            // Build search query
            var searchQuery = _resultClassifier.BuildSearchQuery(audiobook);
            _logger.LogInformation("Searching for audiobook '{Title}' with query: {Query}", audiobook.Title, searchQuery);

            // Search for results
            var searchResults = await searchService.SearchAsync(searchQuery, isAutomaticSearch: true, origin: SearchOrigin.Sweep);
            _logger.LogInformation("Found {Count} raw search results for audiobook '{Title}'", searchResults.Count, audiobook.Title);

            // Author-free fallback: release names often omit the author, so a full title+author
            // query can return nothing even when the book is available. Retry without the author,
            // then assess every broadened result with the same identity and quality policy.
            if (searchResults.Count == 0)
            {
                var fallbackQuery = _resultClassifier.BuildTitleFallbackSearchQuery(audiobook);
                if (!string.IsNullOrWhiteSpace(fallbackQuery)
                    && !string.Equals(fallbackQuery, searchQuery, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("No results for '{Title}' with the full query; retrying without the author: {Query}",
                        audiobook.Title, fallbackQuery);
                    var fallbackResults = await searchService.SearchAsync(fallbackQuery, isAutomaticSearch: true, origin: SearchOrigin.Sweep);
                    searchResults = fallbackResults;
                    _logger.LogInformation("Author-free fallback for '{Title}': {Raw} result(s) to assess",
                        audiobook.Title, fallbackResults.Count);
                }
            }

            // Broadcast detailed debug info about the raw search results to help diagnose automatic search failures
            try
            {
                // Build a concise summary of up to 10 raw results
                var rawSummaries = searchResults.Take(10).Select(r => new
                {
                    title = r.Title,
                    asin = r.Asin,
                    source = r.Source,
                    sizeMB = r.Size > 0 ? (r.Size / 1024 / 1024) : -1,
                    seeders = r.Seeders,
                    format = r.Format,
                    downloadType = r.DownloadType
                }).ToList();

                using var scope = _serviceScopeFactory.CreateScope();
                var hub = scope.ServiceProvider.GetRequiredService<IHubContext<DownloadHub>>();
                // Send structured payload with type and audiobookId so the UI can ignore automatic messages by default
                await hub.Clients.All.SendCoreAsync("SearchProgress", new object[] { new { message = $"Automatic search query: {searchQuery}", details = new { rawCount = searchResults.Count, rawSamples = rawSummaries }, type = "automatic", audiobookId = audiobook.Id } });
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogDebug(ex, "Failed to broadcast raw search results summary for audiobook {Id}", audiobook.Id);
            }

            if (!searchResults.Any())
            {
                _logger.LogInformation("No search results found for audiobook '{Title}'", audiobook.Title);
                return 0;
            }

            // Drop releases already blocked for this book, or held by the client as a duplicate of
            // content it already has, before they are assessed.
            //
            // This is the path that actually re-grabs: it selects here and calls StartDownloadAsync
            // directly rather than going through SearchAndDownloadAsync, so without this a release
            // could fail, be blocked, and be grabbed again by the next automatic pass.
            using (var blocklistScope = _serviceScopeFactory.CreateScope())
            {
                var blocklistService = blocklistScope.ServiceProvider.GetRequiredService<IBlocklistService>();
                searchResults = ClientDuplicateReleaseFilter.Exclude(
                    allDownloads,
                    await BlockedReleaseFilter.ExcludeAsync(blocklistService, audiobook.Id, searchResults, _logger),
                    _logger);
            }

            var assessment = await automaticScoring.ScoreAsync(audiobook, searchResults, audiobook.QualityProfile, stoppingToken);

            // Ranked once, in the order SelectBest considers candidates, so the debug log and the
            // grab agree and neither inherits indexer order. The operator's own quality ordering
            // decides first and the score breaks its ties; see
            // AutomaticSearchScoringService.InSelectionOrder for why.
            var ranked = AutomaticSearchScoringService.InSelectionOrder(assessment.Candidates, audiobook.QualityProfile).ToList();
            _logger.LogInformation("Scored {Count} search results for audiobook '{Title}':", ranked.Count, audiobook.Title);

            // Broadcast scored result summaries (score + rejection reasons) to aid debugging
            try
            {
                var scoredSummaries = ranked.Select(s => new
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

                using var scope2 = _serviceScopeFactory.CreateScope();
                var hub2 = scope2.ServiceProvider.GetRequiredService<IHubContext<DownloadHub>>();
                await hub2.Clients.All.SendCoreAsync("SearchProgress", new object[] { new { message = $"Scored results for '{audiobook.Title}'", details = new { scoredCount = ranked.Count, scoredSamples = scoredSummaries }, type = "automatic", audiobookId = audiobook.Id } });
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogDebug(ex, "Failed to broadcast scored search results for audiobook {Id}", audiobook.Id);
            }
            // Same ordering as the selection below, so the log lists candidates in the order
            // they were actually considered.
            foreach (var scoredResult in ranked)
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

            var topResult = automaticScoring.SelectBest(assessment);

            if (topResult == null)
            {
                _logger.LogInformation("No acceptable search results found for audiobook '{Title}' after quality filtering", audiobook.Title);
                return 0;
            }

            // Take the best candidate that is actually an upgrade, rather than testing only the
            // first and abandoning the search when it fails. The gate below resolves a quality
            // label by exact profile name while the ranking resolves it by codec and bitrate, so
            // the two disagree on a label such as a bare "AAC": the ranking places it and the gate
            // cannot, and one unresolvable release at the top used to discard every release
            // behind it, including ones that would have passed. SelectBest still picks among the
            // upgrades, so they are taken in the same order as every other selection.
            if (!string.IsNullOrEmpty(bestExistingQuality))
            {
                topResult = automaticScoring.SelectBest(assessment with
                {
                    Candidates = assessment.Candidates
                        .Where(candidate => _qualityEvaluator.IsQualityBetter(candidate.SearchResult.Quality, bestExistingQuality, audiobook.QualityProfile))
                        .ToList()
                });

                if (topResult == null)
                {
                    _logger.LogInformation("No result for audiobook '{Title}' is better than the existing quality '{ExistingQuality}', skipping download",
                        audiobook.Title, bestExistingQuality);
                    return 0;
                }

                _logger.LogInformation("Chose quality '{ResultQuality}' over existing quality '{ExistingQuality}', proceeding with download",
                    topResult.SearchResult.Quality, bestExistingQuality);
            }
            else
            {
                _logger.LogInformation("No existing files for audiobook '{Title}', proceeding with download", audiobook.Title);
            }

            _logger.LogInformation("Chose result for audiobook '{Title}': {ResultTitle} (Score: {Score}, Quality: {Quality})",
                audiobook.Title, topResult.SearchResult.Title, topResult.TotalScore, topResult.SearchResult.Quality);

            // Add score to the search result for tracking
            topResult.SearchResult.Score = topResult.TotalScore;

            // Queue download for the top result
            var downloadsQueued = 0;
            try
            {
                // Determine appropriate download client for this result
                var protocol = _resultClassifier.ResolveProtocol(topResult.SearchResult);
                var downloadClientId = await downloadClientSelector.GetAppropriateDownloadClientAsync(
                    protocol, topResult.SearchResult.IndexerId);

                if (string.IsNullOrEmpty(downloadClientId))
                {
                    _logger.LogWarning("No suitable download client found for protocol: {Protocol}", protocol);
                    return 0;
                }

                if (!await automaticScoring.IsCurrentAsync(assessment, audiobook.Id, stoppingToken)) return 0;
                await downloadService.StartDownloadAsync(topResult.SearchResult, downloadClientId, audiobook.Id, stoppingToken);
                downloadsQueued++;

                _logger.LogInformation("Queued download for audiobook '{Title}': {ResultTitle} (Score: {Score})",
                    audiobook.Title, topResult.SearchResult.Title, topResult.TotalScore);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Failed to queue download for audiobook '{Title}': {ResultTitle}",
                    audiobook.Title, topResult.SearchResult.Title);
            }

            return downloadsQueued;
        }

    }
}
