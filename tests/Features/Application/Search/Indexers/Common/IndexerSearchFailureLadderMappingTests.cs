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
using Listenarr.Infrastructure.Search.RequestGate;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestGate.IndexerRequestGateHarness;

namespace Listenarr.Tests.Features.Application.Search.Indexers.Common
{
    /// <summary>
    /// How the workflow maps a failure-ladder deferral, through the real request gate (failure
    /// threshold <see cref="IndexerRequestPolicy.FailureThreshold"/>) and the real Internet Archive
    /// provider on a counting stub. A cooldown the indexer's own failures set, with no candidates
    /// left, is a failure that keeps its deferral; every other deferral stays Deferred.
    /// </summary>
    [Trait("Name", "IndexerSearchFailureLadderMappingTests")]
    [Trait("Category", "IndexerSearchWorkflow")]
    public class IndexerSearchFailureLadderMappingTests : BaseTests
    {
        private const int IndexerId = 42;
        private const string AudioMetadata = """
{
  "metadata": { "language": "English" },
  "files": [
    { "name": "alice.m4b", "format": "LibriVox Apple Audiobook", "size": "900" }
  ]
}
""";

        private static readonly IndexerSearchContext SweepContext = new(SearchOrigin.Sweep, CycleAllowance: null);

        public static TheoryData<RequestDeferralReason> NonLadderReasons()
        {
            var reasons = new TheoryData<RequestDeferralReason>();
            foreach (var reason in Enum.GetValues<RequestDeferralReason>().Where(r => r != RequestDeferralReason.FailureLadder))
            {
                reasons.Add(reason);
            }

            return reasons;
        }

        [Fact]
        public async Task FailureLadderDeferralMapsToFailed_NoCandidatesLeft_IsFailedAndKeepsDeferral()
        {
            // Given: the search answers and every metadata GET returns 503, one item more than the threshold
            var stub = new CountingStub(_ => Status(HttpStatusCode.ServiceUnavailable), "item_1", "item_2", "item_3", "item_4");

            // When
            var outcome = await SearchOneAsync(stub);

            // Then: the third failure trips the ladder, the fourth item is held back, and nothing is left
            var expectedUntil = (Start + (IndexerRequestPolicy.FailureThreshold * Interval) + IndexerRequestPolicy.EscalationLadder[0]).UtcDateTime;
            Assert.Equal(IndexerSearchStatus.Failed, outcome.Status);
            Assert.Equal("indexer failing (cooldown)", outcome.FailureReason);
            Assert.Equal(new RequestDeferral(expectedUntil, RequestDeferralReason.FailureLadder), outcome.Deferral);
            Assert.Empty(outcome.Results);
            Assert.Equal(1 + IndexerRequestPolicy.FailureThreshold, outcome.RequestsSent);
            Assert.Equal(1 + IndexerRequestPolicy.FailureThreshold, stub.Requests);
        }

        [Fact]
        public async Task FailureLadderDeferralMapsToFailed_RateLimitedMidSearchWithNoCandidates_StaysDeferred()
        {
            // Given: the first metadata GET answers 429, which sets a rate-limit cooldown
            var stub = new CountingStub(_ => Status(HttpStatusCode.TooManyRequests), "item_1", "item_2");

            // When
            var outcome = await SearchOneAsync(stub);

            // Then: the second metadata GET is held back, and the outcome stays Deferred
            Assert.Equal(IndexerSearchStatus.Deferred, outcome.Status);
            Assert.Equal(RequestDeferralReason.RateLimited, outcome.Deferral?.Reason);
            Assert.Null(outcome.FailureReason);
            Assert.Empty(outcome.Results);
            Assert.Equal(2, stub.Requests);
        }

        [Fact]
        public async Task FailureLadderDeferralMapsToFailed_CandidatesLeft_StaysDeferred()
        {
            // Given: the first item's metadata answers, then the next three return 503
            var stub = new CountingStub(
                uri => uri.AbsolutePath.EndsWith("/alice_book", StringComparison.Ordinal)
                    ? Ok(AudioMetadata)
                    : Status(HttpStatusCode.ServiceUnavailable),
                "alice_book", "item_2", "item_3", "item_4", "item_5");

            // When
            var outcome = await SearchOneAsync(stub);

            // Then: the ladder trips and holds the fifth item back, and the candidate keeps it Deferred
            Assert.Equal(IndexerSearchStatus.Deferred, outcome.Status);
            Assert.Equal(RequestDeferralReason.FailureLadder, outcome.Deferral?.Reason);
            Assert.Null(outcome.FailureReason);
            Assert.Single(outcome.Results);
            Assert.Equal(2 + IndexerRequestPolicy.FailureThreshold, stub.Requests);
        }

