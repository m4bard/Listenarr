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
using Listenarr.Application.Search.Indexers.Common;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Api.Features.Search;

/// <summary>
/// Searching one indexer by id, as the manual search does once per enabled indexer. An indexer the
/// request gate held back, one that failed and one that answered with nothing all came back as an
/// empty list, so the manual search could only ever say "no results". The outcome has to survive to
/// the response for the search to say "could not search" instead, with the cooldown's end when one
/// is behind it.
/// </summary>
/// <remarks>Indexer names and titles are invented.</remarks>
[Trait("Area", "Search")]
[Trait("Name", "SearchByApiCouldNotSearchTests")]
[Trait("Category", "IndexerSearchOutcome")]
public sealed class SearchByApiCouldNotSearchTests : BaseTests
{
    // The controllers' own settings (Startup/ListenarrServiceRegistration.cs, AddJsonOptions): web
    // defaults, enums as strings, and nulls left out. Asserting against anything looser would pass a
    // shape the frontend never receives.
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly DateTime Until = new(2026, 3, 14, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Method", "SearchIndexerOutcomeAsync")]
    [Trait("Scenario", "IndexerDeferred")]
    public async Task SearchIndexerOutcomeAsync_GateDeferredTheSearch_KeepsTheDeferral()
    {
        var deferral = new RequestDeferral(Until, RequestDeferralReason.RateLimited);
        var workflow = CreateWorkflow(indexer => new IndexerSearchOutcome(indexer.Id, IndexerSearchStatus.Deferred, [], 0, deferral));

        var outcome = await workflow.SearchIndexerOutcomeAsync("1", "Alice");
        var bare = await workflow.SearchIndexerResultsAsync("1", "Alice");

        Assert.NotNull(outcome);
        Assert.Equal((IndexerSearchStatus.Deferred, (RequestDeferral?)deferral), (outcome.Status, outcome.Deferral));
        Assert.Empty(bare);
    }

    [Fact]
    [Trait("Method", "SearchIndexerOutcomeAsync")]
    [Trait("Scenario", "IndexerMissing")]
    public async Task SearchIndexerOutcomeAsync_UnknownIndexer_IsNull()
    {
        var workflow = CreateWorkflow(indexer => new IndexerSearchOutcome(indexer.Id, IndexerSearchStatus.Completed, [], 1));

        Assert.Null(await workflow.SearchIndexerOutcomeAsync("2", "Alice"));
    }

    [Fact]
    [Trait("Method", "SearchByApi")]
    [Trait("Scenario", "EnvelopeForADeferredIndexer")]
    public async Task SearchByApi_WithOutcome_DeferredIndexer_SaysNotAnsweredWithReasonAndRetryAfter()
    {
        var body = await CallAsync(
            new IndexerSearchOutcome(1, IndexerSearchStatus.Deferred, [], 0, new RequestDeferral(Until, RequestDeferralReason.RateLimited)),
            includeOutcome: true);

        Assert.False(body.GetProperty("answered").GetBoolean());
        Assert.Equal("RateLimited", body.GetProperty("failureReason").GetString());
        Assert.Equal(Until, body.GetProperty("retryAfterUtc").GetDateTime());
        Assert.Equal(0, body.GetProperty("results").GetArrayLength());
    }

    [Fact]
    [Trait("Method", "SearchByApi")]
    [Trait("Scenario", "EnvelopeForAFailingIndexerInCooldown")]
    public async Task SearchByApi_WithOutcome_FailureLadderCooldown_SaysTheFailureAndRetryAfter()
    {
        var body = await CallAsync(
            new IndexerSearchOutcome(1, IndexerSearchStatus.Failed, [], 0, new RequestDeferral(Until, RequestDeferralReason.FailureLadder), "indexer failing (cooldown)"),
            includeOutcome: true);

        Assert.False(body.GetProperty("answered").GetBoolean());
        Assert.Equal("indexer failing (cooldown)", body.GetProperty("failureReason").GetString());
        Assert.Equal(Until, body.GetProperty("retryAfterUtc").GetDateTime());
    }

    [Theory]
    [Trait("Method", "SearchByApi")]
    [Trait("Scenario", "EnvelopeForAFailedIndexer")]
    [InlineData("timeout", "timeout")]
    [InlineData("no provider", "NotConfigured")]
    public async Task SearchByApi_WithOutcome_FailedIndexer_SaysNotAnsweredAndWhy(string failure, string expected)
    {
        var body = await CallAsync(new IndexerSearchOutcome(1, IndexerSearchStatus.Failed, [], 1, FailureReason: failure), includeOutcome: true);

        Assert.False(body.GetProperty("answered").GetBoolean());
        Assert.Equal(expected, body.GetProperty("failureReason").GetString());
        Assert.False(body.TryGetProperty("retryAfterUtc", out _));
    }

    [Fact]
    [Trait("Method", "SearchByApi")]
    [Trait("Scenario", "EnvelopeForAMissingIndexer")]
    public async Task SearchByApi_WithOutcome_MissingIndexer_SaysNotFound()
    {
        var body = await CallAsync(null, includeOutcome: true);

        Assert.False(body.GetProperty("answered").GetBoolean());
        Assert.Equal("NotFound", body.GetProperty("failureReason").GetString());
    }

    [Fact]
    [Trait("Method", "SearchByApi")]
    [Trait("Scenario", "EnvelopeForAnAnsweredIndexer")]
    public async Task SearchByApi_WithOutcome_AnsweredIndexer_CarriesTheResultsAndNoReason()
    {
        var body = await CallAsync(new IndexerSearchOutcome(1, IndexerSearchStatus.Completed, [Result("Alice in the Orchard")], 1), includeOutcome: true);

        Assert.True(body.GetProperty("answered").GetBoolean());
        Assert.False(body.TryGetProperty("failureReason", out _));
        Assert.False(body.TryGetProperty("retryAfterUtc", out _));
        var result = Assert.Single(body.GetProperty("results").EnumerateArray());
        Assert.Equal("Alice in the Orchard", result.GetProperty("title").GetString());
    }

    [Fact]
    [Trait("Method", "SearchByApi")]
    [Trait("Scenario", "BareListWithoutTheFlag")]
    public async Task SearchByApi_WithoutOutcome_ReturnsTheBareListAsBefore()
    {
        var body = await CallAsync(new IndexerSearchOutcome(1, IndexerSearchStatus.Deferred, [], 0, new RequestDeferral(Until, RequestDeferralReason.RateLimited)), includeOutcome: false);

        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(1, body.GetArrayLength());
    }

    private static async Task<JsonElement> CallAsync(IndexerSearchOutcome? outcome, bool includeOutcome)
    {
        var service = new Mock<ISearchService>();
        service
            .Setup(s => s.SearchIndexerOutcomeAsync("1", "Alice", null, It.IsAny<SearchRequest?>()))
            .ReturnsAsync(outcome);
        service
            .Setup(s => s.SearchIndexerResultsAsync("1", "Alice", null, It.IsAny<SearchRequest?>()))
            .ReturnsAsync(new List<IndexerSearchResult> { Result("Alice in the Orchard") });
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

    private static IndexerSearchWorkflow CreateWorkflow(Func<Indexer, IndexerSearchOutcome> respond)
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

        return new IndexerSearchWorkflow(
            new HttpClient(),
            Mock.Of<IConfigurationService>(),
            repository.Object,
            new[] { new AnsweringProvider(respond) },
            new IndexerAdditionalSettingsParser(NullLogger<IndexerAdditionalSettingsParser>.Instance),
            NullLogger<IndexerSearchWorkflow>.Instance);
    }

    private sealed class AnsweringProvider(Func<Indexer, IndexerSearchOutcome> respond) : IIndexerSearchProvider
    {
        public string IndexerType => "Torznab";

        public Task<IndexerSearchOutcome> SearchAsync(
            Indexer indexer,
            string query,
            string? category,
            SearchRequest? request,
            IndexerSearchContext context,
            CancellationToken cancellationToken) => Task.FromResult(respond(indexer));
    }
}
