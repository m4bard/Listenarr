/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Search.Indexers;

[Trait("Area", "Search")]
[Trait("Name", "IndexerSearchWorkflowLadderTests")]
[Trait("Category", "IndexerSearchWorkflow")]
public sealed class IndexerSearchWorkflowLadderTests : BaseTests
{
    /// <summary>
    /// The three-rung plan these tests walk, stated rather than derived.
    /// </summary>
    /// <remarks>
    /// These tests are about what the workflow does with a plan, not about which rungs
    /// <see cref="AudiobookSearchQueryBuilder"/> decides an audiobook deserves. Building the plan
    /// here keeps the two apart, so a change to the builder's rung policy shows up in the builder's
    /// own tests instead of silently shortening the ladder these ones need. It was derived before:
    /// gating the bare-title rung took this fixture from three rungs to two and broke four tests
    /// that had nothing to say about gating.
    /// </remarks>
    private static SearchQueryPlan LadderPlan() =>
        new(new[]
        {
            new SearchQueryForm(1, "Heaven's River Dennis E Taylor", SearchQueryFormKind.TitleAuthor),
            new SearchQueryForm(2, "Heaven's River", SearchQueryFormKind.Title),
            new SearchQueryForm(3, "Bobiverse Dennis E Taylor", SearchQueryFormKind.SeriesAuthor)
        });

    [Fact]
    [Trait("Method", "SearchIndexersAsync")]
    [Trait("Scenario", "Tier1Hits")]
    public async Task SearchIndexersAsync_Tier1Hits_IssuesOneQueryOnly()
    {
        // Given: an indexer that finds the book on the first form
        var provider = new RecordingSearchProvider(query =>
            IndexerQueryObservation.FromResults(new List<IndexerSearchResult> { CreateResult("hit") }, query));

        var plan = LadderPlan();
        var workflow = CreateWorkflow(provider, CreateIndexer(1, "Torznab One"));

        // When
        var results = await workflow.SearchIndexersAsync(plan.PrimaryQuery, plan: plan);

        // Then: a search that already worked costs exactly what it cost before the ladder existed
        Assert.Single(results);
        Assert.Single(provider.Queries);
    }

    [Fact]
    [Trait("Method", "SearchIndexersAsync")]
    [Trait("Scenario", "Tier1NoMatch")]
    public async Task SearchIndexersAsync_Tier1NoMatch_EscalatesToTier2()
    {
        // Given: the author narrows the query past the indexer's own title
        var provider = new RecordingSearchProvider(query =>
            query.Contains("Taylor", StringComparison.OrdinalIgnoreCase)
                ? IndexerQueryObservation.NoMatch(IndexerQueryReason.EmptyChannel, query)
                : IndexerQueryObservation.FromResults(new List<IndexerSearchResult> { CreateResult("hit") }, query));

        var plan = LadderPlan();
        var workflow = CreateWorkflow(provider, CreateIndexer(1, "Torznab One"));

        // When
        var results = await workflow.SearchIndexersAsync(plan.PrimaryQuery, plan: plan);

        // Then
        Assert.Single(results);
        Assert.Equal(new[] { "Heavens River Dennis E Taylor", "Heavens River" }, provider.Queries.ToArray());
    }

    [Fact]
    [Trait("Method", "RunQueryPlanAsync")]
    [Trait("Scenario", "TitleFormsNoMatch")]
    public async Task RunQueryPlanAsync_TitleFormsNoMatch_RecoversViaSeriesTier()
    {
        // Given: the mechanism the finding measured, faked rather than fetched. Every form built
        // from the title carries the token the indexer cannot match, so only the series can recover it.
        var provider = new RecordingSearchProvider(query =>
            query.Contains("Heavens", StringComparison.OrdinalIgnoreCase)
                ? IndexerQueryObservation.NoMatch(IndexerQueryReason.EmptyChannel, query)
                : IndexerQueryObservation.FromResults(new List<IndexerSearchResult> { CreateResult("hit") }, query));

        var plan = LadderPlan();
        var indexer = CreateIndexer(1, "Torznab One");
        var workflow = CreateWorkflow(provider, indexer);

        // When
        var observation = await workflow.RunQueryPlanAsync(indexer, plan, null, null, CancellationToken.None);

        // Then: tiers 1 to 3 in order, and there is no tier 4 to escalate to once tier 3 answers
        Assert.Equal(
            new[] { "Heavens River Dennis E Taylor", "Heavens River", "Bobiverse Dennis E Taylor" },
            provider.Queries.ToArray());
        Assert.Equal(IndexerQueryOutcome.Hit, observation.Outcome);
        Assert.Equal(3, observation.Tier);
    }

