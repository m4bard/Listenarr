/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Builders;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search.Sweep
{
    // A new or re-enabled indexer is searched in the next cycle, not after the books' backoff:
    // the indexer write pulls the books' due times forward, and the sweep's own selection then
    // queries only the indexers that are due for each book.
    [Trait("Name", "AutomaticSearchIndexerSetTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class AutomaticSearchIndexerSetTests : AutomaticSearchSweepTestBase
    {
        [Fact]
        public async Task IndexerSetGrowth_PullsDueForward()
        {
            await ConfigureAsync();
            var first = await AddIndexerAsync("A");
            await AddIndexerAsync("B");
            var book = (await AddBooksAsync(1, StartUtc.AddMinutes(-5))).Single();
            await RunCycleAsync();
            TakeSearches();

            // Both indexers answered with nothing, so the book waits on their no-result backoff.
            Clock.Advance(TimeSpan.FromHours(1));
            await RunCycleAsync();
            Assert.Empty(TakeSearches());

            // A settings-only save of an eligible indexer, as the cookie refresh does, changes nothing.
            await using (var context = Database.CreateContext())
            {
                var repository = new EfIndexerRepository(context, Clock);
                var indexer = (await repository.GetByIdAsync(first))!;
                indexer.AdditionalSettings = "{\"mam_id\":\"refreshed\"}";
                await repository.UpdateAsync(indexer);
            }

            await RunCycleAsync();
            Assert.Empty(TakeSearches());

            int added;
            await using (var context = Database.CreateContext())
            {
                added = (await new EfIndexerRepository(context, Clock).AddAsync(NewIndexer("C"))).Id;
            }

            await RunCycleAsync();
            Assert.Equal(new[] { (added, book.Query) }, TakeSearches().Select(search => (search.IndexerId, search.Query)));
        }

        [Fact]
        public async Task EnablingAnIndexer_PullsDueForward()
        {
            await ConfigureAsync();
            await AddIndexerAsync("A");
            await AddIndexerAsync("B");
            var disabled = await AddIndexerAsync("C");
            await using (var context = Database.CreateContext())
            {
                await context.Indexers.Where(indexer => indexer.Id == disabled)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(indexer => indexer.IsEnabled, false));
            }

            var book = (await AddBooksAsync(1, StartUtc.AddMinutes(-5))).Single();
            await RunCycleAsync();
            Assert.DoesNotContain(TakeSearches(), search => search.IndexerId == disabled);
            Clock.Advance(TimeSpan.FromHours(1));

            // The toggle endpoint's path: load tracked, flip the flag, save.
            await using (var context = Database.CreateContext())
            {
                var repository = new EfIndexerRepository(context, Clock);
                var indexer = (await repository.GetByIdAsync(disabled))!;
                indexer.IsEnabled = true;
                await repository.UpdateAsync(indexer);
            }

            await RunCycleAsync();
            Assert.Equal(new[] { (disabled, book.Query) }, TakeSearches().Select(search => (search.IndexerId, search.Query)));
        }

        // The pull also reaches a book waiting on its own failure ladder: it gets one early retry
        // (accepted, and stated in the PR text). After a BookFailed no pair row exists, so every
        // indexer is due; after a GrabNotCompleted the old pair waits, so only the new one is.
        [Theory]
        [InlineData(ScoringThrowsSource, true)]
        [InlineData(StartThrowsSource, false)]
        public async Task BookOnTheFailureLadder_IsPulledForward(string source, bool oldIndexerSearchedAgain)
        {
            await ConfigureAsync();
            var old = await AddIndexerAsync("A");
            var book = (await AddBooksAsync(1, StartUtc.AddMinutes(-5))).Single();
            Indexers.Script = (_, _) => Returns(source);
            await RunCycleAsync();
            TakeSearches();
            var (dueUtc, failureCount, _, _) = await Database.ReadBookAsync(book.Id);
            Assert.Equal((StartUtc.AddMinutes(15), 1), (dueUtc, failureCount));

            Clock.Advance(TimeSpan.FromMinutes(1));
            await RunCycleAsync();
            Assert.Empty(TakeSearches());

            int added;
            await using (var context = Database.CreateContext())
            {
                added = (await new EfIndexerRepository(context, Clock).AddAsync(NewIndexer("C"))).Id;
            }

            await RunCycleAsync();
            var expected = oldIndexerSearchedAgain ? new[] { old, added } : new[] { added };
            Assert.Equal(
                expected.OrderBy(id => id),
                TakeSearches().Where(search => search.Query == book.Query).Select(search => search.IndexerId).Distinct().OrderBy(id => id));
        }

        private static Indexer NewIndexer(string name)
        {
            var indexer = new IndexerBuilder()
                .WithName(name)
                .WithImplementation(ScriptedSearchProvider.Implementation)
                .WithUrl("https://indexer.invalid")
                .WithEnabled()
                .Build();
            indexer.EnableAutomaticSearch = true;
            return indexer;
        }
    }
}
