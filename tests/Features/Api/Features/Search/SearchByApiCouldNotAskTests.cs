/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System.Text.Json;
using Listenarr.Api.Features.Search;
using Listenarr.Application.Search.Indexers.Common;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Api.Features.Search;

/// <summary>
/// Searching one indexer by id, as the manual search does once per enabled indexer. An indexer
/// that timed out and one that answered with nothing both came back as an empty list, so the
/// manual search could only ever say "no results". The outcome has to survive to the response for
/// the search to say "could not ask" instead, and it has to do so for an indexer that failed beside
/// others that answered, not only when everything came back empty.
/// </summary>
/// <remarks>Indexer names and titles are invented.</remarks>
[Trait("Area", "Search")]
[Trait("Name", "SearchByApiCouldNotAskTests")]
[Trait("Category", "IndexerSearchOutcome")]
public sealed class SearchByApiCouldNotAskTests : BaseTests
{
    // The controllers' own settings (Startup/ListenarrServiceRegistration.cs, AddJsonOptions): web
    // defaults, enums as strings, and nulls left out. Asserting against anything looser would pass a
    // shape the frontend never receives.
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    [Trait("Method", "SearchIndexerObservationAsync")]
    [Trait("Scenario", "IndexerTimedOut")]
    public async Task SearchIndexerObservationAsync_IndexerTimedOut_ReportsUnavailableNotEmpty()
    {
        // Given
        var workflow = CreateWorkflow(_ => IndexerQueryObservation.Unavailable(IndexerQueryReason.Timeout, "Alice", "TaskCanceledException"), out _);

        // When
        var observation = await workflow.SearchIndexerObservationAsync("1", "Alice");

        // Then
        Assert.NotNull(observation);
        Assert.False(observation!.Answered);
        Assert.Equal(IndexerQueryOutcome.Unavailable, observation.Outcome);
        Assert.Equal(IndexerQueryReason.Timeout, observation.Reason);
    }

    [Fact]
    [Trait("Method", "SearchIndexerObservationAsync")]
    [Trait("Scenario", "ProviderThrew")]
    public async Task SearchIndexerObservationAsync_ProviderThrew_ReportsUnavailable()
    {
        var workflow = CreateWorkflow(_ => throw new HttpRequestException("connection refused"), out _);

        var observation = await workflow.SearchIndexerObservationAsync("1", "Alice");

        Assert.NotNull(observation);
        Assert.False(observation!.Answered);
        Assert.Equal(IndexerQueryOutcome.Unavailable, observation.Outcome);
    }

    [Fact]
    [Trait("Method", "SearchIndexerObservationAsync")]
    [Trait("Scenario", "GenuinelyEmpty")]
    public async Task SearchIndexerObservationAsync_IndexerAnsweredWithNothing_IsAnsweredNotAFailure()
    {
        // Known-good: a well-formed empty answer must stay "no results". Reporting it as a failure
        // would collapse the two cases again in the other direction.
        var workflow = CreateWorkflow(_ => IndexerQueryObservation.NoMatch(IndexerQueryReason.EmptyChannel, "Alice"), out _);

        var observation = await workflow.SearchIndexerObservationAsync("1", "Alice");

        Assert.NotNull(observation);
        Assert.True(observation!.Answered);
        Assert.Equal(IndexerQueryOutcome.NoMatch, observation.Outcome);
    }

    [Fact]
    [Trait("Method", "SearchIndexerObservationAsync")]
    [Trait("Scenario", "UnknownIndexer")]
    public async Task SearchIndexerObservationAsync_UnknownIndexer_ReturnsNull()
    {
        var workflow = CreateWorkflow(_ => IndexerQueryObservation.NoMatch(IndexerQueryReason.EmptyChannel, "Alice"), out _);

        var observation = await workflow.SearchIndexerObservationAsync("99", "Alice");

        Assert.Null(observation);
    }