    [Fact]
    [Trait("Method", "RunQueryPlanAsync")]
    [Trait("Scenario", "AllTiersNoMatch")]
    public async Task RunQueryPlanAsync_AllTiersNoMatch_ReportsExhausted()
    {
        // Given: an indexer that answers every form, and has none of them
        var provider = new RecordingSearchProvider(query =>
            IndexerQueryObservation.NoMatch(IndexerQueryReason.EmptyChannel, query));

        var plan = LadderPlan();
        var indexer = CreateIndexer(1, "Torznab One");
        var workflow = CreateWorkflow(provider, indexer);

        // When
        var observation = await workflow.RunQueryPlanAsync(indexer, plan, null, null, CancellationToken.None);

        // Then
        Assert.Equal(3, provider.Queries.Count);
        Assert.Equal(IndexerQueryOutcome.NoMatch, observation.Outcome);
        Assert.Equal(3, observation.Tier);
    }

    [Fact]
    [Trait("Method", "SearchIndexersAsync")]
    [Trait("Scenario", "IndexerUnavailable")]
    public async Task SearchIndexersAsync_IndexerUnavailable_DoesNotEscalate()
    {
        // Given: an indexer that is timing out rather than answering
        var provider = new RecordingSearchProvider(query =>
            IndexerQueryObservation.Unavailable(IndexerQueryReason.Timeout, query, "TaskCanceledException"));

        var plan = LadderPlan();
        var workflow = CreateWorkflow(provider, CreateIndexer(1, "Torznab One"));

        // When
        var results = await workflow.SearchIndexersAsync(plan.PrimaryQuery, plan: plan);

        // Then: the safety property. Answering a failing indexer with three more requests per book,
        // across a sweep of a whole library, is how an install gets blocked at the network level.
        Assert.Empty(results);
        Assert.Single(provider.Queries);
    }

    [Theory]
    [Trait("Method", "RunQueryPlanAsync")]
    [Trait("Scenario", "IndexerDidNotAnswer")]
    [InlineData(IndexerQueryOutcome.Unavailable, IndexerQueryReason.Timeout)]
    [InlineData(IndexerQueryOutcome.Unavailable, IndexerQueryReason.HttpStatus)]
    [InlineData(IndexerQueryOutcome.Unavailable, IndexerQueryReason.NetworkError)]
    [InlineData(IndexerQueryOutcome.Unreadable, IndexerQueryReason.MalformedXml)]
    [InlineData(IndexerQueryOutcome.NotConfigured, IndexerQueryReason.NoProviderForImplementation)]
    public async Task RunQueryPlanAsync_IndexerDidNotAnswer_StopsAtTheFirstFormAndKeepsTheReason(
        IndexerQueryOutcome outcome,
        IndexerQueryReason reason)
    {
        // Given: every way of not getting a usable answer. None of them is a tier miss, and a
        // different q would not fix any of them.
        var provider = new RecordingSearchProvider(query => outcome switch
        {
            IndexerQueryOutcome.Unreadable => IndexerQueryObservation.Unreadable(reason, query),
            IndexerQueryOutcome.NotConfigured => IndexerQueryObservation.NotConfigured(reason, query),
            _ => IndexerQueryObservation.Unavailable(reason, query)
        });

        var plan = LadderPlan();
        var indexer = CreateIndexer(1, "Torznab One");
        var workflow = CreateWorkflow(provider, indexer);

        // When
        var observation = await workflow.RunQueryPlanAsync(indexer, plan, null, null, CancellationToken.None);

        // Then: this is the test that fails if ShouldEscalate is ever "simplified" to an empty count
        Assert.Single(provider.Queries);
        Assert.Equal(outcome, observation.Outcome);
        Assert.Equal(reason, observation.Reason);
        Assert.False(observation.ShouldEscalate);
    }

    [Fact]
    [Trait("Method", "SearchIndexersAsync")]
    [Trait("Scenario", "OneIndexerThrows")]
    public async Task SearchIndexersAsync_OneIndexerFails_OthersStillLadder()
    {
        // Given: a throwing indexer beside one that only has the book under its series
        var provider = new PerIndexerRecordingSearchProvider((indexer, query) =>
        {
            if (indexer.Name == "Throwing")
            {
                throw new HttpRequestException("connection refused");
            }

            return query.Contains("Heavens", StringComparison.OrdinalIgnoreCase)
                ? IndexerQueryObservation.NoMatch(IndexerQueryReason.EmptyChannel, query)
                : IndexerQueryObservation.FromResults(new List<IndexerSearchResult> { CreateResult("hit") }, query);
        });

        var plan = LadderPlan();
        var workflow = CreateWorkflow(provider, CreateIndexer(1, "Throwing"), CreateIndexer(2, "Healthy"));

        // When
        var results = await workflow.SearchIndexersAsync(plan.PrimaryQuery, plan: plan);

        // Then: containment is not a tier miss. The thrower is abandoned after one request and the
        // healthy sibling still walks down to the form that finds the book.
        Assert.Single(results);
        Assert.Single(provider.QueriesFor("Throwing"));
        Assert.Equal(
            new[] { "Heavens River Dennis E Taylor", "Heavens River", "Bobiverse Dennis E Taylor" },
            provider.QueriesFor("Healthy").ToArray());
    }

