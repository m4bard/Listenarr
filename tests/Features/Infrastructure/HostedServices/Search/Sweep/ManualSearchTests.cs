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
using Listenarr.Tests.Builders;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search.Sweep
{
    // The manual search (SearchAndDownloadAsync) over the sweep's harness: real DownloadService, real
    // schedule store, scripted indexers behind the scripted gate. It ignores due times, goes through
    // the gate, and writes only Satisfied pair outcomes: its query is narrower than the sweep's (no
    // series, no fallback), so finding nothing must not push back the sweep's retries.
    [Trait("Name", "ManualSearchTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class ManualSearchTests : AutomaticSearchSweepTestBase
    {
        private static readonly DateTime NotDueForDaysUtc = StartUtc.AddDays(10);

        [Theory]
        [InlineData(SearchOrigin.Interactive)]
        [InlineData(SearchOrigin.Bulk)]
        public async Task ManualSearch_BypassesDueTime_NotTheGate(SearchOrigin origin)
        {
            await ConfigureManualAsync();
            var indexer = await AddIndexerAsync("Only");
            var book = (await AddBooksAsync(1, NotDueForDaysUtc)).Single();
            var untilUtc = StartUtc.AddMinutes(45);
            Gate.DeferUntil[indexer] = untilUtc;

            var result = await ManualSearchAsync(book.Id, origin);

            // The book is not due for ten days, yet the indexer was asked; the gate saw the request
            // and held it back, so nothing reached the server, and the result says when and why.
            Assert.Equal(new[] { indexer }, Gate.Sends.Select(send => send.IndexerId));
            Assert.Equal(0, Server.Total);
            Assert.False(result.Success);
            Assert.NotNull(result.Deferral);
            Assert.Equal(untilUtc, result.Deferral.Value.UntilUtc);
            Assert.Equal(RequestDeferralReason.CycleRequestBound, result.Deferral.Value.Reason);
            Assert.Contains("deferred", result.Message);
            Assert.NotEqual("No search results found", result.Message);
        }

        [Fact]
        public async Task ManualSearch_WithAnOpenGate_SearchesAndGrabsABookThatIsNotDue()
        {
            await ConfigureManualAsync();
            var indexer = await AddIndexerAsync("Only");
            var book = (await AddBooksAsync(1, NotDueForDaysUtc)).Single();
            Indexers.Script = (_, _) => Release(AcceptableSource);

            var result = await ManualSearchAsync(book.Id);

            Assert.True(result.Success, result.Message);
            Assert.Null(result.Deferral);
            Assert.Equal(1, Server.ServedFor(indexer));
        }

        [Fact]
        public async Task ManualSearch_OfACutoffMetBook_IsScoredAndGrabbedAsOnCanary()
        {
            await ConfigureManualAsync();
            await AddIndexerAsync("Only");
            var book = (await AddBooksAsync(1, StartUtc.AddHours(-1))).Single();
            await MakeCutoffMetAsync(book.Id);
            Indexers.Script = (_, _) => Release(AcceptableSource);

            // Control: the sweep does skip this book, so the manual search below is what searches it.
            await RunCycleAsync();
            Assert.Empty(TakeSearches());

            var result = await ManualSearchAsync(book.Id);

            Assert.True(result.Success, result.Message);
            Assert.Equal(AcceptableSource, result.SearchResult!.Source);
            Assert.False(string.IsNullOrEmpty(result.DownloadId));
            Assert.Single(TakeSearches());
        }

        [Fact]
        public async Task ManualSearch_RecordsCompletedPairsThroughTheStore_AndNothingOnTheBook()
        {
            await ConfigureManualAsync();
            var found = await AddIndexerAsync("Found");
            var empty = await AddIndexerAsync("Empty");
            var book = (await AddBooksAsync(1, NotDueForDaysUtc)).Single();
            Indexers.Script = (indexerId, _) => indexerId == found ? Release(AcceptableSource) : IndexerScript.Nothing;

            var result = await ManualSearchAsync(book.Id);

            Assert.True(result.Success, result.Message);
            // A grab satisfies the book on every indexer that answered.
            foreach (var indexerId in new[] { found, empty })
            {
                var pair = await Database.ReadPairAsync(book.Id, indexerId);
                Assert.NotNull(pair);
                Assert.Equal(nameof(PairScheduleResult.Satisfied), pair.LastOutcome);
                Assert.Equal(StartUtc.AddHours(24), pair.NextAttemptUtc);
            }

            var (dueUtc, failureCount, lastSearchUtc, _) = await Database.ReadBookAsync(book.Id);
            Assert.Equal(NotDueForDaysUtc, dueUtc);
            Assert.Equal(0, failureCount);
            Assert.Null(lastSearchUtc);
        }

        [Theory]
        [InlineData(false, "No search results found")]
        [InlineData(true, "No acceptable search results found")]
        public async Task ManualSearch_FindingNothing_LeavesThePairUntouched(bool returnsRejected, string expectedMessage)
        {
            await ConfigureManualAsync();
            var indexer = await AddIndexerAsync("Only");
            var book = (await AddBooksAsync(1, NotDueForDaysUtc)).Single();
            var sweepRetryUtc = StartUtc.AddHours(6);
            await Database.SeedPairAsync(new AudiobookIndexerSearchState
            {
                AudiobookId = book.Id,
                IndexerId = indexer,
                NextAttemptUtc = sweepRetryUtc,
                NoResultCount = 1,
                LastOutcome = nameof(PairScheduleResult.NoAcceptableResults),
                LastCompletedUtc = StartUtc.AddHours(-18)
            });
            Indexers.Script = (_, _) => returnsRejected ? Release(RejectedSource) : IndexerScript.Nothing;

            var result = await ManualSearchAsync(book.Id);

            Assert.False(result.Success);
            Assert.Equal(expectedMessage, result.Message);
            Assert.Equal(1, Server.ServedFor(indexer));
            var pair = await Database.ReadPairAsync(book.Id, indexer);
            Assert.Equal(
                (sweepRetryUtc, 1, 0, nameof(PairScheduleResult.NoAcceptableResults), (DateTime?)StartUtc.AddHours(-18)),
                (pair!.NextAttemptUtc, pair.NoResultCount, pair.FailureCount, pair.LastOutcome, pair.LastCompletedUtc));
        }

        [Fact]
        public async Task ManualSearch_FailedIndexer_RecordsNothing()
        {
            await ConfigureManualAsync();
            var answered = await AddIndexerAsync("Answered");
            var failing = await AddIndexerAsync("Failing");
            var book = (await AddBooksAsync(1, NotDueForDaysUtc)).Single();
            Server.StatusFor = indexerId => indexerId == failing ? HttpStatusCode.InternalServerError : HttpStatusCode.OK;
            Indexers.Script = (_, _) => Release(AcceptableSource);

            var result = await ManualSearchAsync(book.Id);

            Assert.True(result.Success, result.Message);
            Assert.Equal(nameof(PairScheduleResult.Satisfied), (await Database.ReadPairAsync(book.Id, answered))!.LastOutcome);
            Assert.Null(await Database.ReadPairAsync(book.Id, failing));
            var (dueUtc, failureCount, _, _) = await Database.ReadBookAsync(book.Id);
            Assert.Equal(NotDueForDaysUtc, dueUtc);
            Assert.Equal(0, failureCount);
        }

        [Fact]
        public async Task ManualSearch_DeferredIndexer_RecordsNothingForIt()
        {
            await ConfigureManualAsync();
            var answered = await AddIndexerAsync("Answered");
            var deferred = await AddIndexerAsync("Deferred");
            var book = (await AddBooksAsync(1, NotDueForDaysUtc)).Single();
            Gate.DeferUntil[deferred] = StartUtc.AddHours(1);
            Indexers.Script = (_, _) => Release(AcceptableSource);

            var result = await ManualSearchAsync(book.Id);

            Assert.True(result.Success, result.Message);
            Assert.Equal(nameof(PairScheduleResult.Satisfied), (await Database.ReadPairAsync(book.Id, answered))!.LastOutcome);
            Assert.Null(await Database.ReadPairAsync(book.Id, deferred));
        }

        [Fact]
        public async Task ManualSearch_AllIndexersFailed_StillSaysNoResultsFound()
        {
            await ConfigureManualAsync();
            await AddIndexerAsync("Only");
            var book = (await AddBooksAsync(1, NotDueForDaysUtc)).Single();
            Server.StatusFor = _ => HttpStatusCode.InternalServerError;

            var result = await ManualSearchAsync(book.Id);

            Assert.False(result.Success);
            Assert.Null(result.Deferral);
            Assert.Equal("No search results found", result.Message);
        }

        private async Task ConfigureManualAsync()
        {
            await ConfigureAsync();
            await using var context = Database.CreateContext();
            context.DownloadClientConfigurations.Add(new DownloadClientConfigurationBuilder()
                .WithHost("localhost")
                .WithPort(8080)
                .WithUsername("admin")
                .WithPassword("admin")
                .WithType("qbittorrent")
                .Build());
            await context.SaveChangesAsync();
        }

        private async Task<SearchAndDownloadResult> ManualSearchAsync(int audiobookId, SearchOrigin origin = SearchOrigin.Interactive)
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<DownloadService>().SearchAndDownloadAsync(audiobookId, origin);
        }

        private static IndexerScript Release(string source) =>
            new(1, new[]
            {
                new IndexerSearchResult
                {
                    Title = "release",
                    Source = source,
                    MagnetLink = "magnet:?xt=urn:btih:ABCDEF1234567890ABCDEF1234567890ABCDEF12&dn=release",
                    Size = 800_000_000,
                    Seeders = 20
                }
            });
    }
}
