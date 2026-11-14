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
    /// One automatic-search sweep. Reads due books in pages, oldest due first, and searches each on
    /// its due indexers only. Every book it handles ends the cycle with a persisted outcome, or is
    /// left untouched because a bound stopped the cycle. The cycle stops at the book cap, when its
    /// request allowance is spent, at the page bound, or on cancellation.
    /// </summary>
    public sealed class AutomaticSearchProcessor : IAutomaticSearchProcessor
    {
        private readonly ILogger<AutomaticSearchProcessor> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public AutomaticSearchProcessor(
            ILogger<AutomaticSearchProcessor> logger,
            IServiceScopeFactory serviceScopeFactory)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
        }

        public async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting automatic search cycle for monitored audiobooks");

            using var scope = _serviceScopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            var settings = await services.GetRequiredService<IConfigurationService>().GetApplicationSettingsAsync();
            var timing = AutomaticSearchTiming.FromSettings(settings);
            var maxBooks = Math.Clamp(
                settings.AutomaticSearchMaxBooksPerCycle,
                AutomaticSearchBounds.MinBooksPerCycle,
                AutomaticSearchBounds.MaxBooksPerCycle);
            var maxRequests = Math.Clamp(
                settings.AutomaticSearchMaxRequestsPerCycle,
                AutomaticSearchBounds.MinRequestsPerCycle,
                AutomaticSearchBounds.MaxRequestsPerCycle);
            var clock = services.GetService<TimeProvider>() ?? TimeProvider.System;
            var store = services.GetRequiredService<IAutomaticSearchScheduleStore>();
            var indexers = await services.GetRequiredService<IIndexerRepository>().GetEnabledAsync(isAutomaticSearch: true, cancellationToken);
            var preChecks = new AutomaticSearchPreChecks(
                services.GetRequiredService<IDownloadRepository>(),
                services.GetRequiredService<IAudiobookFileRepository>(),
                _logger);
            var bookSearch = new AutomaticSearchBookSearch(
                _logger,
                _serviceScopeFactory,
                services.GetRequiredService<ISearchService>(),
                services.GetRequiredService<IAutomaticSearchAcquisition>(),
                store,
                clock,
                timing.BookCooldown,
                new CycleRequestAllowance(maxRequests),
                maxRequests);

            // Selection uses the cycle's start time throughout, so a book that became due while the
            // cycle ran waits for the next one, behind every book that was already due.
            var selectionUtc = clock.GetUtcNow().UtcDateTime;
            var processedIds = new List<int>();
            var searchedBooks = 0;
            var downloadsQueued = 0;
            var hasSearched = false;
            var stopped = false;

            for (var page = 0; page < AutomaticSearchBounds.SelectionPageBound && !stopped && !cancellationToken.IsCancellationRequested; page++)
            {
                var books = await store.GetDuePageAsync(selectionUtc, processedIds, AutomaticSearchBounds.SelectionPageSize, cancellationToken);
                if (books.Count == 0)
                {
                    break;
                }

                var pairStates = (await store.GetPairStatesAsync(books.Select(book => book.Id).ToList(), cancellationToken))
                    .ToLookup(pair => pair.AudiobookId);
                foreach (var audiobook in books)
                {
                    if (cancellationToken.IsCancellationRequested || searchedBooks >= maxBooks || bookSearch.AllowanceSpent)
                    {
                        stopped = true;
                        break;
                    }

                    // Excluded before anything can fail, so a book whose outcome could not be
                    // persisted is still not read again in this cycle.
                    processedIds.Add(audiobook.Id);
                    var progress = new AutomaticSearchBookProgress();
                    try
                    {
                        var dueIndexers = await SelectDueIndexersAsync(audiobook, preChecks, indexers, pairStates[audiobook.Id], clock, cancellationToken);

                        // Pause between books that query the indexers so a large wanted list does not
                        // hammer them. A book skipped before its search sends no request, so it does
                        // not wait, and a run of skipped books does not hold up the next real search.
                        if (dueIndexers.Count > 0 && hasSearched && timing.BookDelay > TimeSpan.Zero)
                        {
                            await Task.Delay(timing.BookDelay, clock, cancellationToken);

                            // The checks above can be a whole book delay old once the wait ends. Repeat
                            // them so a download started or an import finished meanwhile is not grabbed again.
                            dueIndexers = await SelectDueIndexersAsync(audiobook, preChecks, indexers, pairStates[audiobook.Id], clock, cancellationToken);
                        }

                        if (dueIndexers.Count == 0)
                        {
                            await store.PersistBookAsync(
                                new BookScheduleUpdate(audiobook.Id, BookSearchOutcome.NotNeeded, AnyPairCompleted: false),
                                clock.GetUtcNow().UtcDateTime,
                                timing.BookCooldown,
                                cancellationToken);
                            continue;
                        }

                        hasSearched = true;
                        var result = await bookSearch.SearchAsync(audiobook, dueIndexers, progress, cancellationToken);
                        downloadsQueued += result.DownloadsQueued;
                        _logger.LogInformation("Processed audiobook '{Title}': {Outcome}, queued {QueuedCount} downloads",
                            audiobook.Title, result.Outcome, result.DownloadsQueued);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        stopped = true;
                        break;
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
                    {
                        _logger.LogError(ex, "Error processing audiobook '{Title}' (ID: {Id})", audiobook.Title, audiobook.Id);
                        await RecordBookFailedAsync(store, audiobook, progress, clock, timing.BookCooldown, cancellationToken);
                    }
                    finally
                    {
                        // Every book that reached the indexer search counts toward the cap, whatever its outcome.
                        if (progress.ReachedSearch)
                        {
                            searchedBooks++;
                        }
                    }
                }

                if (books.Count < AutomaticSearchBounds.SelectionPageSize)
                {
                    break;
                }
            }

            _logger.LogInformation("Automatic search cycle completed. Handled {HandledCount} audiobooks, searched {SearchedCount}, queued {DownloadsQueued} total downloads",
                processedIds.Count, searchedBooks, downloadsQueued);
        }

        // The indexers enabled for automatic search whose pair with this book is due now; a pair with
        // no row has never been searched and is due. Empty when a pre-check skips the book.
        private static async Task<IReadOnlyList<Indexer>> SelectDueIndexersAsync(
            Audiobook audiobook,
            AutomaticSearchPreChecks preChecks,
            IReadOnlyList<Indexer> indexers,
            IEnumerable<AudiobookIndexerSearchState> pairStates,
            TimeProvider clock,
            CancellationToken cancellationToken)
        {
            if (!(await preChecks.EvaluateAsync(audiobook, cancellationToken)).Needed)
            {
                return Array.Empty<Indexer>();
            }

            var nowUtc = clock.GetUtcNow().UtcDateTime;
            var notYetDue = pairStates
                .Where(pair => pair.NextAttemptUtc > nowUtc)
                .Select(pair => pair.IndexerId)
                .ToHashSet();
            return indexers.Where(indexer => !notYetDue.Contains(indexer.Id)).ToList();
        }

        private async Task RecordBookFailedAsync(
            IAutomaticSearchScheduleStore store,
            Audiobook audiobook,
            AutomaticSearchBookProgress progress,
            TimeProvider clock,
            TimeSpan cooldown,
            CancellationToken cancellationToken)
        {
            try
            {
                await store.PersistBookAsync(
                    new BookScheduleUpdate(audiobook.Id, BookSearchOutcome.BookFailed, progress.AnyPairCompleted),
                    clock.GetUtcNow().UtcDateTime,
                    cooldown,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                // The book is already excluded for the rest of this cycle; its due time is unchanged,
                // so the next cycle takes it again.
                _logger.LogError(ex, "Could not record the failed automatic search of audiobook '{Title}' (ID: {Id})", audiobook.Title, audiobook.Id);
            }
        }
    }
}
