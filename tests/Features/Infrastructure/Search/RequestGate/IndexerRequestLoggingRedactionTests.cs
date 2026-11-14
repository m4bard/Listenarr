/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Infrastructure.Search.RequestGate;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Features.Infrastructure.Persistence;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestGate.IndexerRequestGateHarness;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestGate;

// What the gate, the state cache and a real provider log while an indexer is rate limited, then
// deferred, then failing: neither the indexer's key, its URL query nor its settings may appear.
[Trait("Name", "IndexerRequestLoggingRedactionTests")]
[Trait("Category", "Infrastructure")]
public sealed class IndexerRequestLoggingRedactionTests : BaseTests
{
    private const string UrlKey = "url-key-0f3a9c";
    private const string ApiKey = "api-key-71be42";
    private const string SessionValue = "session-value-5d8e10";

    [Fact]
    public async Task RateLimitedDeferredAndFailingSearches_LogNoUrlQueryKeyOrSetting()
    {
        // Given
        await using var database = await IndexerRequestStateTestDatabase.CreateAsync(indexerCount: 2);
        var captured = new ConcurrentQueue<string>();
        var clock = new ManualClock(Start);
        var cache = new IndexerRequestStateCache(
            new EfIndexerRequestStateStore(database.Factory),
            new CapturingLogger<IndexerRequestStateCache>(captured));
        var gate = new IndexerRequestGate(
            cache,
            [],
            new IndexerConcurrencyLimiter(2, 8),
            new IndexerPacingSchedule(Interval, clock, clock.AdvanceDelayAsync),
            clock,
            new CapturingLogger<IndexerRequestGate>(captured));
        using var wire = new HttpClient(new DelegatingHandlerMock((request, _) => Task.FromResult(
            request.RequestUri!.Host.StartsWith("limited", StringComparison.Ordinal)
                ? RateLimited(new RetryConditionHeaderValue(TimeSpan.FromMinutes(5)))
                : Status(HttpStatusCode.InternalServerError))));
        var provider = new TorznabNewznabSearchProvider(wire, gate, new CapturingLogger<TorznabNewznabSearchProvider>(captured));
        var limited = Secret(database.IndexerIds[0], "limited");
        var failing = Secret(database.IndexerIds[1], "failing");
        var context = new IndexerSearchContext(SearchOrigin.Sweep, null, IndexerRequestKind.Search);

        // When
        var rateLimited = await provider.SearchAsync(limited, "query", null, null, context, CancellationToken.None);
        var deferred = await provider.SearchAsync(limited, "query", null, null, context, CancellationToken.None);
        var failed = await provider.SearchAsync(failing, "query", null, null, context, CancellationToken.None);

        // Then: the paths under test ran, and logged.
        Assert.Equal(
            new[] { IndexerSearchStatus.Deferred, IndexerSearchStatus.Deferred, IndexerSearchStatus.Failed },
            new[] { rateLimited.Status, deferred.Status, failed.Status });
        Assert.Contains(captured, line => line.Contains("rate limited", StringComparison.Ordinal));
        Assert.Contains(captured, line => line.Contains("returned status", StringComparison.Ordinal));
        foreach (var secret in new[] { UrlKey, ApiKey, SessionValue })
        {
            Assert.DoesNotContain(captured, line => line.Contains(secret, StringComparison.Ordinal));
        }
    }

    private static Indexer Secret(int id, string hostPrefix) => new IndexerBuilder()
        .WithId(id)
        .WithName($"Indexer {id}")
        .WithImplementation("Torznab")
        .WithUrl($"https://{hostPrefix}.example.invalid/api?apikey={UrlKey}")
        .WithApiKey(ApiKey)
        .WithSetting("mam_id", SessionValue)
        .Build();

    // Keeps each entry's rendered message and its exception, so an exception text is searched too.
    // Debug entries are not kept: they are off in production, and the provider's own pre-existing
    // debug line for the request URL redacts only the API key.
    private sealed class CapturingLogger<T>(ConcurrentQueue<string> sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                sink.Enqueue($"{formatter(state, exception)} {exception}");
            }
        }
    }
}
