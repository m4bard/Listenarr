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

namespace Listenarr.Tests.Features.Application.Search.Indexers.Common
{
    /// <summary>
    /// How a Torznab indexer's answer maps to a typed outcome, through the workflow and the real
    /// provider on a stub handler. The caller's cycle token is kept separate from the HttpClient's
    /// own timeout, so a timeout is never mistaken for a cancellation.
    /// </summary>
    [Trait("Name", "IndexerSearchOutcomeMappingTests")]
    [Trait("Category", "IndexerSearchWorkflow")]
    public class IndexerSearchOutcomeMappingTests : BaseTests
    {
        private const string EmptyFeed = "<rss><channel></channel></rss>";
        private static readonly IndexerSearchContext SweepContext = new(SearchOrigin.Sweep, CycleAllowance: null);

        [Fact]
        public async Task SearchIndexersDetailedAsync_Status500_IsFailed()
        {
            var stub = new StubServer((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
            using var cycle = new CancellationTokenSource();

            var outcome = await SearchOneAsync(stub, SweepContext, cycle.Token);

            Assert.Equal(IndexerSearchStatus.Failed, outcome.Status);
            Assert.Equal("status 500", outcome.FailureReason);
            Assert.Empty(outcome.Results);
            Assert.Equal(1, outcome.RequestsSent);
            Assert.Equal(1, stub.Requests);
        }

        [Fact]
        public async Task SearchIndexersDetailedAsync_EmptyFeed_IsCompletedWithNoResults()
        {
            var stub = new StubServer((_, _) => Task.FromResult(Feed(EmptyFeed)));
            using var cycle = new CancellationTokenSource();

            var outcome = await SearchOneAsync(stub, SweepContext, cycle.Token);

            Assert.Equal(IndexerSearchStatus.Completed, outcome.Status);
            Assert.Null(outcome.FailureReason);
            Assert.Empty(outcome.Results);
            Assert.Equal(1, outcome.RequestsSent);
        }

        [Fact]
        public async Task SearchIndexersDetailedAsync_HandlerThrowsTaskCanceled_TokenNotCancelled_IsFailedTimeout()
        {
            var stub = new StubServer((_, _) => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));
            using var cycle = new CancellationTokenSource();

            var outcome = await SearchOneAsync(stub, SweepContext, cycle.Token);

            Assert.False(cycle.IsCancellationRequested);
            Assert.Equal(IndexerSearchStatus.Failed, outcome.Status);
            Assert.Equal("timeout", outcome.FailureReason);
            Assert.Equal(1, outcome.RequestsSent);
        }

        [Fact]
        public async Task SearchIndexersDetailedAsync_ClientTimeoutElapses_TokenNotCancelled_IsFailedTimeout()
        {
            var stub = new StubServer(async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return Feed(EmptyFeed);
            });
            using var cycle = new CancellationTokenSource();

            var outcome = await SearchOneAsync(stub, SweepContext, cycle.Token, clientTimeout: TimeSpan.FromMilliseconds(200));

            Assert.False(cycle.IsCancellationRequested);
            Assert.Equal(IndexerSearchStatus.Failed, outcome.Status);
            Assert.Equal("timeout", outcome.FailureReason);
        }

        [Fact]
        public async Task SearchIndexersDetailedAsync_CycleTokenCancelled_IsCancelled()
        {
            using var cycle = new CancellationTokenSource();
            var stub = new StubServer(async (_, token) =>
            {
                await cycle.CancelAsync();
                await Task.Delay(Timeout.Infinite, token);
                return Feed(EmptyFeed);
            });

            var outcome = await SearchOneAsync(stub, SweepContext, cycle.Token);

            Assert.Equal(IndexerSearchStatus.Cancelled, outcome.Status);
            Assert.Null(outcome.FailureReason);
            Assert.Empty(outcome.Results);
        }

        [Fact]
        public async Task SearchIndexersDetailedAsync_AllowanceSpent_IsDeferredWithoutARequest()
        {
            var stub = new StubServer((_, _) => Task.FromResult(Feed(EmptyFeed)));
            var context = new IndexerSearchContext(SearchOrigin.Sweep, new CycleRequestAllowance(0));

            var outcome = await SearchOneAsync(stub, context, CancellationToken.None);

            Assert.Equal(IndexerSearchStatus.Deferred, outcome.Status);
            Assert.Equal(RequestDeferralReason.CycleRequestBound, outcome.Deferral?.Reason);
            Assert.Equal(0, outcome.RequestsSent);
            Assert.Equal(0, stub.Requests);
        }

        [Fact]
        public async Task SearchIndexersDetailedAsync_IndexerIds_SearchesOnlyThoseIndexers()
        {
            var stub = new StubServer((_, _) => Task.FromResult(Feed(EmptyFeed)));
            var workflow = CreateWorkflow(
                new[] { TorznabIndexer(1), TorznabIndexer(2) },
                new[] { TorznabProvider(stub, timeout: null) });

            var report = await workflow.SearchIndexersDetailedAsync(
                "query", SweepContext, new[] { 2 }, isAutomaticSearch: true, request: null, CancellationToken.None);

            var outcome = Assert.Single(report.Outcomes);
            Assert.Equal(2, outcome.IndexerId);
            Assert.Equal(1, stub.Requests);
        }

