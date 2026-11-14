/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using static Listenarr.Tests.Features.Api.Features.Indexers.IndexerGateTestSupport;

namespace Listenarr.Tests.Features.Api.Features.Indexers;

[Trait("Name", "IndexerTestWorkflowGateTests")]
[Trait("Category", "IndexerTestWorkflow")]
public sealed class IndexerTestWorkflowGateTests : BaseTests
{
    private const string EmptyFeed = "<rss><channel></channel></rss>";

    [Fact]
    public async Task ConnectionTest_InCooldown_SendsNothingAndWritesNoTestResult()
    {
        // Given: a stored Torznab indexer whose gate defers every request
        await using var server = RedirectingServer("/none", EmptyFeed);
        var repository = new Mock<IIndexerRepository>();
        var indexer = CreateIndexer(server);
        var workflow = CreateWorkflow(repository.Object, new DeferringGate(), new HttpClientHandler { AllowAutoRedirect = false });

        // When
        var result = await workflow.TestGenericIndexerAsync(indexer, persist: true);

        // Then: the cooldown is reported, nothing reached the wire and no test result was written
        Assert.False(result.Succeeded);
        Assert.Equal(Cooldown.Describe(), result.Message);
        Assert.Contains("indexer in cooldown until 2026-06-19T12:30:00.0000000Z (RateLimited)", result.Message);
        Assert.Empty(server.RequestPaths);
        Assert.Null(indexer.LastTestedAt);
        Assert.Null(indexer.LastTestSuccessful);
        repository.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Never);
        repository.Verify(r => r.UpdateAsync(It.IsAny<Indexer>()), Times.Never);
    }

    [Fact]
    public async Task ConnectionTest_NotInCooldown_SendsAndWritesTestResult()
    {
        // Given: the same indexer behind a gate that sends
        await using var server = RedirectingServer("/none", EmptyFeed);
        var repository = new Mock<IIndexerRepository>();
        var indexer = CreateIndexer(server);
        var workflow = CreateWorkflow(repository.Object, new RecordingGate(), new HttpClientHandler { AllowAutoRedirect = false });

        // When
        var result = await workflow.TestGenericIndexerAsync(indexer, persist: true);

        // Then: the control for the cooldown test: one request went out and a result was written
        Assert.True(result.Succeeded);
        Assert.Single(server.RequestPaths);
        Assert.NotNull(indexer.LastTestedAt);
        Assert.True(indexer.LastTestSuccessful);
    }

    [Fact]
    public async Task ConnectionTestRedirectHopsAreGateCalls()
    {
        // Given: a Torznab test URL that answers 302 to a second path, which answers 200
        await using var server = RedirectingServer("/api", EmptyFeed);
        var gate = new RecordingGate();
        var workflow = CreateWorkflow(Mock.Of<IIndexerRepository>(), gate, new HttpClientHandler { AllowAutoRedirect = false });

        // When
        var result = await workflow.TestGenericIndexerAsync(CreateIndexer(server), persist: false);

        // Then: both wire requests are gate calls, interactive, and the followed hop is a redirect
        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "/api", "/moved" }, server.RequestPaths);
        Assert.Equal(
            new[]
            {
                new IndexerRequestContext(7, SearchOrigin.Interactive, IndexerRequestKind.ConnectionTest),
                new IndexerRequestContext(7, SearchOrigin.Interactive, IndexerRequestKind.Redirect)
            },
            gate.Contexts);
    }

    [Fact]
    public async Task ConnectionTest_OnAnAutoRedirectingClient_HidesTheHopFromTheGate()
    {
        // Given: the same server, with the default client that follows redirects inside one send
        await using var server = RedirectingServer("/api", EmptyFeed);
        var gate = new RecordingGate();
        var workflow = CreateWorkflow(Mock.Of<IIndexerRepository>(), gate, new HttpClientHandler());

        // When
        await workflow.TestGenericIndexerAsync(CreateIndexer(server), persist: false);

        // Then: the control for the redirect test: two wire requests, one gate call
        Assert.Equal(new[] { "/api", "/moved" }, server.RequestPaths);
        Assert.Single(gate.Contexts);
    }

    private static IndexerTestWorkflow CreateWorkflow(IIndexerRepository repository, IIndexerRequestGate gate, HttpMessageHandler handler)
        => new(repository, new StubHttpClientFactory(handler), gate, NullLogger<IndexerTestWorkflow>.Instance);

    private static Indexer CreateIndexer(LoopbackHttpServer server) => new()
    {
        Id = 7,
        Name = "Torznab",
        Url = server.BaseUri.ToString(),
        Implementation = "Torznab",
        Type = "Torrent",
        ApiKey = "key"
    };
}
