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

/// <summary>
/// The selection-time half of the failure backoff: which indexers the fan-out declines to ask, and
/// which outcomes it hands back to the ladder.
/// </summary>
[Trait("Area", "Search")]
[Trait("Name", "IndexerSearchWorkflowBackoffTests")]
[Trait("Category", "IndexerSearchWorkflow")]
public sealed class IndexerSearchWorkflowBackoffTests : BaseTests
{
    [Fact]
    [Trait("Method", "SearchIndexersAsync")]
    [Trait("Scenario", "BlockedIndexerSkipped")]
    public async Task SearchIndexersAsync_BlockedIndexer_IsNeverAsked()
    {
        // Given
        var provider = new RecordingSearchProvider(_ => Hit("Alice"));
        var status = new FakeIndexerStatusService { Blocked = { 1 } };
        var workflow = CreateWorkflow(provider, status, Indexer(1, "Blocked"), Indexer(2, "Healthy"));

        // When
        var results = await workflow.SearchIndexersAsync("Alice");

        // Then: the whole point of the feature. The blocked indexer costs no request at all, and
        // the healthy one is unaffected.
        Assert.Equal(["Healthy"], provider.QueriedIndexers);
        Assert.Single(results);
    }

    [Fact]
    [Trait("Method", "SearchIndexersAsync")]
    [Trait("Scenario", "AllBlocked")]
    public async Task SearchIndexersAsync_EveryIndexerBlocked_ReturnsEmptyNotFabricatedResults()
    {
        // Given
        var provider = new RecordingSearchProvider(_ => Hit("Alice"));
        var status = new FakeIndexerStatusService { Blocked = { 1, 2 } };
        var workflow = CreateWorkflow(provider, status, Indexer(1, "One"), Indexer(2, "Two"));

        // When
        var results = await workflow.SearchIndexersAsync("Alice");

        // Then: the mock-results branch fires when nothing is CONFIGURED. Reaching it because every
        // configured indexer happens to be in cooldown would put five invented releases in front of
        // the automatic-search scorer, and from there into a grab.
        Assert.Empty(results);
        Assert.Empty(provider.QueriedIndexers);
    }

    [Fact]
    [Trait("Method", "SearchIndexersAsync")]
    [Trait("Scenario", "NoneConfigured")]
    public async Task SearchIndexersAsync_NoIndexersConfigured_StillReturnsMockResults()
    {
        // Given: the control for the test above. Without it, "returns empty" and "the mock branch
        // is broken" look the same, and the behaviour this feature must not change looks changed.
        var provider = new RecordingSearchProvider(_ => Hit("Alice"));
        var workflow = CreateWorkflow(provider, new FakeIndexerStatusService());

        // When
        var results = await workflow.SearchIndexersAsync("Alice");

        // Then
        Assert.NotEmpty(results);
        Assert.Empty(provider.QueriedIndexers);
    }

    [Fact]
    [Trait("Method", "SearchIndexersAsync")]
    [Trait("Scenario", "FilterDisabled")]
    public async Task SearchIndexersAsync_WithFilteringOff_AsksTheBlockedIndexerAnyway()
    {
        // Given
        var provider = new RecordingSearchProvider(_ => Hit("Alice"));
        var status = new FakeIndexerStatusService { Blocked = { 1 } };
        var workflow = CreateWorkflow(provider, status, Indexer(1, "Blocked"));

        // When
        var results = await workflow.SearchIndexersAsync("Alice", filterBlocked: false);

        // Then
        Assert.Equal(["Blocked"], provider.QueriedIndexers);
        Assert.Single(results);
    }

    [Fact]
    [Trait("Method", "SearchIndexersAsync")]
    [Trait("Scenario", "OutcomesRecorded")]
    public async Task SearchIndexersAsync_RecordsOneOutcomePerIndexerAsked()
    {
        // Given
        var provider = new RecordingSearchProvider(indexer =>
            indexer.Name == "Failing"
                ? IndexerQueryObservation.Unavailable(IndexerQueryReason.Timeout, "Alice", "TaskCanceledException")
                : Hit("Alice"));
        var status = new FakeIndexerStatusService();
        var workflow = CreateWorkflow(provider, status, Indexer(1, "Failing"), Indexer(2, "Healthy"));

        // When
        await workflow.SearchIndexersAsync("Alice");

        // Then
        Assert.Equal(2, status.Recorded.Count);
        Assert.Equal(
            IndexerQueryReason.Timeout,
            status.Recorded.Single(r => r.Indexer.Name == "Failing").Observation.Reason);
        Assert.Equal(
            IndexerQueryOutcome.Hit,
            status.Recorded.Single(r => r.Indexer.Name == "Healthy").Observation.Outcome);
    }

    [Fact]
    [Trait("Method", "SearchIndexersAsync")]
    [Trait("Scenario", "TimeoutRecorded")]
    public async Task SearchIndexersAsync_OneIndexerTimesOut_TheOtherStillReturnsAndTheTimeoutIsRecorded()
    {
        // Given: the regression test for the failure mode the whole feature exists to notice. An
        // HttpClient timeout arrives as a TaskCanceledException wrapping a TimeoutException.
        var provider = new RecordingSearchProvider(indexer =>
            indexer.Name == "Slow"
                ? throw new TaskCanceledException("timed out", new TimeoutException())
                : IndexerQueryObservation.FromResults(
                    [Result("First"), Result("Second")],
                    "Alice"));
        var status = new FakeIndexerStatusService();
        var workflow = CreateWorkflow(provider, status, Indexer(1, "Slow"), Indexer(2, "Healthy"));

        // When
        var results = await workflow.SearchIndexersAsync("Alice");

        // Then
        Assert.Equal(2, results.Count);
        var recorded = status.Recorded.Single(r => r.Indexer.Name == "Slow");
        Assert.Equal(IndexerQueryOutcome.Unavailable, recorded.Observation.Outcome);
        Assert.Equal(IndexerQueryReason.Timeout, recorded.Observation.Reason);
    }