        [Fact]
        public async Task SearchIndexersDetailedAsync_ProviderThrowsTaskCanceled_TokenNotCancelled_IsFailedTimeout()
        {
            var workflow = CreateWorkflow(
                new[] { TorznabIndexer(1) },
                new[] { new ThrowingProvider(() => new TaskCanceledException()) });
            using var cycle = new CancellationTokenSource();

            var report = await workflow.SearchIndexersDetailedAsync(
                "query", SweepContext, indexerIds: null, isAutomaticSearch: true, request: null, cycle.Token);

            var outcome = Assert.Single(report.Outcomes);
            Assert.Equal(IndexerSearchStatus.Failed, outcome.Status);
            Assert.Equal("timeout", outcome.FailureReason);
        }

        [Fact]
        public async Task SearchIndexersDetailedAsync_ProviderThrowsWhileCycleCancelled_IsCancelled()
        {
            using var cycle = new CancellationTokenSource();
            var workflow = CreateWorkflow(
                new[] { TorznabIndexer(1) },
                new[] { new ThrowingProvider(() =>
                {
                    cycle.Cancel();
                    return new OperationCanceledException(cycle.Token);
                }) });

            var report = await workflow.SearchIndexersDetailedAsync(
                "query", SweepContext, indexerIds: null, isAutomaticSearch: true, request: null, cycle.Token);

            Assert.Equal(IndexerSearchStatus.Cancelled, Assert.Single(report.Outcomes).Status);
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.OK)]
        [InlineData(HttpStatusCode.RequestTimeout)]
        public async Task SearchIndexersAsync_FailedEmptyOrTimedOut_StillReturnsAnEmptyList(HttpStatusCode answer)
        {
            // The list-returning wrapper keeps its behaviour: every one of these is an empty list.
            var stub = new StubServer((_, _) => answer switch
            {
                HttpStatusCode.OK => Task.FromResult(Feed(EmptyFeed)),
                HttpStatusCode.RequestTimeout => throw new TaskCanceledException(),
                _ => Task.FromResult(new HttpResponseMessage(answer))
            });
            var workflow = CreateWorkflow(new[] { TorznabIndexer(1) }, new[] { TorznabProvider(stub, timeout: null) });

            var results = await workflow.SearchIndexersAsync("query", isAutomaticSearch: true);

            Assert.Empty(results);
            Assert.Equal(1, stub.Requests);
        }

        private static async Task<IndexerSearchOutcome> SearchOneAsync(
            StubServer stub,
            IndexerSearchContext context,
            CancellationToken cycleToken,
            TimeSpan? clientTimeout = null)
        {
            var workflow = CreateWorkflow(new[] { TorznabIndexer(1) }, new[] { TorznabProvider(stub, clientTimeout) });
            var report = await workflow.SearchIndexersDetailedAsync(
                "query", context, indexerIds: null, isAutomaticSearch: true, request: null, cycleToken);
            return Assert.Single(report.Outcomes);
        }

        private static IndexerSearchWorkflow CreateWorkflow(
            IEnumerable<Indexer> enabledIndexers,
            IEnumerable<IIndexerSearchProvider> providers)
        {
            var indexerRepository = new Mock<IIndexerRepository>();
            indexerRepository
                .Setup(r => r.GetEnabledAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(enabledIndexers.ToList());

            return new IndexerSearchWorkflow(
                new HttpClient(),
                Mock.Of<IConfigurationService>(),
                indexerRepository.Object,
                providers,
                new IndexerAdditionalSettingsParser(NullLogger<IndexerAdditionalSettingsParser>.Instance),
                NullLogger<IndexerSearchWorkflow>.Instance);
        }

        private static TorznabNewznabSearchProvider TorznabProvider(StubServer stub, TimeSpan? timeout)
        {
            var httpClient = new HttpClient(stub);
            if (timeout.HasValue)
            {
                httpClient.Timeout = timeout.Value;
            }

            return new TorznabNewznabSearchProvider(
                httpClient,
                new PassThroughIndexerRequestGate(TimeProvider.System),
                NullLogger<TorznabNewznabSearchProvider>.Instance);
        }

        private static Indexer TorznabIndexer(int id)
        {
            return new IndexerBuilder()
                .WithId(id)
                .WithName($"Torznab {id}")
                .WithImplementation("Torznab")
                .WithUrl($"https://indexer{id}.example")
                .Build();
        }

        private static HttpResponseMessage Feed(string xml)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) };
        }

        private sealed class StubServer : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _answer;
            private int _requests;

            public StubServer(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
            {
                _answer = answer;
            }

            public int Requests => Volatile.Read(ref _requests);

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _requests);
                return _answer(request, cancellationToken);
            }
        }

        private sealed class ThrowingProvider : IIndexerSearchProvider
        {
            private readonly Func<Exception> _exception;

            public ThrowingProvider(Func<Exception> exception)
            {
                _exception = exception;
            }

            public string IndexerType => "Torznab";

            public Task<IndexerSearchOutcome> SearchAsync(
                Indexer indexer,
                string query,
                string? category,
                SearchRequest? request,
                IndexerSearchContext context,
                CancellationToken cancellationToken)
            {
                throw _exception();
            }
        }
    }
}