    [Fact]
    [Trait("Method", "SearchIndexerObservationAsync")]
    [Trait("Scenario", "RecordsBackoffOnce")]
    public async Task SearchIndexerObservationAsync_FeedsTheBackoffExactlyOnce()
    {
        // The manual search doubles as the probe that walks a recovered indexer back down the
        // backoff ladder. Recording the answer twice would move it two rungs per search.
        var workflow = CreateWorkflow(_ => IndexerQueryObservation.Unavailable(IndexerQueryReason.Timeout, "Alice"), out var status);

        await workflow.SearchIndexerObservationAsync("1", "Alice");

        status.Verify(
            s => s.RecordAsync(It.IsAny<Indexer>(), It.IsAny<IndexerQueryObservation>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Method", "SearchByApi")]
    [Trait("Scenario", "EnvelopeForAFailedIndexer")]
    public async Task SearchByApi_WithOutcome_IndexerTimedOut_SaysItWasNotAnswered()
    {
        // Given
        var service = new Mock<ISearchService>();
        service
            .Setup(s => s.SearchIndexerObservationAsync("1", "Alice", null, It.IsAny<SearchRequest?>()))
            .ReturnsAsync(IndexerQueryObservation.Unavailable(IndexerQueryReason.Timeout, "Alice"));

        // When
        var body = await CallAsync(service, includeOutcome: true);

        // Then
        Assert.False(body.GetProperty("answered").GetBoolean());
        Assert.Equal("Timeout", body.GetProperty("failureReason").GetString());
        Assert.Equal(0, body.GetProperty("results").GetArrayLength());
    }

    [Fact]
    [Trait("Method", "SearchByApi")]
    [Trait("Scenario", "EnvelopeForAHit")]
    public async Task SearchByApi_WithOutcome_IndexerAnswered_CarriesResultsAndNoFailure()
    {
        var service = new Mock<ISearchService>();
        service
            .Setup(s => s.SearchIndexerObservationAsync("1", "Alice", null, It.IsAny<SearchRequest?>()))
            .ReturnsAsync(IndexerQueryObservation.FromResults(new List<IndexerSearchResult> { Result("Alice in the Orchard") }, "Alice"));

        var body = await CallAsync(service, includeOutcome: true);

        Assert.True(body.GetProperty("answered").GetBoolean());
        // Null, so left out entirely under the app's settings
        Assert.False(body.TryGetProperty("failureReason", out _));
        var result = Assert.Single(body.GetProperty("results").EnumerateArray());
        Assert.Equal("Alice in the Orchard", result.GetProperty("title").GetString());
    }

    [Fact]
    [Trait("Method", "SearchByApi")]
    [Trait("Scenario", "EnvelopeForAnUnknownIndexer")]
    public async Task SearchByApi_WithOutcome_UnknownIndexer_IsNotAnswered()
    {
        var service = new Mock<ISearchService>();
        service
            .Setup(s => s.SearchIndexerObservationAsync("1", "Alice", null, It.IsAny<SearchRequest?>()))
            .ReturnsAsync((IndexerQueryObservation?)null);

        var body = await CallAsync(service, includeOutcome: true);

        Assert.False(body.GetProperty("answered").GetBoolean());
        Assert.Equal("NotFound", body.GetProperty("failureReason").GetString());
    }

    [Fact]
    [Trait("Method", "SearchByApi")]
    [Trait("Scenario", "EnvelopeForAnUnusableIndexer")]
    public async Task SearchByApi_WithOutcome_NoProviderForTheIndexer_SaysNotConfigured()
    {
        // The request was never sent, so the reason worth showing is the outcome, not the
        // provider-lookup detail behind it.
        var service = new Mock<ISearchService>();
        service
            .Setup(s => s.SearchIndexerObservationAsync("1", "Alice", null, It.IsAny<SearchRequest?>()))
            .ReturnsAsync(IndexerQueryObservation.NotConfigured(IndexerQueryReason.NoProviderForImplementation, "Alice"));

        var body = await CallAsync(service, includeOutcome: true);

        Assert.False(body.GetProperty("answered").GetBoolean());
        Assert.Equal("NotConfigured", body.GetProperty("failureReason").GetString());
    }

    [Fact]
    [Trait("Method", "SearchByApi")]
    [Trait("Scenario", "EnvelopeWithoutAReason")]
    public async Task SearchByApi_WithOutcome_FailureWithNoRecordedReason_OmitsTheReason()
    {
        // "None" is not a reason; sending it would put "(None)" in front of the operator.
        var service = new Mock<ISearchService>();
        service
            .Setup(s => s.SearchIndexerObservationAsync("1", "Alice", null, It.IsAny<SearchRequest?>()))
            .ReturnsAsync(IndexerQueryObservation.Unavailable(IndexerQueryReason.None, "Alice"));

        var body = await CallAsync(service, includeOutcome: true);

        Assert.False(body.GetProperty("answered").GetBoolean());
        Assert.False(body.TryGetProperty("failureReason", out _));
    }

    [Fact]
    [Trait("Method", "SearchByApi")]
    [Trait("Scenario", "LegacyShapeUnchanged")]
    public async Task SearchByApi_WithoutOutcome_StillReturnsTheBareList()
    {
        // Control: a caller that did not ask for the envelope gets exactly what it always got.
        var service = new Mock<ISearchService>();
        service
            .Setup(s => s.SearchIndexerResultsAsync("1", "Alice", null, It.IsAny<SearchRequest?>()))
            .ReturnsAsync(new List<IndexerSearchResult> { Result("Alice in the Orchard") });

        var body = await CallAsync(service, includeOutcome: false);

        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(1, body.GetArrayLength());
    }

    private static async Task<JsonElement> CallAsync(Mock<ISearchService> service, bool includeOutcome)
    {
        var controller = new SearchController(
            service.Object,
            NullLogger<SearchController>.Instance,
            new Mock<AudibleService>(new HttpClient(), NullLogger<AudibleService>.Instance).Object,
            Mock.Of<IAudiobookMetadataService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var response = await controller.SearchByApi("1", "Alice", includeOutcome: includeOutcome);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        return JsonSerializer.SerializeToElement(ok.Value, ok.Value!.GetType(), Web);
    }

    private static IndexerSearchResult Result(string title) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Title = title,
        Source = "Alderbrook",
        Size = 1024,
        TorrentUrl = "https://indexer.invalid/download/1"
    };

    private static IndexerSearchWorkflow CreateWorkflow(
        Func<Indexer, IndexerQueryObservation> respond,
        out Mock<IIndexerStatusService> status)
    {
        var indexer = new IndexerBuilder()
            .WithId(1)
            .WithName("Alderbrook")
            .WithType("Torrent")
            .WithImplementation("Torznab")
            .WithUrl("https://indexer.invalid")
            .WithEnabled()
            .Build();

        var repository = new Mock<IIndexerRepository>();
        repository
            .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexer);

        status = new Mock<IIndexerStatusService>();
        status
            .Setup(s => s.RecordAsync(It.IsAny<Indexer>(), It.IsAny<IndexerQueryObservation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IndexerBackoffState.Healthy);

        return new IndexerSearchWorkflow(
            new HttpClient(),
            Mock.Of<IConfigurationService>(),
            repository.Object,
            new[] { new AnsweringProvider(respond) },
            new IndexerAdditionalSettingsParser(NullLogger<IndexerAdditionalSettingsParser>.Instance),
            NullLogger<IndexerSearchWorkflow>.Instance,
            indexerStatusService: status.Object);
    }

    private sealed class AnsweringProvider(Func<Indexer, IndexerQueryObservation> respond) : IIndexerSearchProvider
    {
        public string IndexerType => "Torznab";

        public Task<IndexerQueryObservation> SearchAsync(
            Indexer indexer,
            string query,
            string? category = null,
            SearchRequest? request = null,
            CancellationToken ct = default) => Task.FromResult(respond(indexer));
    }
}
