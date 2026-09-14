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

[Trait("Area", "Search")]
[Trait("Name", "TorznabNewznabSearchProviderOutcomeTests")]
[Trait("Category", "TorznabNewznabSearchProvider")]
public sealed class TorznabNewznabSearchProviderOutcomeTests : BaseTests
{
    private const string EmptyChannelXml = """
<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"><channel><title>Test</title></channel></rss>
""";

    private const string OneItemXml = """
<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0">
  <channel>
    <title>Test</title>
    <item>
      <title>Alices Adventures in Wonderland</title>
      <guid>alice-1</guid>
      <link>magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567</link>
      <size>1000</size>
    </item>
  </channel>
</rss>
""";

    private const string NoChannelXml = """
<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"><notachannel /></rss>
""";

    [Fact]
    [Trait("Method", "SearchAsync")]
    [Trait("Scenario", "NonSuccessStatus")]
    public async Task SearchAsync_ServerError_ReportsUnavailableWithHttpStatus()
    {
        // Given
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        // When
        var observation = await provider.SearchAsync(CreateIndexer(), "Alice");

        // Then
        Assert.Equal(IndexerQueryOutcome.Unavailable, observation.Outcome);
        Assert.Equal(IndexerQueryReason.HttpStatus, observation.Reason);
        Assert.Empty(observation.Results);
        Assert.False(observation.Answered);
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
        var observation = await provider.SearchAsync(CreateIndexer(), "Alice");

        // Then
        Assert.Equal(IndexerQueryOutcome.Unavailable, observation.Outcome);
        Assert.Equal(IndexerQueryReason.Timeout, observation.Reason);
        Assert.False(observation.ShouldEscalate);
    }

    [Fact]
    [Trait("Method", "SearchAsync")]
    [Trait("Scenario", "NetworkFailure")]
    public async Task SearchAsync_ConnectionFails_ReportsUnavailableWithNetworkError()
    {
        // Given
        var provider = CreateProvider(_ => throw new HttpRequestException("connection refused"));

        // When
        var observation = await provider.SearchAsync(CreateIndexer(), "Alice");

        // Then
        Assert.Equal(IndexerQueryOutcome.Unavailable, observation.Outcome);
        Assert.Equal(IndexerQueryReason.NetworkError, observation.Reason);
        Assert.False(observation.ShouldEscalate);
    }

    [Fact]
    [Trait("Method", "SearchAsync")]
    [Trait("Scenario", "MalformedBody")]
    public async Task SearchAsync_MalformedXml_ReportsUnreadableWithMalformedXml()
    {
        // Given
        var provider = CreateProvider(_ => Ok("<rss><channel><item></rss>"));

        // When
        var observation = await provider.SearchAsync(CreateIndexer(), "Alice");

        // Then
        Assert.Equal(IndexerQueryOutcome.Unreadable, observation.Outcome);
        Assert.Equal(IndexerQueryReason.MalformedXml, observation.Reason);
        Assert.False(observation.Answered);

        // A parse failure is not a miss: a different query string will not make the body parseable.
        Assert.False(observation.ShouldEscalate);
    }

    [Fact]
    [Trait("Method", "SearchAsync")]
    [Trait("Scenario", "MissingChannel")]
    public async Task SearchAsync_ResponseWithoutChannel_ReportsUnreadableWithMissingChannel()
    {
        // Given
        var provider = CreateProvider(_ => Ok(NoChannelXml));

        // When
        var observation = await provider.SearchAsync(CreateIndexer(), "Alice");

        // Then
        Assert.Equal(IndexerQueryOutcome.Unreadable, observation.Outcome);
        Assert.Equal(IndexerQueryReason.MissingChannel, observation.Reason);
        Assert.False(observation.ShouldEscalate);
    }

    [Fact]
    [Trait("Method", "SearchAsync")]
    [Trait("Scenario", "EmptyChannel")]
    public async Task SearchAsync_WellFormedEmptyChannel_ReportsNoMatchNotUnavailable()
    {
        // Given: the control for this group. Without it, a mapping that answered Unavailable for
        // everything would pass every other test here.
        var provider = CreateProvider(_ => Ok(EmptyChannelXml));

        // When
        var observation = await provider.SearchAsync(CreateIndexer(), "Alice");

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
    public async Task SearchAsync_WellFormedResponseWithItems_ReportsHit()
    {
        // Given
        var provider = CreateProvider(_ => Ok(OneItemXml));

        // When
        var observation = await provider.SearchAsync(CreateIndexer(), "Alice");

        // Then
        Assert.Equal(IndexerQueryOutcome.Hit, observation.Outcome);
        Assert.Equal(IndexerQueryReason.None, observation.Reason);
        Assert.Single(observation.Results);
        Assert.True(observation.Answered);
        Assert.False(observation.ShouldEscalate);
    }

    [Fact]
    [Trait("Method", "SearchAsync")]
    [Trait("Scenario", "DetailRedaction")]
    public async Task SearchAsync_ServerError_DetailCarriesNoUrlOrApiKey()
    {
        // Given
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var indexer = CreateIndexer();

        // When
        var observation = await provider.SearchAsync(indexer, "Alice");

        // Then
        Assert.NotNull(observation.Detail);
        Assert.DoesNotContain("http", observation.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(indexer.ApiKey!, observation.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static TorznabNewznabSearchProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new DelegatingHandlerMock((request, _) => Task.FromResult(respond(request)));
        var httpClient = new HttpClient(handler);

        return new TorznabNewznabSearchProvider(httpClient, NullLogger<TorznabNewznabSearchProvider>.Instance);
    }

    private static Indexer CreateIndexer() =>
        new IndexerBuilder()
            .WithId(7)
            .WithName("Test Torznab")
            .WithType("Torrent")
            .WithImplementation("Torznab")
            .WithUrl("https://indexer.invalid")
            .WithApiKey("secret-api-key")
            .Build();
}