    [Fact]
    [Trait("Method", "SearchIndexersAsync")]
    [Trait("Scenario", "VerbatimQuery")]
    public async Task SearchIndexersAsync_VerbatimQuery_IssuesOneQueryOnly()
    {
        // Given: free-text search, where there is no record to build further forms from
        var provider = new RecordingSearchProvider(query =>
            IndexerQueryObservation.NoMatch(IndexerQueryReason.EmptyChannel, query));

        var workflow = CreateWorkflow(provider, CreateIndexer(1, "Torznab One"));

        // When: no plan, exactly as the manual search endpoints call it
        var results = await workflow.SearchIndexersAsync("heavens river");

        // Then: one request, byte for byte what the operator typed
        Assert.Empty(results);
        Assert.Equal(new[] { "heavens river" }, provider.Queries.ToArray());
    }

    private static IndexerSearchResult CreateResult(string title) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Title = title,
        Source = "Fake"
    };

    private static Indexer CreateIndexer(int id, string name) =>
        new IndexerBuilder()
            .WithId(id)
            .WithName(name)
            .WithType("Torrent")
            .WithImplementation("Torznab")
            .WithUrl("https://indexer.invalid")
            .WithEnabled()
            .Build();

    private static IndexerSearchWorkflow CreateWorkflow(
        IIndexerSearchProvider provider,
        params Indexer[] indexers)
    {
        var indexerRepository = new Mock<IIndexerRepository>();
        indexerRepository
            .Setup(repository => repository.GetEnabledAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexers.ToList());

        return new IndexerSearchWorkflow(
            new HttpClient(),
            Mock.Of<IConfigurationService>(),
            indexerRepository.Object,
            new[] { provider },
            new IndexerAdditionalSettingsParser(NullLogger<IndexerAdditionalSettingsParser>.Instance),
            NullLogger<IndexerSearchWorkflow>.Instance);
    }

    /// <summary>
    /// Records the query strings one indexer was asked, in order, and answers with whatever the
    /// test dictates. The queries recorded are post-sanitizer, which is what reaches the wire.
    /// </summary>
    private sealed class RecordingSearchProvider : IIndexerSearchProvider
    {
        private readonly Func<string, IndexerQueryObservation> _respond;

        public RecordingSearchProvider(Func<string, IndexerQueryObservation> respond) => _respond = respond;

        public List<string> Queries { get; } = new();

        public string IndexerType => "Torznab";

        public Task<IndexerQueryObservation> SearchAsync(
            Indexer indexer,
            string query,
            string? category = null,
            SearchRequest? request = null,
            CancellationToken ct = default)
        {
            lock (Queries)
            {
                Queries.Add(query);
            }

            return Task.FromResult(_respond(query));
        }
    }

    /// <summary>
    /// The same, keeping each indexer's queries apart so a fan-out test can assert that one
    /// indexer's ladder is unaffected by another's failure.
    /// </summary>
    private sealed class PerIndexerRecordingSearchProvider : IIndexerSearchProvider
    {
        private readonly Func<Indexer, string, IndexerQueryObservation> _respond;
        private readonly Dictionary<string, List<string>> _queries = new();

        public PerIndexerRecordingSearchProvider(Func<Indexer, string, IndexerQueryObservation> respond) =>
            _respond = respond;

        public string IndexerType => "Torznab";

        public IReadOnlyList<string> QueriesFor(string indexerName)
        {
            lock (_queries)
            {
                return _queries.TryGetValue(indexerName, out var recorded)
                    ? recorded.ToList()
                    : new List<string>();
            }
        }

        public Task<IndexerQueryObservation> SearchAsync(
            Indexer indexer,
            string query,
            string? category = null,
            SearchRequest? request = null,
            CancellationToken ct = default)
        {
            lock (_queries)
            {
                if (!_queries.TryGetValue(indexer.Name, out var recorded))
                {
                    recorded = new List<string>();
                    _queries[indexer.Name] = recorded;
                }

                recorded.Add(query);
            }

            return Task.FromResult(_respond(indexer, query));
        }
    }
}
