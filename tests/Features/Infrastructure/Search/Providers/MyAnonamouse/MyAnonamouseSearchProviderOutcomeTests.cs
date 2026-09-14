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
using System.Text;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Search.Providers.MyAnonamouse;

[Trait("Area", "Search")]
[Trait("Name", "MyAnonamouseSearchProviderOutcomeTests")]
[Trait("Category", "MyAnonamouseSearchProvider")]
public sealed class MyAnonamouseSearchProviderOutcomeTests : BaseTests
{
    private const string EmptyResultJson = """{ "data": [], "found": 0 }""";

    private const string OneResultJson = """
[
  {
    "guid": "https://www.myanonamouse.net/t/700",
    "title": "Alices Adventures in Wonderland",
    "size": "1234"
  }
]
""";

    [Fact]
    [Trait("Method", "SearchAsync")]
    [Trait("Scenario", "NonSuccessStatus")]
    public async Task SearchAsync_ServerError_ReportsUnavailableWithHttpStatus()
    {
        // Given
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(string.Empty)
        });

        // When
        var observation = await provider.SearchAsync(CreateIndexer(), "Alice", null);

        // Then
        Assert.Equal(IndexerQueryOutcome.Unavailable, observation.Outcome);
        Assert.Equal(IndexerQueryReason.HttpStatus, observation.Reason);
        Assert.Empty(observation.Results);
        Assert.False(observation.ShouldEscalate);
        Assert.Equal("500", observation.Detail);
    }

    [Fact]
    [Trait("Method", "SearchAsync")]
    [Trait("Scenario", "RequestTimeout")]
    public async Task SearchAsync_RequestTimesOut_ReportsUnavailableWithTimeout()
    {
        // Given: the shape HttpClient uses for its own request timeout
        var provider = CreateProvider(_ => throw new TaskCanceledException("timed out", new TimeoutException()));

        // When
        var observation = await provider.SearchAsync(CreateIndexer(), "Alice", null);

        // Then
        Assert.Equal(IndexerQueryOutcome.Unavailable, observation.Outcome);
        Assert.Equal(IndexerQueryReason.Timeout, observation.Reason);
        Assert.False(observation.ShouldEscalate);
    }

    [Fact]
    [Trait("Method", "SearchAsync")]
    [Trait("Scenario", "MissingCredentials")]
    public async Task SearchAsync_WithoutMamId_ReportsNotConfigured()
    {
        // Given
        var provider = CreateProvider(_ => Ok(OneResultJson));
        var indexer = CreateIndexer(mamId: null);

        // When
        var observation = await provider.SearchAsync(indexer, "Alice", null);

        // Then
        Assert.Equal(IndexerQueryOutcome.NotConfigured, observation.Outcome);
        Assert.False(observation.Answered);
        Assert.False(observation.ShouldEscalate);
    }

    [Fact]
    [Trait("Method", "SearchAsync")]
    [Trait("Scenario", "EmptyResultSet")]
    public async Task SearchAsync_WellFormedEmptyResponse_ReportsNoMatchNotUnavailable()
    {
        // Given: the control for this group. Without it, a mapping that answered Unavailable for
        // everything would pass every other outcome test here.
        var provider = CreateProvider(_ => Ok(EmptyResultJson));

        // When
        var observation = await provider.SearchAsync(CreateIndexer(), "Alice", null);

        // Then
        Assert.Equal(IndexerQueryOutcome.NoMatch, observation.Outcome);
        Assert.Equal(IndexerQueryReason.EmptyChannel, observation.Reason);
        Assert.Empty(observation.Results);
        Assert.True(observation.Answered);
        Assert.True(observation.ShouldEscalate);
    }

    [Fact]
    [Trait("Method", "SearchAsync")]
    [Trait("Scenario", "ResultsReturned")]
    public async Task SearchAsync_ResponseWithItems_ReportsHit()
    {
        // Given
        var provider = CreateProvider(_ => Ok(OneResultJson));

        // When
        var observation = await provider.SearchAsync(CreateIndexer(), "Alice", null);

        // Then
        Assert.Equal(IndexerQueryOutcome.Hit, observation.Outcome);
        Assert.Equal(IndexerQueryReason.None, observation.Reason);
        Assert.Single(observation.Results);
        Assert.True(observation.Answered);
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static MyAnonamouseSearchProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new DelegatingHandlerMock((request, _) => Task.FromResult(respond(request)));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.myanonamouse.net") };

        return new MyAnonamouseSearchProvider(
            NullLogger<MyAnonamouseSearchProvider>.Instance,
            httpClient,
            Mock.Of<IIndexerRepository>());
    }

    private static Indexer CreateIndexer(string? mamId = "test_mam")
    {
        var builder = new IndexerBuilder()
            .WithId(9)
            .WithName("MyAnonamouse")
            .WithType("Torrent")
            .WithImplementation("MyAnonamouse")
            .WithUrl("https://www.myanonamouse.net");

        if (mamId != null)
        {
            builder.WithSetting("mam_id", mamId);
        }

        return builder.Build();
    }
}
