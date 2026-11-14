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
using System.Text.Json;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Search.Providers.InternetArchive;

[Trait("Name", "InternetArchiveSearchProviderGateTests")]
[Trait("Category", "InternetArchiveSearchProvider")]
public sealed class InternetArchiveSearchProviderGateTests : BaseTests
{
    private const string AudioMetadata = """
{
  "metadata": { "language": "English" },
  "files": [
    { "name": "alice.m4b", "format": "LibriVox Apple Audiobook", "size": "900" }
  ]
}
""";

    [Fact]
    public async Task Provider_UsesQueryKindForSearchAndEnrichmentForMetadata()
    {
        // Given: a fallback-query search with one item
        var gate = new FakeGate();
        var provider = CreateProvider(gate, new RequestLog(), _ => Ok(AudioMetadata), "alice_book");
        var allowance = new CycleRequestAllowance(10);
        var context = new IndexerSearchContext(SearchOrigin.Sweep, allowance, IndexerRequestKind.FallbackQuery);

        // When
        var outcome = await provider.SearchAsync(CreateIndexer(), "Alice", null, null, context, CancellationToken.None);

        // Then: every request went through the gate with the context's origin and allowance
        Assert.Equal(IndexerSearchStatus.Completed, outcome.Status);
        Assert.Equal(
            new[]
            {
                new IndexerRequestContext(42, SearchOrigin.Sweep, IndexerRequestKind.FallbackQuery, allowance),
                new IndexerRequestContext(42, SearchOrigin.Sweep, IndexerRequestKind.Enrichment, allowance)
            },
            gate.Contexts.Take(2));
        Assert.Equal(gate.Contexts.Count, outcome.RequestsSent);
    }

    [Fact]
    public async Task Provider_DefaultContext_SendsVariantsAsSearch()
    {
        var gate = new FakeGate();
        var provider = CreateProvider(gate, new RequestLog(), _ => Ok(AudioMetadata), "alice_book");

        await provider.SearchAsync(CreateIndexer(), "Alice", null, null, Interactive(), CancellationToken.None);

        Assert.Equal(IndexerRequestKind.Search, gate.Contexts[0].Kind);
    }

    [Fact]
    public async Task Deferral_AtSecondMetadataRequest_IsDeferredWithResultsGathered()
    {
        // Given: two items; the gate defers the third request (the second metadata GET)
        var gate = new FakeGate(deferCall: 3);
        var log = new RequestLog();
        var provider = CreateProvider(gate, log, _ => Ok(AudioMetadata), "alice_book", "second_book");

        // When
        var outcome = await provider.SearchAsync(CreateIndexer(), "Alice", null, null, Interactive(), CancellationToken.None);

        // Then: Deferred, with the first item's results and only the two sent requests
        Assert.Equal(IndexerSearchStatus.Deferred, outcome.Status);
        Assert.NotNull(outcome.Deferral);
        Assert.Single(outcome.Results);
        Assert.Equal(2, outcome.RequestsSent);
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public async Task Deferral_AtFirstRequest_IsDeferredWithoutResults()
    {
        var gate = new FakeGate(deferCall: 1);
        var log = new RequestLog();
        var provider = CreateProvider(gate, log, _ => Ok(AudioMetadata), "alice_book");

        var outcome = await provider.SearchAsync(CreateIndexer(), "Alice", null, null, Interactive(), CancellationToken.None);

        Assert.Equal(IndexerSearchStatus.Deferred, outcome.Status);
        Assert.Empty(outcome.Results);
        Assert.Equal(0, outcome.RequestsSent);
        Assert.Equal(0, log.Count);
    }

    [Fact]
    public async Task EnrichmentFailure_EveryMetadataRequestFails_IsFailedNotCompleted()
    {
        // Given: the search answers, every metadata GET returns 503
        var gate = new FakeGate();
        var provider = CreateProvider(
            gate, new RequestLog(), _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), "alice_book", "second_book");

        // When
        var outcome = await provider.SearchAsync(CreateIndexer(), "Alice", null, null, Interactive(), CancellationToken.None);

        // Then: no candidate results and a failed enrichment is a failure, not an empty success
        Assert.Equal(IndexerSearchStatus.Failed, outcome.Status);
        Assert.Empty(outcome.Results);
        Assert.Equal(3, outcome.RequestsSent);
    }

