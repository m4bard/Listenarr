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
    /// What the cycle needs to know about a book while it is being searched, including after an
    /// exception: whether it reached the indexer search (it then counts toward the book cap) and
    /// whether any indexer completed (its last search time is then stamped).
    /// </summary>
    internal sealed class AutomaticSearchBookProgress
    {
        public bool ReachedSearch { get; set; }

        public bool AnyPairCompleted { get; set; }
    }

    internal sealed record AutomaticSearchBookResult(BookSearchOutcome Outcome, int DownloadsQueued);

    /// <summary>
    /// Searches one book on its due indexers, one indexer at a time, so a cycle's request allowance
    /// is spent on whole indexer searches in due order. A failed or deferred indexer is persisted as
    /// soon as it finishes; the completed ones once the acquisition has decided the book; then the book.
    /// </summary>
    internal sealed class AutomaticSearchBookSearch(
        ILogger logger,
        IServiceScopeFactory serviceScopeFactory,
        ISearchService searchService,
        IAutomaticSearchAcquisition acquisition,
        IAutomaticSearchScheduleStore store,
        TimeProvider clock,
        TimeSpan cooldown,
        CycleRequestAllowance allowance,
        int maxRequests)
    {
        private readonly AutomaticSearchResultClassifier _resultClassifier = new(logger);

        public bool AllowanceSpent => allowance.Taken >= maxRequests;

        public async Task<AutomaticSearchBookResult> SearchAsync(
            Audiobook audiobook,
            IReadOnlyList<Indexer> dueIndexers,
            AutomaticSearchBookProgress progress,
            CancellationToken ct)
        {
            progress.ReachedSearch = true;
            var searchQuery = _resultClassifier.BuildSearchQuery(audiobook);
            logger.LogInformation("Searching for audiobook '{Title}' with query: {Query}", audiobook.Title, searchQuery);

            var outcomes = new List<(Indexer Indexer, IndexerSearchOutcome Outcome)>();
            var cutByRequestBound = false;
            foreach (var indexer in dueIndexers)
            {
                if (AllowanceSpent)
                {
                    // The indexers not reached stay due, and the book keeps its due time.
                    cutByRequestBound = true;
                    break;
                }

                var outcome = await SearchIndexerAsync(searchQuery, indexer, IndexerRequestKind.Search, ct);
                if (outcome == null)
                {
                    continue;
                }

                if (outcome.Status == IndexerSearchStatus.Cancelled)
                {
                    return await RecordCancelledAsync(audiobook, outcomes, progress);
                }

                outcomes.Add((indexer, outcome));
                cutByRequestBound |= await PersistUnfinishedAsync(audiobook, indexer, outcome, ct);
            }

            var candidates = outcomes.SelectMany(entry => entry.Outcome.Results).ToList();
            logger.LogInformation("Found {Count} raw search results for audiobook '{Title}'", candidates.Count, audiobook.Title);

            // Author-free fallback: release names often omit the author, so a full title+author
            // query can return nothing even when the book is available. An indexer whose full query
            // completed empty is asked again without the author, then the broadened results are
            // constrained to titles that still match this book, so a same-title/different-book
            // release can't pass quality scoring and be grabbed.
            if (!cutByRequestBound)
            {
                var fallback = await SearchFallbackAsync(audiobook, searchQuery, outcomes, ct);
                if (fallback == null)
                {
                    return await RecordCancelledAsync(audiobook, outcomes, progress);
                }

                cutByRequestBound = fallback.Value;
                candidates = outcomes.SelectMany(entry => entry.Outcome.Results).ToList();
            }

            progress.AnyPairCompleted = outcomes.Any(entry => entry.Outcome.Status == IndexerSearchStatus.Completed);
            await BroadcastRawResultsAsync(audiobook, searchQuery, candidates);

            var acquired = candidates.Count == 0
                ? null
                : await acquisition.AcquireAsync(audiobook, candidates, SearchOrigin.Sweep, ct);
            return await RecordBookAsync(audiobook, outcomes, acquired, cutByRequestBound, progress, ct);
        }

        // Null when the indexer is no longer enabled for automatic search.
        private async Task<IndexerSearchOutcome?> SearchIndexerAsync(
            string query,
            Indexer indexer,
            IndexerRequestKind kind,
            CancellationToken ct)
        {
            var report = await searchService.SearchIndexersDetailedAsync(
                query,
                new IndexerSearchContext(SearchOrigin.Sweep, allowance, kind),
                new[] { indexer.Id },
                isAutomaticSearch: true,
                request: null,
                ct);
            return report.Outcomes.FirstOrDefault(outcome => outcome.IndexerId == indexer.Id);
        }

        // Runs the title-only query on every indexer whose full query completed with no results.
        // Returns whether the request bound cut a search, or null when the cycle was cancelled.
        private async Task<bool?> SearchFallbackAsync(
            Audiobook audiobook,
            string searchQuery,
            List<(Indexer Indexer, IndexerSearchOutcome Outcome)> outcomes,
            CancellationToken ct)
        {
            var fallbackQuery = _resultClassifier.BuildTitleFallbackSearchQuery(audiobook);
            if (string.IsNullOrWhiteSpace(fallbackQuery)
                || string.Equals(fallbackQuery, searchQuery, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            for (var index = 0; index < outcomes.Count; index++)
            {
                var (indexer, primary) = outcomes[index];
                if (primary.Status != IndexerSearchStatus.Completed || primary.Results.Count > 0)
                {
                    continue;
                }

                if (AllowanceSpent)
                {
                    return true;
                }

                logger.LogInformation("No results for '{Title}' from indexer '{Indexer}' with the full query; retrying without the author: {Query}",
                    audiobook.Title, indexer.Name, fallbackQuery);
                var fallback = await SearchIndexerAsync(fallbackQuery, indexer, IndexerRequestKind.FallbackQuery, ct);
                if (fallback == null)
                {
                    continue;
                }

                if (fallback.Status == IndexerSearchStatus.Cancelled)
                {
                    return null;
                }

                var kept = _resultClassifier.ConstrainFallbackResultsToTitle(fallback.Results, audiobook);
                logger.LogInformation("Author-free fallback for '{Title}': {Raw} raw result(s), {Kept} kept after title match",
                    audiobook.Title, fallback.Results.Count, kept.Count);
                // One indexer search covers both queries: its requests add up, and a fallback that
                // did not complete decides how the indexer's search ended.
                var merged = fallback with { Results = kept, RequestsSent = primary.RequestsSent + fallback.RequestsSent };
                outcomes[index] = (indexer, merged);
                if (await PersistUnfinishedAsync(audiobook, indexer, merged, ct))
                {
                    return true;
                }
            }

            return false;
        }

        // Persists a failed or deferred indexer search at once. Returns whether the cycle's request
        // bound cut it, so its book keeps its due time and heads the next cycle.
        private async Task<bool> PersistUnfinishedAsync(
            Audiobook audiobook,
            Indexer indexer,
            IndexerSearchOutcome outcome,
            CancellationToken ct)
        {
            var nowUtc = clock.GetUtcNow().UtcDateTime;
            switch (outcome.Status)
            {
                case IndexerSearchStatus.Failed:
                    await store.PersistPairAsync(
                        new PairScheduleUpdate(audiobook.Id, indexer.Id, PairScheduleResult.Failed, outcome.Deferral?.UntilUtc),
                        nowUtc, cooldown, ct);
                    return false;
                case IndexerSearchStatus.Deferred when AllowanceSpent && outcome.RequestsSent >= maxRequests:
                    // This search alone used the whole allowance, so it can never fit in a cycle.
                    // Retrying it on the failure ladder keeps it from spending every cycle's allowance.
                    logger.LogWarning(
                        "Indexer '{Indexer}' needed more than the {MaxRequests} requests one automatic-search cycle may send for audiobook '{Title}' ({Sent} sent); recording the search as failed",
                        indexer.Name, maxRequests, audiobook.Title, outcome.RequestsSent);
                    await store.PersistPairAsync(
                        new PairScheduleUpdate(audiobook.Id, indexer.Id, PairScheduleResult.Failed),
                        nowUtc, cooldown, ct);
                    return false;
                case IndexerSearchStatus.Deferred:
                    await store.PersistPairAsync(
                        new PairScheduleUpdate(audiobook.Id, indexer.Id, PairScheduleResult.Deferred, outcome.Deferral?.UntilUtc ?? nowUtc),
                        nowUtc, cooldown, ct);
                    return AllowanceSpent;
                default:
                    return false;
            }
        }

        private async Task<AutomaticSearchBookResult> RecordBookAsync(
            Audiobook audiobook,
            List<(Indexer Indexer, IndexerSearchOutcome Outcome)> outcomes,
            AcquisitionResult? acquired,
            bool cutByRequestBound,
            AutomaticSearchBookProgress progress,
            CancellationToken ct)
        {
            var nowUtc = clock.GetUtcNow().UtcDateTime;
            if (acquired?.Outcome is BookSearchOutcome.BookFailed or BookSearchOutcome.NotNeeded)
            {
                // The completed searches are not recorded: nothing was decided from them, and they
                // are repeated when the book is next due.
                await store.PersistBookAsync(
                    new BookScheduleUpdate(audiobook.Id, acquired.Outcome, progress.AnyPairCompleted),
                    nowUtc, cooldown, ct);
                return new AutomaticSearchBookResult(acquired.Outcome, 0);
            }

            var grab = acquired?.Outcome switch
            {
                BookSearchOutcome.Satisfied => GrabResult.Queued,
                BookSearchOutcome.GrabNotCompleted => GrabResult.NotCompleted,
                _ => GrabResult.NotAttempted
            };
            var outcome = BookSearchOutcomeDecision.Decide(
                outcomes.Select(entry => entry.Outcome).ToList(),
                acceptableFound: grab != GrabResult.NotAttempted,
                grab);
            var grabDeferredUntilUtc = acquired?.GrabDeferral?.UntilUtc;
            var (pairResult, pairNotBeforeUtc) = outcome switch
            {
                BookSearchOutcome.Satisfied => (PairScheduleResult.Satisfied, (DateTime?)null),
                BookSearchOutcome.GrabNotCompleted => (PairScheduleResult.GrabNotCompleted,
                    grabDeferredUntilUtc ?? AutomaticSearchSchedulePolicy.NextBookFailureAttempt(
                        nowUtc, audiobook.AutomaticSearchFailureCount + 1, cooldown)),
                _ => (PairScheduleResult.NoAcceptableResults, null)
            };
            foreach (var (indexer, _) in outcomes.Where(entry => entry.Outcome.Status == IndexerSearchStatus.Completed))
            {
                await store.PersistPairAsync(
                    new PairScheduleUpdate(audiobook.Id, indexer.Id, pairResult, pairNotBeforeUtc),
                    nowUtc, cooldown, ct);
            }

            // A book the request bound cut keeps its selection-time due time, as a cancelled one
            // does, so it heads the next cycle and its remaining searches get the whole allowance.
            var keepsDueTime = cutByRequestBound
                && outcome is BookSearchOutcome.PartiallyCompleted or BookSearchOutcome.Deferred;
            await store.PersistBookAsync(
                new BookScheduleUpdate(
                    audiobook.Id,
                    keepsDueTime ? BookSearchOutcome.Cancelled : outcome,
                    progress.AnyPairCompleted,
                    grabDeferredUntilUtc),
                nowUtc, cooldown, ct);
            return new AutomaticSearchBookResult(outcome, acquired?.DownloadsQueued ?? 0);
        }

        // The cycle was cancelled mid-book. Indexers that completed with nothing are recorded, since
        // that does not depend on the rest of the search; the rest is left due. Written without the
        // cancelled token, so the outcomes already in hand are kept.
        private async Task<AutomaticSearchBookResult> RecordCancelledAsync(
            Audiobook audiobook,
            List<(Indexer Indexer, IndexerSearchOutcome Outcome)> outcomes,
            AutomaticSearchBookProgress progress)
        {
            var nowUtc = clock.GetUtcNow().UtcDateTime;
            var completed = outcomes.Where(entry => entry.Outcome.Status == IndexerSearchStatus.Completed).ToList();
            foreach (var (indexer, _) in completed.Where(entry => entry.Outcome.Results.Count == 0))
            {
                await store.PersistPairAsync(
                    new PairScheduleUpdate(audiobook.Id, indexer.Id, PairScheduleResult.NoAcceptableResults),
                    nowUtc, cooldown, CancellationToken.None);
            }

            progress.AnyPairCompleted = completed.Count > 0;
            if (progress.AnyPairCompleted)
            {
                await store.PersistBookAsync(
                    new BookScheduleUpdate(audiobook.Id, BookSearchOutcome.Cancelled, AnyPairCompleted: true),
                    nowUtc, cooldown, CancellationToken.None);
            }

            return new AutomaticSearchBookResult(BookSearchOutcome.Cancelled, 0);
        }

        // Broadcast detailed debug info about the raw search results to help diagnose automatic search failures
        private async Task BroadcastRawResultsAsync(Audiobook audiobook, string searchQuery, List<IndexerSearchResult> searchResults)
        {
            try
            {
                // Build a concise summary of up to 10 raw results
                var rawSummaries = searchResults.Take(10).Select(SearchResultConverters.ToSearchResult).Select(r => new
                {
                    title = r.Title,
                    asin = r.Asin,
                    source = r.Source,
                    sizeMB = r.Size > 0 ? (r.Size / 1024 / 1024) : -1,
                    seeders = r.Seeders,
                    format = r.Format,
                    downloadType = r.DownloadType
                }).ToList();

                using var scope = serviceScopeFactory.CreateScope();
                var hub = scope.ServiceProvider.GetRequiredService<IHubContext<DownloadHub>>();
                // Send structured payload with type and audiobookId so the UI can ignore automatic messages by default
                await hub.Clients.All.SendCoreAsync("SearchProgress", new object[] { new { message = $"Automatic search query: {searchQuery}", details = new { rawCount = searchResults.Count, rawSamples = rawSummaries }, type = "automatic", audiobookId = audiobook.Id } });
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogDebug(ex, "Failed to broadcast raw search results summary for audiobook {Id}", audiobook.Id);
            }
        }
    }
}
