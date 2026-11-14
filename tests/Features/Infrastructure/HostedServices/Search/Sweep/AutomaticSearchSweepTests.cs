/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Net;
using Listenarr.Application.Search.Scheduling;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search.Sweep
{
    // The sweep's per-book and per-indexer outcomes, persisted through the schedule store on SQLite.
    [Trait("Name", "AutomaticSearchSweepTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class AutomaticSearchSweepTests : AutomaticSearchSweepTestBase
    {
        [Fact]
        public async Task FixedFailureBacklog_IsBoundedAndBacksOff()
        {
            await ConfigureAsync(maxBooks: 10);
            await AddIndexerAsync("Only");
            var throwers = await AddBooksAsync(100, StartUtc.AddHours(-1));
            var healthy = (await AddBooksAsync(1, StartUtc.AddHours(-1))).Single();
            var throwerQueries = throwers.Select(book => book.Query).ToHashSet();
            Indexers.Script = (_, query) => Returns(throwerQueries.Contains(query) ? ScoringThrowsSource : AcceptableSource);
            var searchedAt = new Dictionary<string, List<DateTime>>();
            int? healthyCycle = null;

            for (var cycle = 1; cycle <= 40; cycle++)
            {
                await RunCycleAsync();
                var searched = TakeSearches().Select(search => search.Query).Distinct().ToList();
                // (ii) every book that reached the indexer search counts toward the cap, throwers included.
                Assert.True(searched.Count <= 10, $"cycle {cycle} searched {searched.Count} books, over the cap of 10");
                foreach (var query in searched)
                {
                    searchedAt.TryAdd(query, new List<DateTime>());
                    searchedAt[query].Add(Clock.UtcNow);
                }

                healthyCycle ??= searched.Contains(healthy.Query) ? cycle : null;
                Clock.Advance(TimeSpan.FromMinutes(1));
            }

            // (i) the healthy book is reached once the throwers ahead of it have each had a turn.
            Assert.True(healthyCycle is <= 11, $"the healthy book was first searched in cycle {healthyCycle?.ToString() ?? "none"}");
            // (iii) a thrower is searched again only once its book-failure wait (15 min, doubling) has passed.
            foreach (var query in throwerQueries)
            {
                var times = searchedAt.GetValueOrDefault(query) ?? new List<DateTime>();
                for (var attempt = 1; attempt < times.Count; attempt++)
                {
                    var wait = TimeSpan.FromMinutes(15 * Math.Pow(2, attempt - 1));
                    Assert.True(
                        times[attempt] - times[attempt - 1] >= wait,
                        $"'{query}' was searched again after {(times[attempt] - times[attempt - 1]).TotalMinutes} min, before its {wait.TotalMinutes} min wait");
                }
            }
        }

        [Fact]
        public async Task Restart_PreservesPairAndBookState()
        {
            await ConfigureAsync();
            var answered = await AddIndexerAsync("Answered");
            var failing = await AddIndexerAsync("Failing");
            var book = (await AddBooksAsync(1, StartUtc.AddMinutes(-5))).Single();
            Server.StatusFor = indexerId => indexerId == failing ? HttpStatusCode.InternalServerError : HttpStatusCode.OK;
            await RunCycleAsync();
            TakeSearches();

            // A new process: a new provider, scope, store and processor over the same database file.
            BuildHost();
            Server.StatusFor = _ => HttpStatusCode.OK;
            Clock.Advance(TimeSpan.FromHours(1));
            await RunCycleAsync();

            Assert.Equal(new[] { (failing, book.Query) }, TakeSearches().Select(search => (search.IndexerId, search.Query)).Distinct());
            var completedPair = await Database.ReadPairAsync(book.Id, answered);
            Assert.Equal(nameof(PairScheduleResult.NoAcceptableResults), completedPair!.LastOutcome);
        }

        [Fact]
        public async Task CallerCancel_AfterAComplete_KeepsAAndLeavesBDue()
        {
            await ConfigureAsync();
            var answered = await AddIndexerAsync("Answered");
            var silent = await AddIndexerAsync("Silent");
            var dueUtc = StartUtc.AddMinutes(-5);
            var book = (await AddBooksAsync(1, dueUtc)).Single();
            Server.Silent[silent] = true;
            using var cycle = new CancellationTokenSource();
            _ = Server.SilentRequestArrived.Task.ContinueWith(_ => cycle.Cancel(), TaskScheduler.Default);

            await RunCycleAsync(cycle.Token);

            var stored = await Database.ReadPairAsync(book.Id, answered);
            Assert.NotNull(stored);
            Assert.Equal(1, stored.NoResultCount);
            Assert.Null(await Database.ReadPairAsync(book.Id, silent));
            var (bookDueUtc, failureCount, _, _) = await Database.ReadBookAsync(book.Id);
            Assert.Equal(dueUtc, bookDueUtc);
            Assert.Equal(0, failureCount);
        }

        [Fact]
        public async Task CancelledToken_WritesNothing()
        {
            await ConfigureAsync();
            var silent = await AddIndexerAsync("Silent");
            var dueUtc = StartUtc.AddMinutes(-5);
            var book = (await AddBooksAsync(1, dueUtc)).Single();
            Server.Silent[silent] = true;
            using var cycle = new CancellationTokenSource();
            _ = Server.SilentRequestArrived.Task.ContinueWith(_ => cycle.Cancel(), TaskScheduler.Default);

            await RunCycleAsync(cycle.Token);

            Assert.Null(await Database.ReadPairAsync(book.Id, silent));
            Assert.Equal((dueUtc, 0, (DateTime?)null, true), await Database.ReadBookAsync(book.Id));
        }

        [Fact]
        public async Task PartialOutage_OnlyFailedIndexerRetried()
        {
            await ConfigureAsync();
            var answered = await AddIndexerAsync("Answered");
            var failing = await AddIndexerAsync("Failing");
            var book = (await AddBooksAsync(1, StartUtc.AddMinutes(-5))).Single();
            Server.StatusFor = indexerId => indexerId == failing ? HttpStatusCode.InternalServerError : HttpStatusCode.OK;

            await RunCycleAsync();

            var noResult = await Database.ReadPairAsync(book.Id, answered);
            var failed = await Database.ReadPairAsync(book.Id, failing);
            Assert.Equal((1, 0), (noResult!.NoResultCount, noResult.FailureCount));
            Assert.Equal((0, 1), (failed!.NoResultCount, failed.FailureCount));
            Assert.Equal(StartUtc.AddHours(1), failed.NextAttemptUtc);
            Assert.Equal(StartUtc.AddHours(1), (await Database.ReadBookAsync(book.Id)).DueUtc);
            TakeSearches();

            Clock.Advance(TimeSpan.FromHours(1));
            await RunCycleAsync();

            Assert.Equal(new[] { failing }, TakeSearches().Select(search => search.IndexerId).Distinct());
        }

        [Fact]
        public async Task Deferral_DoesNotGrowNoResult()
        {
            await ConfigureAsync();
            await AddIndexerAsync("Answered");
            var deferred = await AddIndexerAsync("Deferred");
            var book = (await AddBooksAsync(1, StartUtc.AddMinutes(-5))).Single();
            var untilUtc = StartUtc.AddHours(2);
            Gate.DeferUntil[deferred] = untilUtc;

            await RunCycleAsync();

            var pair = await Database.ReadPairAsync(book.Id, deferred);
            Assert.NotNull(pair);
            Assert.Equal((0, 0, untilUtc), (pair.NoResultCount, pair.FailureCount, pair.NextAttemptUtc));
            Assert.Equal(untilUtc, (await Database.ReadBookAsync(book.Id)).DueUtc);
            Assert.Equal(0, Server.ServedFor(deferred));
            TakeSearches();

            Clock.Advance(TimeSpan.FromHours(2));
            await RunCycleAsync();

            Assert.Equal(new[] { deferred }, TakeSearches().Select(search => search.IndexerId).Distinct());
            Assert.Equal(1, Server.ServedFor(deferred));
        }

        [Theory]
        [InlineData(ScoringThrowsSource, DirectDownloadMetadataKeys.ClientId, nameof(BookSearchOutcome.BookFailed))]
        [InlineData(AcceptableSource, "Torrent", nameof(BookSearchOutcome.GrabNotCompleted))]
        [InlineData(StartThrowsSource, DirectDownloadMetadataKeys.ClientId, nameof(BookSearchOutcome.GrabNotCompleted))]
        public async Task BookLevelOutcomes(string source, string downloadType, string expected)
        {
            await ConfigureAsync();
            var indexer = await AddIndexerAsync("Only");
            var book = (await AddBooksAsync(1, StartUtc.AddMinutes(-5))).Single();
            Indexers.Script = (_, _) => Returns(source, downloadType: downloadType);

            await RunCycleAsync();

            var (dueUtc, failureCount, lastSearchUtc, _) = await Database.ReadBookAsync(book.Id);
            Assert.Equal(1, failureCount);
            Assert.Equal(StartUtc.AddMinutes(15), dueUtc);
            Assert.Equal(StartUtc, lastSearchUtc);
            var pair = await Database.ReadPairAsync(book.Id, indexer);
            if (expected == nameof(BookSearchOutcome.BookFailed))
            {
                // The exception changes no pair counter; the completed search is simply repeated.
                Assert.Null(pair);
            }
            else
            {
                Assert.Equal((nameof(PairScheduleResult.GrabNotCompleted), 0, 0), (pair!.LastOutcome, pair.NoResultCount, pair.FailureCount));
                Assert.Equal(dueUtc, pair.NextAttemptUtc);
            }
        }

        [Fact]
        public async Task InCycleAndCrossCycleProgress()
        {
            await ConfigureAsync();
            await AddIndexerAsync("Only");
            // More books than one selection page, so the cycle reads a second page.
            var books = await AddBooksAsync(60, StartUtc.AddMinutes(-5));
            var broken = books[0];
            DecorateStore = store => new FailingBookWriteStore(store, broken.Id);
            BuildHost();

            await RunCycleAsync();

            var searches = TakeSearches().Where(search => search.Query == broken.Query).ToList();
            Assert.Single(searches.Select(search => search.Kind).Where(kind => kind == IndexerRequestKind.Search));
            foreach (var book in books.Skip(1))
            {
                Assert.True((await Database.ReadBookAsync(book.Id)).DueUtc > Clock.UtcNow, $"book {book.Id} is still due");
            }
        }

        private sealed class FailingBookWriteStore(IAutomaticSearchScheduleStore inner, int brokenBookId) : IAutomaticSearchScheduleStore
        {
            public Task<IReadOnlyList<Audiobook>> GetDuePageAsync(DateTime nowUtc, IReadOnlyCollection<int> excludedIds, int pageSize, CancellationToken ct = default) =>
                inner.GetDuePageAsync(nowUtc, excludedIds, pageSize, ct);

            public Task<IReadOnlyList<AudiobookIndexerSearchState>> GetPairStatesAsync(IReadOnlyCollection<int> audiobookIds, CancellationToken ct = default) =>
                inner.GetPairStatesAsync(audiobookIds, ct);

            public Task PersistPairAsync(PairScheduleUpdate update, DateTime nowUtc, TimeSpan cooldown, CancellationToken ct = default) =>
                update.AudiobookId == brokenBookId
                    ? throw new InvalidOperationException("pair write failed")
                    : inner.PersistPairAsync(update, nowUtc, cooldown, ct);

            public Task PersistBookAsync(BookScheduleUpdate update, DateTime nowUtc, TimeSpan cooldown, CancellationToken ct = default) =>
                update.AudiobookId == brokenBookId
                    ? throw new InvalidOperationException("book write failed")
                    : inner.PersistBookAsync(update, nowUtc, cooldown, ct);
        }
    }
}