    [Fact]
    public async Task EnrichmentFailure_MetadataTransportError_IsFailedNotCompleted()
    {
        var gate = new FakeGate();
        var provider = CreateProvider(
            gate, new RequestLog(), _ => throw new HttpRequestException("connection reset"), "alice_book");

        var outcome = await provider.SearchAsync(CreateIndexer(), "Alice", null, null, Interactive(), CancellationToken.None);

        Assert.Equal(IndexerSearchStatus.Failed, outcome.Status);
        Assert.Empty(outcome.Results);
    }

    [Fact]
    public async Task EnrichmentFailure_SomeResultsRemain_IsCompleted()
    {
        // Given: the first item's metadata answers and the second item's returns 503
        var provider = CreateProvider(
            new FakeGate(),
            new RequestLog(),
            uri => uri.AbsolutePath.EndsWith("/alice_book", StringComparison.Ordinal)
                ? Ok(AudioMetadata)
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            "alice_book",
            "second_book");

        var outcome = await provider.SearchAsync(CreateIndexer(), "Alice", null, null, Interactive(), CancellationToken.None);

        Assert.Equal(IndexerSearchStatus.Completed, outcome.Status);
        Assert.Single(outcome.Results);
    }

    private static IndexerSearchContext Interactive() => new(SearchOrigin.Interactive, CycleAllowance: null);

    private static InternetArchiveSearchProvider CreateProvider(
        IIndexerRequestGate gate,
        RequestLog log,
        Func<Uri, HttpResponseMessage> metadataResponse,
        params string[] identifiers)
    {
        var handler = new DelegatingHandlerMock((request, _) =>
        {
            var uri = request.RequestUri ?? new Uri("about:blank");
            log.Count++;
            return Task.FromResult(uri.AbsolutePath.Contains("/advancedsearch.php", StringComparison.OrdinalIgnoreCase)
                ? Ok(CreateSearchResponse(identifiers))
                : metadataResponse(uri));
        });

        var configurationService = new Mock<IConfigurationService>();
        configurationService
            .Setup(service => service.GetApplicationSettingsAsync())
            .ReturnsAsync(new ApplicationSettingsBuilder().WithExtractArchive().Build());

        return new InternetArchiveSearchProvider(
            new HttpClient(handler),
            gate,
            configurationService.Object,
            NullLogger<InternetArchiveSearchProvider>.Instance);
    }

    private static Indexer CreateIndexer()
    {
        return new IndexerBuilder()
            .WithId(42)
            .WithName("Internet Archive")
            .WithType("DirectDownload")
            .WithImplementation("InternetArchive")
            .WithUrl("https://archive.org")
            .Build();
    }

    private static HttpResponseMessage Ok(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content)
    };

    private static string CreateSearchResponse(string[] identifiers)
    {
        var docs = string.Join(
            ",",
            identifiers.Select(identifier =>
                $$"""{"identifier": {{JsonSerializer.Serialize(identifier)}}, "title": "Alice's Adventures in Wonderland", "creator": "Lewis Carroll", "date": "2010-03-01"}"""));
        return "{\"response\": {\"docs\": [" + docs + "]}}";
    }

    private sealed class RequestLog
    {
        public int Count { get; set; }
    }

    // Records every request context, defers the call number it is told to, and dispatches the rest.
    private sealed class FakeGate : IIndexerRequestGate
    {
        private readonly int _deferCall;

        public FakeGate(int deferCall = 0)
        {
            _deferCall = deferCall;
        }

        public List<IndexerRequestContext> Contexts { get; } = new();

        public async Task<IndexerRequestResult> SendAsync(
            IndexerRequestContext context,
            Func<CancellationToken, Task<HttpResponseMessage>> dispatch,
            CancellationToken cancellationToken)
        {
            if (Contexts.Count + 1 == _deferCall)
            {
                Contexts.Add(context);
                return new IndexerRequestResult.Deferred(
                    new RequestDeferral(DateTime.UtcNow.AddMinutes(5), RequestDeferralReason.CycleRequestBound));
            }

            Contexts.Add(context);
            return new IndexerRequestResult.Sent(await dispatch(cancellationToken));
        }

        public Task<RequestDeferral?> GetDeferralAsync(int indexerId, SearchOrigin origin, CancellationToken cancellationToken)
        {
            return Task.FromResult<RequestDeferral?>(null);
        }
    }
}
