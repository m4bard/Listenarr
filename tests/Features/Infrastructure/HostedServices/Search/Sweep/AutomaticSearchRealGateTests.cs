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
using System.Net.Http.Headers;
using Listenarr.Application.Search.Scheduling;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Infrastructure.Search.RequestGate;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search.Sweep
{
    // The sweep over the real request gate and a real Torznab provider: a 429 with Retry-After is an
    // indexer-wide cooldown, so the pair is deferred (not failed, not "found nothing") and no later
    // book in the cycle, or after a restart, reaches the indexer before the cooldown ends.
    [Trait("Name", "AutomaticSearchRealGateTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class AutomaticSearchRealGateTests : AutomaticSearchSweepTestBase
    {
        private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(120);
        private readonly IndexerWire _wire = new();

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            Clock.CompleteTimersAtOnce = true;
            UseRealGate();
        }

        [Fact]
        public async Task RateLimitedIndexer_DefersThePair_AndIsNotContactedAgainBeforeRetryAfter()
        {
            await ConfigureAsync();
            var indexer = await AddTorznabIndexerAsync();
            var books = await AddBooksAsync(2, StartUtc.AddMinutes(-5));

            await RunCycleAsync();

            Assert.Equal(1, _wire.Hits);
            foreach (var book in books)
            {
                await AssertDeferredUntilRetryAfterAsync(book.Id, indexer);
            }
        }

        [Fact]
        public async Task RateLimitedIndexer_AfterRestart_DefersWithoutContactingTheIndexer()
        {
            await ConfigureAsync();
            var indexer = await AddTorznabIndexerAsync();
            await AddBooksAsync(1, StartUtc.AddMinutes(-5));
            await RunCycleAsync();
            Assert.Equal(1, _wire.Hits);

            UseRealGate();
            var next = (await AddBooksAsync(1, StartUtc.AddMinutes(-5))).Single();
            await RunCycleAsync();

            Assert.Equal(1, _wire.Hits);
            await AssertDeferredUntilRetryAfterAsync(next.Id, indexer);
        }

        private async Task AssertDeferredUntilRetryAfterAsync(int bookId, int indexerId)
        {
            var pair = await Database.ReadPairAsync(bookId, indexerId);
            Assert.NotNull(pair);
            Assert.Equal(
                (nameof(PairScheduleResult.Deferred), 0, 0, StartUtc + RetryAfter),
                (pair.LastOutcome, pair.NoResultCount, pair.FailureCount, pair.NextAttemptUtc));
        }

        // A new cache and gate over the same database file, as after a process restart.
        private void UseRealGate()
        {
            var cache = new IndexerRequestStateCache(
                new EfIndexerRequestStateStore(DatabaseContexts),
                NullLogger<IndexerRequestStateCache>.Instance);
            var gate = new IndexerRequestGate(cache, [], Clock, NullLogger<IndexerRequestGate>.Instance);
            GateOverride = gate;
            AdditionalProviders.Clear();
            AdditionalProviders.Add(new TorznabNewznabSearchProvider(
                new HttpClient(new DelegatingHandlerMock(_wire.HandleAsync)),
                gate,
                NullLogger<TorznabNewznabSearchProvider>.Instance));
            BuildHost();
        }

        private async Task<int> AddTorznabIndexerAsync()
        {
            await using var context = Database.CreateContext();
            var indexer = new IndexerBuilder()
                .WithName("Torznab")
                .WithImplementation("Torznab")
                .WithUrl("https://althub.example")
                .WithEnabled()
                .Build();
            indexer.EnableAutomaticSearch = true;
            context.Indexers.Add(indexer);
            await context.SaveChangesAsync();
            return indexer.Id;
        }

        // Answers every request with 429 and Retry-After, and counts what reaches it.
        private sealed class IndexerWire
        {
            private int _hits;

            public int Hits => Volatile.Read(ref _hits);

            public Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _hits);
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(RetryAfter);
                return Task.FromResult(response);
            }
        }
    }
}
