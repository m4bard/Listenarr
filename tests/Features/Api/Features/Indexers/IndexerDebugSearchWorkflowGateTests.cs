/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Text.Json;
using Listenarr.Api.Startup;
using Listenarr.Infrastructure.DependencyInjection.Platform;
using Listenarr.Infrastructure.DependencyInjection.Search;
using Listenarr.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using static Listenarr.Tests.Features.Api.Features.Indexers.IndexerGateTestSupport;

namespace Listenarr.Tests.Features.Api.Features.Indexers;

[Trait("Name", "IndexerDebugSearchWorkflowGateTests")]
[Trait("Category", "IndexerDebugSearchWorkflow")]
public sealed class IndexerDebugSearchWorkflowGateTests : BaseTests
{
    private const string SearchPath = "/tor/js/loadSearchJSONbasic.php";

    [Fact]
    public async Task ManualSearch_InCooldown_ReturnsTheDeferralAndSendsNothing()
    {
        // Given: a MyAnonamouse indexer whose gate defers every request
        await using var server = RedirectingServer("/none", "[]");
        var workflow = new IndexerDebugSearchWorkflow(
            new StubHttpClientFactory(new HttpClientHandler()),
            new DeferringGate(),
            NullLogger<IndexerDebugSearchWorkflow>.Instance);

        // When
        var result = await workflow.ExecuteMyAnonamouseAsync(CreateIndexer(server), 7, QueryBody(), RequestFor(server), HttpContextFor(server));

        // Then
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal(Cooldown.Describe(), MessageOf(result));
        Assert.Empty(server.RequestPaths);
    }

    [Fact]
    public async Task ManualSearch_NotInCooldown_SendsTheSearch()
    {
        // Given: the same indexer behind a gate that sends
        await using var server = RedirectingServer("/none", "[]");
        var workflow = new IndexerDebugSearchWorkflow(
            new StubHttpClientFactory(new HttpClientHandler()),
            new RecordingGate(),
            NullLogger<IndexerDebugSearchWorkflow>.Instance);

        // When
        var result = await workflow.ExecuteMyAnonamouseAsync(CreateIndexer(server), 7, QueryBody(), RequestFor(server), HttpContextFor(server));

        // Then: the control for the cooldown test: the search reached the wire
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Contains(SearchPath, server.RequestPaths);
    }

    [Fact]
    public async Task DebugSearchRedirectHopsAreGateCalls()
    {
        // Given: a search endpoint that answers 302 to a second path, which answers 200
        await using var server = RedirectingServer(SearchPath, "{\"data\":[]}");
        var gate = new RecordingGate();
        var workflow = new IndexerDebugSearchWorkflow(
            new StubHttpClientFactory(new HttpClientHandler()),
            gate,
            NullLogger<IndexerDebugSearchWorkflow>.Instance);

        // When
        var result = await workflow.ExecuteMyAnonamouseAsync(CreateIndexer(server), 7, QueryBody(), RequestFor(server), HttpContextFor(server));

        // Then: both wire requests are gate calls: an interactive search and its redirect hop
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Equal(new[] { SearchPath, "/moved" }, server.RequestPaths.Take(2));
        Assert.Equal(
            new[]
            {
                new IndexerRequestContext(7, SearchOrigin.Interactive, IndexerRequestKind.Search),
                new IndexerRequestContext(7, SearchOrigin.Interactive, IndexerRequestKind.Redirect)
            },
            gate.Contexts);
    }

    [Fact]
    public async Task DebugWorkflow_ResolvedFromDi_UsesGate()
    {
        // Given: the production registrations, with the indexer in a persisted cooldown
        await using var server = RedirectingServer("/none", "[]");
        var store = new Mock<IIndexerRequestStateStore>();
        store.Setup(s => s.LoadAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<IndexerRequestState>
        {
            new()
            {
                IndexerId = 7,
                CooldownUntilUtc = DateTime.UtcNow.AddHours(1),
                CooldownReason = nameof(RequestDeferralReason.RateLimited)
            }
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton(store.Object);
        services.AddSingleton(Mock.Of<IConfigurationService>());
        services.AddSingleton(Mock.Of<IIndexerRepository>());
        services.AddPlatformHttpClients(new ConfigurationBuilder().Build());
        services.AddSearchServices();
        services.AddListenarrDomainWorkflows();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // When: the container builds the workflow and it searches
        var workflow = scope.ServiceProvider.GetRequiredService<IndexerDebugSearchWorkflow>();
        var result = await workflow.ExecuteMyAnonamouseAsync(CreateIndexer(server), 7, QueryBody(), RequestFor(server), HttpContextFor(server));

        // Then: the real gate deferred it, so nothing reached the wire
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Contains("indexer in cooldown until", MessageOf(result));
        Assert.Empty(server.RequestPaths);
    }

    private static Indexer CreateIndexer(LoopbackHttpServer server) => new()
    {
        Id = 7,
        Name = "MAM",
        Url = server.BaseUri.ToString(),
        Implementation = "MyAnonamouse",
        Type = "Torrent",
        AdditionalSettings = """{"mam_id":"secret"}"""
    };

    private static JsonElement QueryBody() => JsonDocument.Parse("""{"query":"test"}""").RootElement;

    private static HttpContext HttpContextFor(LoopbackHttpServer server)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = server.BaseUri.Scheme;
        context.Request.Host = new HostString(server.BaseUri.Authority);
        return context;
    }

    private static HttpRequest RequestFor(LoopbackHttpServer server) => HttpContextFor(server).Request;

    private static string? MessageOf(IndexerDebugSearchWorkflowResult result)
        => (string?)result.Payload.GetType().GetProperty("message")?.GetValue(result.Payload);
}
