/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Tests.Builders;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search.Sweep
{
    // The per-cycle book cap and the order books are taken in.
    [Trait("Name", "AutomaticSearchCycleLimitTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class AutomaticSearchCycleLimitTests : AutomaticSearchSweepTestBase
    {
        [Fact]
        public async Task PerCycleCap_CountsOnlyBooksThatWereSearched()
        {
            await ConfigureAsync(maxBooks: 2);
            await AddIndexerAsync("Only");
            var dueUtc = StartUtc.AddMinutes(-5);
            var books = await AddBooksAsync(5, dueUtc);
            // The first book already has a download in flight, so it is skipped before any request
            // and must not use up one of the two slots. Every book that does reach the indexer
            // search counts, whatever its outcome.
            await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobookId(books[0].Id)
                .WithStatus(DownloadStatus.Downloading)
                .Build());

            await RunCycleAsync();

            Assert.Equal(
                new[] { books[1].Query, books[2].Query },
                TakeSearches().Where(search => search.Kind == IndexerRequestKind.Search).Select(search => search.Query));
            var skipped = await Database.ReadBookAsync(books[0].Id);
            Assert.Null(skipped.LastSearchUtc);
            Assert.Equal(StartUtc.AddHours(24), skipped.DueUtc);
            Assert.Equal(StartUtc, (await Database.ReadBookAsync(books[1].Id)).LastSearchUtc);
            Assert.Equal(StartUtc, (await Database.ReadBookAsync(books[2].Id)).LastSearchUtc);
            foreach (var untouched in books.Skip(3))
            {
                Assert.Equal((dueUtc, (DateTime?)null), ((await Database.ReadBookAsync(untouched.Id)).DueUtc, (await Database.ReadBookAsync(untouched.Id)).LastSearchUtc));
            }
        }

        [Fact]
        public async Task RetryDue_IsServedWhileArrivalsContinue()
        {
            await ConfigureAsync(maxBooks: 10);
            var indexer = await AddIndexerAsync("Only");
            // A no-result retry due at T. No other book is due before T; every arrival is due at its
            // own arrival time, which is later.
            var retryAtUtc = StartUtc;
            var retry = (await AddBooksAsync(1, retryAtUtc)).Single();
            await Database.SeedPairAsync(new AudiobookIndexerSearchState
            {
                AudiobookId = retry.Id,
                IndexerId = indexer,
                NextAttemptUtc = retryAtUtc,
                NoResultCount = 1,
                LastOutcome = "NoAcceptableResults",
                LastCompletedUtc = retryAtUtc.AddHours(-24)
            });
            // Set-based, as the store writes it: a tracked save of the last search time is ignored.
            await using (var context = Database.CreateContext())
            {
                await context.Audiobooks
                    .Where(book => book.Id == retry.Id)
                    .ExecuteUpdateAsync(updates => updates.SetProperty(book => book.LastSearchTime, retryAtUtc.AddHours(-24)));
            }
            Assert.Equal(retryAtUtc.AddHours(-24), (await Database.ReadBookAsync(retry.Id)).LastSearchUtc);

            int? retryCycle = null;
            for (var cycle = 1; cycle <= 3 && retryCycle == null; cycle++)
            {
                Clock.Advance(TimeSpan.FromMinutes(1));
                await AddBooksAsync(30, Clock.UtcNow);
                await RunCycleAsync();
                retryCycle = TakeSearches().Any(search => search.Query == retry.Query) ? cycle : null;
            }

            Assert.Equal(1, retryCycle);
        }
    }
}
