/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Application.Search.Scheduling;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search.Sweep
{
    // The per-cycle request bound, counted where the requests land (the stub server), not at the gate
    // that is meant to bound them. 250 is the lowest allowance a configuration can ask for.
    [Trait("Name", "AutomaticSearchRequestBoundTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class AutomaticSearchRequestBoundTests : AutomaticSearchSweepTestBase
    {
        private const int Allowance = 250;

        [Fact]
        public async Task CycleRequestBound_CountedAtServer()
        {
            await ConfigureAsync(maxRequests: Allowance);
            var first = await AddIndexerAsync("First");
            var second = await AddIndexerAsync("Second");
            await AddBooksAsync(40, StartUtc.AddMinutes(-5));
            // Each search needs its query plus five enrichment requests: 40 books x 2 indexers x 6 = 480.
            Indexers.Script = (_, _) => Returns(RejectedSource, requests: 6);

            await RunCycleAsync();

            Assert.True(Server.Total <= Allowance, $"the server saw {Server.Total} requests in one cycle, over {Allowance}");
            Assert.True(Server.ServedFor(first) > 0 && Server.ServedFor(second) > 0);
        }

        [Fact]
        public async Task ConfiguredAllowanceBelowTheFloor_IsRaisedToTheFloor()
        {
            await ConfigureAsync(maxRequests: 100);
            await AddIndexerAsync("Only");
            await AddBooksAsync(1, StartUtc.AddMinutes(-5));
            Indexers.Script = (_, _) => Returns(RejectedSource, requests: 202);

            await RunCycleAsync();

            Assert.Equal(202, Server.Total);
        }

        [Fact]
        public async Task RequestBound_CutBookHeadsNextCycle()
        {
            await ConfigureAsync(maxRequests: Allowance);
            var indexer = await AddIndexerAsync("Only");
            // 20 competitors sort ahead of the big book in cycle 1 and use 120 requests, so the big
            // book's 150 cannot fit and its pair is cut after 130.
            await AddBooksAsync(20, StartUtc.AddHours(-2));
            var big = (await AddBooksAsync(1, StartUtc.AddHours(-1))).Single();
            // 24 more come due while cycle 1 runs (one request moves the clock a second): after the
            // cycle-1 selection, before the cut. A cut book rewritten to "now" would sort behind them.
            var arriving = new List<SeededBook>();
            for (var second = 1; second <= 24; second++)
            {
                arriving.AddRange(await AddBooksAsync(1, StartUtc.AddSeconds(second)));
            }

            Server.ClockStepPerRequest = TimeSpan.FromSeconds(1);
            Indexers.Script = (_, query) => Returns(RejectedSource, requests: query == big.Query ? 150 : 6);
            var bigDueUtc = (await Database.ReadBookAsync(big.Id)).DueUtc;

            await RunCycleAsync();

            Assert.Equal(Allowance, Server.Total);
            Assert.Equal(bigDueUtc, (await Database.ReadBookAsync(big.Id)).DueUtc);
            var cut = await Database.ReadPairAsync(big.Id, indexer);
            Assert.Equal((nameof(PairScheduleResult.Deferred), 0, 0), (cut!.LastOutcome, cut.NoResultCount, cut.FailureCount));

            Clock.Advance(TimeSpan.FromMinutes(5));
            await RunCycleAsync();

            var completed = await Database.ReadPairAsync(big.Id, indexer);
            Assert.Equal(nameof(PairScheduleResult.NoAcceptableResults), completed!.LastOutcome);
            Assert.Equal(1, completed.NoResultCount);
        }

        [Fact]
        public async Task RequestBound_BeforeAFallback_DefersThePairAndKeepsTheDueTime()
        {
            var (indexer, second) = await ArrangeFallbackAfterTheLastRequestAsync(Allowance);
            var secondDueUtc = (await Database.ReadBookAsync(second.Id)).DueUtc;

            await RunCycleAsync();

            Assert.Equal(Allowance, Server.Total);
            Assert.DoesNotContain(TakeSearches(), search => search.Query == second.FallbackQuery);
            Assert.Null(await Database.ReadPairAsync(second.Id, indexer));
            Assert.Equal(secondDueUtc, (await Database.ReadBookAsync(second.Id)).DueUtc);

            Clock.Advance(TimeSpan.FromMinutes(5));
            await RunCycleAsync();

            Assert.Contains((indexer, second.FallbackQuery, IndexerRequestKind.FallbackQuery), TakeSearches());
            var completed = await Database.ReadPairAsync(second.Id, indexer);
            Assert.Equal((nameof(PairScheduleResult.NoAcceptableResults), 1), (completed!.LastOutcome, completed.NoResultCount));
        }

        [Fact]
        public async Task RoomyAllowance_SendsTheFallback_AndRecordsTheEmptyPair()
        {
            var (indexer, second) = await ArrangeFallbackAfterTheLastRequestAsync(10000);
            var secondDueUtc = (await Database.ReadBookAsync(second.Id)).DueUtc;

            await RunCycleAsync();

            Assert.Contains((indexer, second.FallbackQuery, IndexerRequestKind.FallbackQuery), TakeSearches());
            var pair = await Database.ReadPairAsync(second.Id, indexer);
            Assert.Equal((nameof(PairScheduleResult.NoAcceptableResults), 1), (pair!.LastOutcome, pair.NoResultCount));
            Assert.NotEqual(secondDueUtc, (await Database.ReadBookAsync(second.Id)).DueUtc);
        }

        [Fact]
        public async Task RequestBound_BeforeAnIndexer_LeavesItDueAndKeepsTheDueTime()
        {
            var (unreached, second) = await ArrangeSecondIndexerAfterTheLastRequestAsync(Allowance);
            var secondDueUtc = (await Database.ReadBookAsync(second.Id)).DueUtc;

            await RunCycleAsync();

            Assert.Equal(Allowance, Server.Total);
            Assert.DoesNotContain(TakeSearches(), search => search.IndexerId == unreached && search.Query == second.Query);
            Assert.Null(await Database.ReadPairAsync(second.Id, unreached));
            Assert.Equal(secondDueUtc, (await Database.ReadBookAsync(second.Id)).DueUtc);
        }

        [Fact]
        public async Task RoomyAllowance_AsksEveryIndexer_AndMovesTheDueTime()
        {
            var (unreached, second) = await ArrangeSecondIndexerAfterTheLastRequestAsync(10000);
            var secondDueUtc = (await Database.ReadBookAsync(second.Id)).DueUtc;

            await RunCycleAsync();

            Assert.Contains(TakeSearches(), search => search.IndexerId == unreached && search.Query == second.Query);
            Assert.Equal(nameof(PairScheduleResult.NoAcceptableResults), (await Database.ReadPairAsync(second.Id, unreached))!.LastOutcome);
            Assert.NotEqual(secondDueUtc, (await Database.ReadBookAsync(second.Id)).DueUtc);
        }

        [Fact]
        public async Task OversizedPair_IsFailedWithAWarning_AndBacksOff()
        {
            await ConfigureAsync(maxRequests: Allowance);
            var indexer = await AddIndexerAsync("Enriching");
            var book = (await AddBooksAsync(1, StartUtc.AddMinutes(-5))).Single();
            // Enrichment configured for 400 detail requests: no allowance can hold this pair.
            Indexers.Script = (_, _) => Returns(RejectedSource, requests: 401);

            await RunCycleAsync();

            var pair = await Database.ReadPairAsync(book.Id, indexer);
            Assert.Equal((nameof(PairScheduleResult.Failed), 1), (pair!.LastOutcome, pair.FailureCount));
            Assert.Single(Log.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Enriching"));
            Server.Reset();

            Clock.Advance(TimeSpan.FromMinutes(30));
            await RunCycleAsync();

            Assert.Equal(0, Server.ServedFor(indexer));
        }

        [Fact]
        public async Task TwoLargePairsOfOneBook_BothCompleteWithinTwoCycles()
        {
            await ConfigureAsync(maxRequests: Allowance);
            var first = await AddIndexerAsync("First");
            var second = await AddIndexerAsync("Second");
            var book = (await AddBooksAsync(1, StartUtc.AddMinutes(-5))).Single();
            Indexers.Script = (_, _) => Returns(RejectedSource, requests: 150);

            await RunCycleAsync();
            Clock.Advance(TimeSpan.FromMinutes(5));
            await RunCycleAsync();

            foreach (var indexer in new[] { first, second })
            {
                var pair = await Database.ReadPairAsync(book.Id, indexer);
                Assert.Equal((nameof(PairScheduleResult.NoAcceptableResults), 0), (pair!.LastOutcome, pair.FailureCount));
                Assert.NotNull(pair.LastCompletedUtc);
            }
        }

        // The first book uses all but one request; the second book's full query takes the last and
        // comes back empty, so its title-only fallback is the next request.
        private async Task<(int Indexer, SeededBook Second)> ArrangeFallbackAfterTheLastRequestAsync(int maxRequests)
        {
            await ConfigureAsync(maxRequests: maxRequests);
            var indexer = await AddIndexerAsync("Only");
            var first = (await AddBooksAsync(1, StartUtc.AddHours(-2))).Single();
            var second = (await AddBooksAsync(1, StartUtc.AddHours(-1))).Single();
            Assert.NotEqual(second.Query, second.FallbackQuery);
            Indexers.Script = (_, query) => query == first.Query ? Returns(RejectedSource, requests: Allowance - 1) : IndexerScript.Nothing;
            return (indexer, second);
        }

        // The first book uses 240 requests across both indexers; the second book's query to the
        // first indexer takes the last 10, so the second indexer is next.
        private async Task<(int Unreached, SeededBook Second)> ArrangeSecondIndexerAfterTheLastRequestAsync(int maxRequests)
        {
            await ConfigureAsync(maxRequests: maxRequests);
            await AddIndexerAsync("A");
            var unreached = await AddIndexerAsync("B");
            var first = (await AddBooksAsync(1, StartUtc.AddHours(-2))).Single();
            var second = (await AddBooksAsync(1, StartUtc.AddHours(-1))).Single();
            Indexers.Script = (_, query) => Returns(RejectedSource, requests: query == first.Query ? 120 : 10);
            return (unreached, second);
        }
    }
}