        [Theory]
        [MemberData(nameof(NonLadderReasons))]
        public async Task FailureLadderDeferralMapsToFailed_OtherReasonWithNoCandidates_StaysDeferred(RequestDeferralReason reason)
        {
            // Given: a provider deferred for another reason with nothing gathered
            var deferral = new RequestDeferral(Start.UtcDateTime.AddMinutes(5), reason);
            var provider = new DeferringProvider(deferral);

            // When
            var report = await CreateWorkflow(provider).SearchIndexersDetailedAsync(
                "Alice", SweepContext, indexerIds: null, isAutomaticSearch: true, request: null, CancellationToken.None);

            // Then
            var outcome = Assert.Single(report.Outcomes);
            Assert.Equal(IndexerSearchStatus.Deferred, outcome.Status);
            Assert.Equal(deferral, outcome.Deferral);
            Assert.Null(outcome.FailureReason);
        }

        private static async Task<IndexerSearchOutcome> SearchOneAsync(CountingStub stub)
        {
            var clock = new ManualClock(Start);
            var gate = new IndexerRequestGate(
                new IndexerRequestStateCache(new RecordingStateStore(), NullLogger<IndexerRequestStateCache>.Instance),
                [],
                new IndexerConcurrencyLimiter(IndexerRequestPolicy.MaxConcurrentPerIndexer, IndexerRequestPolicy.MaxConcurrentGlobal),
                new IndexerPacingSchedule(Interval, clock, clock.AdvanceDelayAsync),
                clock,
                NullLogger<IndexerRequestGate>.Instance);

            var configurationService = new Mock<IConfigurationService>();
            configurationService
                .Setup(service => service.GetApplicationSettingsAsync())
                .ReturnsAsync(new ApplicationSettingsBuilder().WithExtractArchive().Build());
            var provider = new InternetArchiveSearchProvider(
                new HttpClient(stub),
                gate,
                configurationService.Object,
                NullLogger<InternetArchiveSearchProvider>.Instance);

            var report = await CreateWorkflow(provider).SearchIndexersDetailedAsync(
                "Alice", SweepContext, indexerIds: null, isAutomaticSearch: true, request: null, CancellationToken.None);
            return Assert.Single(report.Outcomes);
        }

        private static IndexerSearchWorkflow CreateWorkflow(IIndexerSearchProvider provider)
        {
            var indexer = new IndexerBuilder()
                .WithId(IndexerId)
                .WithName("Internet Archive")
                .WithType("DirectDownload")
                .WithImplementation("InternetArchive")
                .WithUrl("https://archive.org")
                .Build();
            var indexerRepository = new Mock<IIndexerRepository>();
            indexerRepository
                .Setup(r => r.GetEnabledAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Indexer> { indexer });

            return new IndexerSearchWorkflow(
                new HttpClient(),
                Mock.Of<IConfigurationService>(),
                indexerRepository.Object,
                new[] { provider },
                new IndexerAdditionalSettingsParser(NullLogger<IndexerAdditionalSettingsParser>.Instance),
                NullLogger<IndexerSearchWorkflow>.Instance);
        }

        private static HttpResponseMessage Ok(string content) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content)
        };

        // Answers the search with the given items and each metadata GET through the delegate,
        // counting every request at the wire.
        private sealed class CountingStub : HttpMessageHandler
        {
            private readonly Func<Uri, HttpResponseMessage> _metadataResponse;
            private readonly string _searchResponse;
            private int _requests;

            public CountingStub(Func<Uri, HttpResponseMessage> metadataResponse, params string[] identifiers)
            {
                _metadataResponse = metadataResponse;
                var docs = identifiers.Select(identifier =>
                    $$"""{"identifier": {{JsonSerializer.Serialize(identifier)}}, "title": "Alice's Adventures in Wonderland", "creator": "Lewis Carroll", "date": "2010-03-01"}""");
                _searchResponse = "{\"response\": {\"docs\": [" + string.Join(",", docs) + "]}}";
            }

            public int Requests => Volatile.Read(ref _requests);

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _requests);
                var uri = request.RequestUri ?? new Uri("about:blank");
                return Task.FromResult(uri.AbsolutePath.Contains("/advancedsearch.php", StringComparison.OrdinalIgnoreCase)
                    ? Ok(_searchResponse)
                    : _metadataResponse(uri));
            }
        }

        private sealed class DeferringProvider : IIndexerSearchProvider
        {
            private readonly RequestDeferral _deferral;

            public DeferringProvider(RequestDeferral deferral)
            {
                _deferral = deferral;
            }

            public string IndexerType => "InternetArchive";

            public Task<IndexerSearchOutcome> SearchAsync(
                Indexer indexer,
                string query,
                string? category,
                SearchRequest? request,
                IndexerSearchContext context,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new IndexerSearchOutcome(
                    indexer.Id, IndexerSearchStatus.Deferred, Array.Empty<IndexerSearchResult>(), RequestsSent: 1, _deferral));
            }
        }
    }
}
