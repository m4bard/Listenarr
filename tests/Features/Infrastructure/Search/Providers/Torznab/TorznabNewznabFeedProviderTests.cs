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
using Listenarr.Tests.Common;
using static Listenarr.Tests.Features.Infrastructure.Search.Providers.Torznab.TorznabFeedTestKit;

namespace Listenarr.Tests.Features.Infrastructure.Search.Providers.Torznab;

[Trait("Name", "TorznabNewznabFeedProviderTests")]
[Trait("Category", "TorznabNewznabSearchProvider")]
public sealed class TorznabNewznabFeedProviderTests : BaseTests
{
    [Fact]
    public async Task FetchPageAsync_EachPage_IsOneGateCallWithTheCallersKind()
    {
        // Given: a two-page poll, page 0 sent as FeedPoll and page 1 as FeedPage
        var gate = new RecordingFeedGate();
        var wire = new WireLog().Respond(Feed(3)).Respond(Feed(2));
        var provider = CreateProvider(gate, wire);
        var indexer = CreateIndexer();

        // When
        var first = await provider.FetchPageAsync(indexer, 0, FeedPoll, CancellationToken.None);
        var second = await provider.FetchPageAsync(indexer, first.ItemsReceived, FeedPage, CancellationToken.None);

        // Then: one gate call and one wire request per page, Discovery with no allowance
        Assert.Equal(
            new[]
            {
                new IndexerRequestContext(IndexerId, SearchOrigin.Discovery, IndexerRequestKind.FeedPoll),
                new IndexerRequestContext(IndexerId, SearchOrigin.Discovery, IndexerRequestKind.FeedPage)
            },
            gate.Contexts);
        Assert.Equal(2, wire.Requests.Count);
        Assert.Contains("offset=3", wire.Requests[1].Query);
        Assert.Equal(IndexerSearchStatus.Completed, first.Status);
        Assert.Equal(3, first.Items.Count);
        Assert.Equal(IndexerSearchStatus.Completed, second.Status);
        Assert.Equal(2, second.Items.Count);
    }

    [Fact]
    public async Task FetchPageAsync_SecondPageDeferred_ReturnsDeferredWithoutAWireRequest()
    {
        // Given: the gate lets page 0 through and defers page 1
        var gate = new RecordingFeedGate();
        gate.DeferCalls.Add(2);
        var wire = new WireLog().Respond(Feed(3));
        var provider = CreateProvider(gate, wire);
        var indexer = CreateIndexer();

        // When
        var first = await provider.FetchPageAsync(indexer, 0, FeedPoll, CancellationToken.None);
        var second = await provider.FetchPageAsync(indexer, first.ItemsReceived, FeedPage, CancellationToken.None);

        // Then: page 0 is read; page 1 carries the gate's deferral and sent nothing
        Assert.Equal(IndexerSearchStatus.Completed, first.Status);
        Assert.Equal(3, first.Items.Count);
        Assert.Equal(IndexerSearchStatus.Deferred, second.Status);
        Assert.Equal(RecordingFeedGate.Deferral, second.Deferral);
        Assert.Empty(second.Items);
        Assert.Equal(new[] { IndexerRequestKind.FeedPoll, IndexerRequestKind.FeedPage }, gate.Contexts.Select(c => c.Kind));
        Assert.Single(wire.Requests);
    }

    [Theory]
    [InlineData(IndexerRequestKind.Search)]
    [InlineData(IndexerRequestKind.FallbackQuery)]
    public async Task FetchPageAsync_ContextWithoutAFeedKind_Throws(IndexerRequestKind kind)
    {
        var gate = new RecordingFeedGate();
        var wire = new WireLog().Respond(Feed(1));
        var provider = CreateProvider(gate, wire);
        var context = new IndexerSearchContext(SearchOrigin.Discovery, CycleAllowance: null, kind);

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.FetchPageAsync(CreateIndexer(), 0, context, CancellationToken.None));

