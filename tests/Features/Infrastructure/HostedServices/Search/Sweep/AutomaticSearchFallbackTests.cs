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
    // The title-only fallback is decided per indexer: only an indexer whose own full query came
    // back empty is asked again, and the gate sees that request as a fallback query.
    [Trait("Name", "AutomaticSearchFallbackTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class AutomaticSearchFallbackTests : AutomaticSearchSweepTestBase
    {
        [Fact]
        public async Task Fallback_PerIndexer()
        {
            await ConfigureAsync();
            var withResults = await AddIndexerAsync("WithResults");
            var empty = await AddIndexerAsync("Empty");
            var book = (await AddBooksAsync(1, StartUtc.AddMinutes(-5))).Single();
            Assert.NotEqual(book.Query, book.FallbackQuery);
            Indexers.Script = (indexerId, query) =>
                indexerId == withResults && query == book.Query ? Returns(RejectedSource)
                : query == book.FallbackQuery ? new IndexerScript(1, new[] { new IndexerSearchResult { Title = book.ReleaseName, Source = RejectedSource } })
                : IndexerScript.Nothing;

            await RunCycleAsync();

            Assert.Equal(
                new[] { (withResults, book.Query), (empty, book.Query), (empty, book.FallbackQuery) },
                TakeSearches().Select(search => (search.IndexerId, search.Query)));
            Assert.Equal(
                new[] { (withResults, IndexerRequestKind.Search), (empty, IndexerRequestKind.Search), (empty, IndexerRequestKind.FallbackQuery) },
                Gate.Sends.ToArray());
        }
    }
}
