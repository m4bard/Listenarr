/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search.Sweep
{
    // The per-book delay paces indexer requests. A book skipped before its search (quality cutoff
    // met, active download) sends none, so it must not wait the delay, while consecutive real
    // searches still do. The delay goes through the recording clock, so no test sleeps.
    [Trait("Name", "AutomaticSearchPacingTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class AutomaticSearchPacingTests : AutomaticSearchSweepTestBase
    {
        private const int BookDelaySeconds = 180;

        private static readonly TimeSpan BookDelay = TimeSpan.FromSeconds(BookDelaySeconds);

        [Fact]
        public async Task CutoffMetBooksAndOneWantedBook_SearchesTheWantedBookWithoutWaiting()
        {
            await ConfigurePacedAsync();
            await AddIndexerAsync("Only");
            var books = await AddBooksAsync(4, StartUtc.AddMinutes(-5));
            foreach (var skipped in books.Take(3))
            {
                await MakeCutoffMetAsync(skipped.Id);
            }

            await RunCycleAsync();

            Assert.Equal(new[] { books[3].Query }, TakeFullQueries());
            Assert.Empty(Clock.Delays);
        }

        [Fact]
        public async Task TwoWantedBooks_StillWaitTheBookDelayBetweenSearches()
        {
            await ConfigurePacedAsync();
            await AddIndexerAsync("Only");
            var books = await AddBooksAsync(2, StartUtc.AddMinutes(-5));

            await RunCycleAsync();

            Assert.Equal(books.Select(book => book.Query), TakeFullQueries());
            Assert.Equal(new[] { BookDelay }, Clock.Delays.ToArray());
        }

        [Fact]
        public async Task CutoffMetBookAmongWantedBooks_WaitsOncePerSearchNotOncePerBook()
        {
            await ConfigurePacedAsync();
            await AddIndexerAsync("Only");
            var books = await AddBooksAsync(3, StartUtc.AddMinutes(-5));
            await MakeCutoffMetAsync(books[1].Id);

            await RunCycleAsync();

            Assert.Equal(new[] { books[0].Query, books[2].Query }, TakeFullQueries());
            Assert.Equal(new[] { BookDelay }, Clock.Delays.ToArray());
        }

        [Fact]
        public async Task ActiveDownloadAndOneWantedBook_SearchesTheWantedBookWithoutWaiting()
        {
            await ConfigurePacedAsync();
            await AddIndexerAsync("Only");
            var books = await AddBooksAsync(2, StartUtc.AddMinutes(-5));
            await AddActiveDownloadAsync(books[0].Id);

            await RunCycleAsync();

            Assert.Equal(new[] { books[1].Query }, TakeFullQueries());
            Assert.Empty(Clock.Delays);
        }

        [Fact]
        public async Task DownloadStartsDuringTheWait_DoesNotGrabTheBookAgain()
        {
            await ConfigurePacedAsync();
            await AddIndexerAsync("Only");
            var books = await AddBooksAsync(2, StartUtc.AddMinutes(-5));
            Indexers.Script = (_, _) => Returns(AcceptableSource);
            // While the cycle waits before the second search, something else (a manual grab) starts
            // a download for the book that has not been searched yet.
            Clock.OnDelay = () => AddActiveDownloadAsync(books[1].Id).GetAwaiter().GetResult();

            await RunCycleAsync();

            Assert.Single(Clock.Delays);
            Assert.Equal(new[] { books[0].Query }, TakeFullQueries());
            Downloads.Verify(d => d.StartDownloadAsync(It.IsAny<SearchResult>(), It.IsAny<string>(), It.IsAny<int?>()), Times.Once);
            var skipped = await Database.ReadBookAsync(books[1].Id);
            Assert.Equal((StartUtc.AddHours(24), (DateTime?)null), (skipped.DueUtc, skipped.LastSearchUtc));
        }

        // The full queries only: a book with no results is also asked the title-only fallback.
        private List<string> TakeFullQueries() =>
            TakeSearches().Where(search => search.Kind == IndexerRequestKind.Search).Select(search => search.Query).ToList();

        private async Task ConfigurePacedAsync()
        {
            await ConfigureAsync(bookDelaySeconds: BookDelaySeconds);
            Clock.CompleteTimersAtOnce = true;
        }
    }
}