        Assert.Empty(gate.Contexts);
        Assert.Empty(wire.Requests);
    }

    [Fact]
    public async Task FetchPageAsync_DefaultContext_Throws()
    {
        var provider = CreateProvider(new RecordingFeedGate(), new WireLog());

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.FetchPageAsync(
                CreateIndexer(), 0, new IndexerSearchContext(SearchOrigin.Discovery, CycleAllowance: null), CancellationToken.None));
    }

    [Fact]
    public async Task FetchPageAsync_TimeoutWithoutCallerCancel_IsFailedTimeout()
    {
        // Given: the indexer never answers and the client's own timeout fires
        var wire = new WireLog().Respond(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        });
        var client = new HttpClient(wire.Handler()) { Timeout = TimeSpan.FromMilliseconds(100) };
        var provider = CreateProvider(new RecordingFeedGate(), wire, client);

        // When
        var page = await provider.FetchPageAsync(CreateIndexer(), 0, FeedPoll, CancellationToken.None);

        // Then
        Assert.Equal(IndexerSearchStatus.Failed, page.Status);
        Assert.Equal("timeout", page.FailureReason);
    }

    [Fact]
    public async Task FetchPageAsync_CallerCancelsMidRequest_IsCancelled()
    {
        // Given: the caller cancels while the request is in flight
        using var cancellation = new CancellationTokenSource();
        var wire = new WireLog().Respond(async (_, token) =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        });
        var provider = CreateProvider(new RecordingFeedGate(), wire);

        // When
        var page = await provider.FetchPageAsync(CreateIndexer(), 0, FeedPoll, cancellation.Token);

        // Then
        Assert.Equal(IndexerSearchStatus.Cancelled, page.Status);
        Assert.Null(page.FailureReason);
        Assert.Empty(page.Items);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "<rss><channel></channel></rss>", "status 503")]
    [InlineData(HttpStatusCode.OK, "<rss><channel><item><title>cut off", "unparseable response")]
    [InlineData(HttpStatusCode.OK, "<error code=\"100\" description=\"Incorrect user credentials\" />", "indexer error 100")]
    [InlineData(HttpStatusCode.OK, "<rss></rss>", "no channel element")]
    public async Task FetchPageAsync_ResponseThatIsNotAFeed_IsFailed(HttpStatusCode status, string body, string reason)
    {
        var wire = new WireLog().Respond(status, body);
        var provider = CreateProvider(new RecordingFeedGate(), wire);

        var page = await provider.FetchPageAsync(CreateIndexer(), 0, FeedPoll, CancellationToken.None);

        Assert.Equal(IndexerSearchStatus.Failed, page.Status);
        Assert.Equal(reason, page.FailureReason);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task FetchPageAsync_TransportError_IsFailed()
    {
        var wire = new WireLog().Respond((_, _) => throw new HttpRequestException("connection refused"));
        var provider = CreateProvider(new RecordingFeedGate(), wire);

        var page = await provider.FetchPageAsync(CreateIndexer(), 0, FeedPoll, CancellationToken.None);

        Assert.Equal(IndexerSearchStatus.Failed, page.Status);
        Assert.Equal("error", page.FailureReason);
    }

    [Fact]
    public async Task FetchPageAsync_EmptyFeed_IsCompletedWithNothingReceived()
    {
        var provider = CreateProvider(new RecordingFeedGate(), new WireLog().Respond(Feed(0)));

        var page = await provider.FetchPageAsync(CreateIndexer(), 0, FeedPoll, CancellationToken.None);

        Assert.Equal(IndexerSearchStatus.Completed, page.Status);
        Assert.Equal(0, page.ItemsReceived);
        Assert.Empty(page.Items);
        Assert.Null(page.TotalAvailable);
    }

    [Theory]
    [InlineData("Torznab", true)]
    [InlineData("newznab", true)]
    [InlineData("MyAnonamouse", false)]
    [InlineData("InternetArchive", false)]
    public void Supports_TorznabAndNewznabOnly(string implementation, bool expected)
    {
        var provider = CreateProvider(new RecordingFeedGate(), new WireLog());

        Assert.Equal(expected, provider.Supports(CreateIndexer(implementation)));
    }
}