    [Fact]
    [Trait("Method", "SearchIndexersAsync")]
    [Trait("Scenario", "StatusWriteFails")]
    public async Task SearchIndexersAsync_StatusWriteThrows_SearchStillReturnsResults()
    {
        // Given
        var provider = new RecordingSearchProvider(_ => Hit("Alice"));
        var status = new FakeIndexerStatusService { ThrowOnRecord = true };
        var workflow = CreateWorkflow(provider, status, Indexer(1, "Healthy"));

        // When
        var results = await workflow.SearchIndexersAsync("Alice");

        // Then: bookkeeping about an indexer must never be able to lose a search that worked.
        Assert.Single(results);
    }

    [Fact]
    [Trait("Method", "SearchIndexerResultsAsync")]
    [Trait("Scenario", "OperatorOverride")]
    public async Task SearchIndexerResultsAsync_BlockedIndexerNamedExplicitly_IsStillAskedAndRecorded()
    {
        // Given
        var provider = new RecordingSearchProvider(_ => Hit("Alice"));
        var status = new FakeIndexerStatusService { Blocked = { 1 } };
        var workflow = CreateWorkflow(provider, status, Indexer(1, "Blocked"));

        // When
        var results = await workflow.SearchIndexerResultsAsync("1", "Alice");

        // Then: an operator naming one indexer has overridden the policy by asking, and the answer
        // is recorded so the request doubles as the probe that walks a recovered indexer back down.
        Assert.Single(results);
        Assert.Equal(["Blocked"], provider.QueriedIndexers);
        Assert.Single(status.Recorded);
    }

    [Fact]
    [Trait("Method", "SearchByApiAsync")]
    [Trait("Scenario", "OperatorOverride")]
    public async Task SearchByApiAsync_BlockedIndexerNamedExplicitly_IsStillAsked()
    {
        // Given
        var provider = new RecordingSearchProvider(_ => Hit("Alice"));
        var status = new FakeIndexerStatusService { Blocked = { 1 } };
        var workflow = CreateWorkflow(provider, status, Indexer(1, "Blocked"));

        // When
        var results = await workflow.SearchByApiAsync("1", "Alice");

        // Then
        Assert.Single(results);
        Assert.Equal(["Blocked"], provider.QueriedIndexers);
        Assert.Single(status.Recorded);
    }

    private static IndexerQueryObservation Hit(string title) =>
        IndexerQueryObservation.FromResults([Result(title)], title);

    private static IndexerSearchResult Result(string title) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Title = title,
        Source = "Fake"
    };

    private static Indexer Indexer(int id, string name) =>
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
        IIndexerStatusService statusService,
        params Indexer[] indexers)
    {
        var indexerRepository = new Mock<IIndexerRepository>();
        indexerRepository
            .Setup(repository => repository.GetEnabledAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexers.ToList());

        foreach (var indexer in indexers)
        {
            indexerRepository
                .Setup(repository => repository.GetByIdAsync(indexer.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(indexer);
        }

        return new IndexerSearchWorkflow(
            new HttpClient(),
            Mock.Of<IConfigurationService>(),
            indexerRepository.Object,
            [provider],
            new IndexerAdditionalSettingsParser(NullLogger<IndexerAdditionalSettingsParser>.Instance),
            NullLogger<IndexerSearchWorkflow>.Instance,
            indexerStatusService: statusService);
    }

    private sealed class FakeIndexerStatusService : IIndexerStatusService
    {
        public HashSet<int> Blocked { get; } = [];

        public List<(Indexer Indexer, IndexerQueryObservation Observation)> Recorded { get; } = [];

        public bool ThrowOnRecord { get; init; }

        public Task<IReadOnlySet<int>> GetBlockedIndexerIdsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<int>>(Blocked);

        public Task<bool> AnyEnabledIndexerBlockedAsync(bool isAutomaticSearch, CancellationToken ct = default) =>
            Task.FromResult(Blocked.Count > 0);

        public Task<IndexerBackoffState> RecordAsync(
            Indexer indexer,
            IndexerQueryObservation observation,
            CancellationToken ct = default)
        {
            if (ThrowOnRecord)
            {
                throw new InvalidOperationException("database is locked");
            }

            lock (Recorded)
            {
                Recorded.Add((indexer, observation));
            }

            return Task.FromResult(IndexerBackoffState.Healthy);
        }
    }

    private sealed class RecordingSearchProvider(Func<Indexer, IndexerQueryObservation> respond) : IIndexerSearchProvider
    {
        public List<string> QueriedIndexers { get; } = [];

        public string IndexerType => "Torznab";

        public Task<IndexerQueryObservation> SearchAsync(
            Indexer indexer,
            string query,
            string? category = null,
            SearchRequest? request = null,
            CancellationToken ct = default)
        {
            lock (QueriedIndexers)
            {
                QueriedIndexers.Add(indexer.Name);
            }

            return Task.FromResult(respond(indexer));
        }
    }
}
