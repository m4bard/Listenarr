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
[Trait("Name", "IndexerSearchWorkflowOutcomeTests")]
[Trait("Category", "IndexerSearchWorkflow")]
public sealed class IndexerSearchWorkflowOutcomeTests : BaseTests
{
    [Fact]
    [Trait("Method", "SearchIndexersAsync")]
    [Trait("Scenario", "OneIndexerThrows")]
    public async Task SearchIndexersAsync_OneIndexerThrows_SiblingIndexerStillReturnsResults()
    {
        // Given: a fan-out over two indexers where the first one's provider throws
        var provider = new RecordingSearchProvider(indexer =>
            indexer.Name == "Throwing"
                ? throw new HttpRequestException("connection refused")
                : IndexerQueryObservation.FromResults(
                    new List<IndexerSearchResult> { CreateResult("Alice from Healthy") },
                    "Alice"));

        var workflow = CreateWorkflow(provider, CreateIndexer(1, "Throwing"), CreateIndexer(2, "Healthy"));

        // When
        var results = await workflow.SearchIndexersAsync("Alice");

        // Then: containment holds, and the healthy sibling's result survives
        var result = Assert.Single(results);
        Assert.Equal("Alice from Healthy", result.Title);
        Assert.Equal(2, provider.QueriedIndexers.Count);
    }

    [Fact]
    [Trait("Method", "SearchIndexersAsync")]
    [Trait("Scenario", "OneIndexerUnavailable")]
    public async Task SearchIndexersAsync_OneIndexerUnavailable_SiblingIndexerStillReturnsResults()
    {
        // Given: an indexer that answers with nothing usable, beside one that answers with a result
        var provider = new RecordingSearchProvider(indexer =>
            indexer.Name == "Unavailable"
                ? IndexerQueryObservation.Unavailable(IndexerQueryReason.Timeout, "Alice", "TaskCanceledException")
                : IndexerQueryObservation.FromResults(
                    new List<IndexerSearchResult> { CreateResult("Alice from Healthy") },
                    "Alice"));

        var workflow = CreateWorkflow(provider, CreateIndexer(1, "Unavailable"), CreateIndexer(2, "Healthy"));

        // When
        var results = await workflow.SearchIndexersAsync("Alice");

        // Then
        var result = Assert.Single(results);
        Assert.Equal("Alice from Healthy", result.Title);
    }

    [Fact]
    [Trait("Method", "SearchIndexersAsync")]
    [Trait("Scenario", "NoMatch")]
    public async Task SearchIndexersAsync_IndexerAnswersWithNothing_IssuesOneQueryOnly()
    {
        // Given: no ladder exists yet, so a miss must still cost exactly one request per indexer
        var provider = new RecordingSearchProvider(_ =>
            IndexerQueryObservation.NoMatch(IndexerQueryReason.EmptyChannel, "Alice"));

        var workflow = CreateWorkflow(provider, CreateIndexer(1, "Torznab One"));

        // When
        var results = await workflow.SearchIndexersAsync("Alice");

        // Then
        Assert.Empty(results);
        Assert.Single(provider.QueriedIndexers);
    }

    [Fact]
    [Trait("Method", "SearchIndexerResultsAsync")]
    [Trait("Scenario", "ResultsReturned")]
    public async Task SearchIndexerResultsAsync_ProviderReturnsHit_UnwrapsResults()
    {
        // Given
        var provider = new RecordingSearchProvider(_ =>
            IndexerQueryObservation.FromResults(
                new List<IndexerSearchResult> { CreateResult("Alice") },
                "Alice"));

        var indexer = CreateIndexer(1, "Torznab One");
        var workflow = CreateWorkflow(provider, indexer);

        // When
        var results = await workflow.SearchIndexerResultsAsync("1", "Alice");

        // Then
        Assert.Single(results);
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
            new[] { provider },
            new IndexerAdditionalSettingsParser(NullLogger<IndexerAdditionalSettingsParser>.Instance),
            NullLogger<IndexerSearchWorkflow>.Instance);
    }

    /// <summary>
    /// Records which indexers were asked, and answers with whatever the test dictates. The hook the
    /// tiered ladder will need, and enough today to prove the fan-out still contains one bad indexer.
    /// </summary>
    private sealed class RecordingSearchProvider : IIndexerSearchProvider
    {
        private readonly Func<Indexer, IndexerQueryObservation> _respond;

        public RecordingSearchProvider(Func<Indexer, IndexerQueryObservation> respond) => _respond = respond;

        public List<string> QueriedIndexers { get; } = new();

        public string IndexerType => "Torznab";

        public Task<IndexerQueryObservation> SearchAsync(
            Indexer indexer,
            string query,
            string? category = null,
            SearchRequest? request = null)
        {
            lock (QueriedIndexers)
            {
                QueriedIndexers.Add(indexer.Name);
            }

            return Task.FromResult(_respond(indexer));
        }
    }
}
