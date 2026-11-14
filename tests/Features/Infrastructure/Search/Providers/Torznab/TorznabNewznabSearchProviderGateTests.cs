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
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Search.Providers.Torznab;

[Trait("Name", "TorznabNewznabSearchProviderGateTests")]
[Trait("Category", "TorznabNewznabSearchProvider")]
public sealed class TorznabNewznabSearchProviderGateTests : BaseTests
{
    [Fact]
    public async Task Provider_UsesQueryKindForPrimarySend()
    {
        // Given: a fallback-query search from the sweep, with a cycle allowance
        var gate = new RecordingGate();
        var requests = new RequestCount();
        var provider = CreateProvider(gate, requests);
        var allowance = new CycleRequestAllowance(5);
        var context = new IndexerSearchContext(SearchOrigin.Sweep, allowance, IndexerRequestKind.FallbackQuery);

        // When
        var outcome = await provider.SearchAsync(CreateIndexer(), "query", null, null, context, CancellationToken.None);

        // Then: the one wire request went through the gate with the context's kind, origin and allowance
        var sent = Assert.Single(gate.Contexts);
        Assert.Equal(new IndexerRequestContext(7, SearchOrigin.Sweep, IndexerRequestKind.FallbackQuery, allowance), sent);
        Assert.Equal(1, requests.Count);
        Assert.Equal(IndexerSearchStatus.Completed, outcome.Status);
        Assert.Equal(1, outcome.RequestsSent);
    }

    [Fact]
    public async Task Provider_DefaultContext_SendsPrimaryAsSearch()
    {
        var gate = new RecordingGate();
        var provider = CreateProvider(gate, new RequestCount());

        await provider.SearchAsync(
            CreateIndexer(), "query", null, null, new IndexerSearchContext(SearchOrigin.Interactive, CycleAllowance: null), CancellationToken.None);

        Assert.Equal(IndexerRequestKind.Search, Assert.Single(gate.Contexts).Kind);
    }

    private static TorznabNewznabSearchProvider CreateProvider(IIndexerRequestGate gate, RequestCount requests)
    {
        var handler = new DelegatingHandlerMock((_, _) =>
        {
            requests.Count++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<rss><channel></channel></rss>")
            });
        });

        return new TorznabNewznabSearchProvider(
            new HttpClient(handler),
            gate,
            NullLogger<TorznabNewznabSearchProvider>.Instance);
    }

    private static Indexer CreateIndexer()
    {
        return new IndexerBuilder()
            .WithId(7)
            .WithName("Torznab")
            .WithImplementation("Torznab")
            .WithUrl("https://indexer.example")
            .Build();
    }

    private sealed class RequestCount
    {
        public int Count { get; set; }
    }

    // Records every request context and then dispatches, as the pass-through gate does.
    private sealed class RecordingGate : IIndexerRequestGate
    {
        public List<IndexerRequestContext> Contexts { get; } = new();

        public async Task<IndexerRequestResult> SendAsync(
            IndexerRequestContext context,
            Func<CancellationToken, Task<HttpResponseMessage>> dispatch,
            CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            return new IndexerRequestResult.Sent(await dispatch(cancellationToken));
        }

        public Task<RequestDeferral?> GetDeferralAsync(int indexerId, SearchOrigin origin, CancellationToken cancellationToken)
        {
            return Task.FromResult<RequestDeferral?>(null);
        }
    }
}
