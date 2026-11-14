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

using System.Net;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestGate.IndexerRequestGateHarness;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestGate.ProviderWireFixture;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestGate;

/// <summary>
/// Q-T5: every request that reaches the wire is one gate call and one charge against the cycle
/// bound, however many requests a single search makes. The wire stub counts independently of
/// the gate.
/// </summary>
[Trait("Name", "IndexerRequestAccountingTests")]
[Trait("Category", "IndexerRequestGate")]
public sealed class IndexerRequestAccountingTests : BaseTests
{
    private const string AudioMetadata = """
{
  "metadata": { "language": "English" },
  "files": [ { "name": "sample.m4b", "format": "LibriVox Apple Audiobook", "size": "900" } ]
}
""";

    [Fact]
    public async Task EveryWireRequestCounts_InternetArchive_ThreeVariantsAndFiveItems_IsEight()
    {
        // Given: a search planned as three query variants that each list the same five items
        using var harness = new IndexerRequestGateHarness(new RecordingStateStore(), InternetArchiveWire(itemCount: 5));
        var gate = new CountingGate(harness.Gate);
        var allowance = new CycleRequestAllowance(50);
        using var client = new HttpClient(harness.Handler, disposeHandler: false);
        var provider = new InternetArchiveSearchProvider(client, gate, ArchiveSettings(), NullLogger<InternetArchiveSearchProvider>.Instance);
        var request = new SearchRequest { Title = "Sample Title", Author = "Sample Author" };

        // When
        var outcome = await provider.SearchAsync(
            ArchiveIndexer(), "Sample Title Sample Author", null, request,
            new IndexerSearchContext(SearchOrigin.Sweep, allowance), CancellationToken.None);

        // Then: 3 variant searches + 5 metadata lookups, each counted once at the gate, the wire and the bound
        Assert.Equal(IndexerSearchStatus.Completed, outcome.Status);
        Assert.Equal(8, harness.Handler.Hits);
        Assert.Equal(8, gate.Contexts.Count);
        Assert.Equal(8, allowance.Taken);
        Assert.Equal(8, outcome.RequestsSent);
    }

    [Fact]
    public async Task EveryWireRequestCounts_MyAnonamouse_SearchPlusTopN()
    {
        // Given: five results of which the top three are enriched
        using var harness = new IndexerRequestGateHarness(new RecordingStateStore(), MyAnonamouseWire(resultCount: 5));
        var gate = new CountingGate(harness.Gate);
        var allowance = new CycleRequestAllowance(50);
        var provider = CreateMyAnonamouseProvider(gate, harness.Handler);

        // When
        var outcome = await provider.SearchAsync(
            CreateMyAnonamouseIndexer(), "query", null, EnrichRequest(topResults: 3),
            new IndexerSearchContext(SearchOrigin.Sweep, allowance), CancellationToken.None);

        // Then: 1 search + 3 detail requests
        Assert.Equal(IndexerSearchStatus.Completed, outcome.Status);
        Assert.Equal(4, harness.Handler.Hits);
        Assert.Equal(4, gate.Contexts.Count);
        Assert.Equal(4, allowance.Taken);
        Assert.Equal(4, outcome.RequestsSent);
        Assert.Equal(3, gate.Contexts.Count(context => context.Kind == IndexerRequestKind.Enrichment));
    }

    [Fact]
    public async Task EveryWireRequestCounts_TorznabRedirectHop_AddsOne()
    {
        // Given: an API that answers 302 to a second path, which answers the feed
        using var harness = new IndexerRequestGateHarness(new RecordingStateStore(), (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("/moved", UriKind.Relative);
                return Task.FromResult(redirect);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<rss><channel></channel></rss>") });
        });
        var gate = new CountingGate(harness.Gate);
        var allowance = new CycleRequestAllowance(50);
        using var client = new HttpClient(harness.Handler, disposeHandler: false);
        var provider = new TorznabNewznabSearchProvider(client, gate, NullLogger<TorznabNewznabSearchProvider>.Instance);
        var indexer = new IndexerBuilder()
            .WithId(7)
            .WithName("Torznab")
            .WithImplementation("Torznab")
            .WithUrl("https://indexer.example.invalid")
            .Build();

        // When
        var outcome = await provider.SearchAsync(
            indexer, "query", null, null, new IndexerSearchContext(SearchOrigin.Sweep, allowance), CancellationToken.None);

        // Then: the search and its one followed hop are two gate calls, two wire requests and two charges
        Assert.Equal(IndexerSearchStatus.Completed, outcome.Status);
        Assert.Equal(2, harness.Handler.Hits);
        Assert.Equal([IndexerRequestKind.Search, IndexerRequestKind.Redirect], gate.Contexts.Select(context => context.Kind));
        Assert.Equal(2, allowance.Taken);
        Assert.Equal(2, outcome.RequestsSent);
    }

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> InternetArchiveWire(int itemCount)
    {
        var docs = string.Join(
            ",",
            Enumerable.Range(1, itemCount).Select(index =>
                $$"""{"identifier": "sample_item_{{index}}", "title": "Sample Item {{index}}", "creator": "Sample Author", "date": "2010-03-01"}"""));
        var searchBody = "{\"response\": {\"docs\": [" + docs + "]}}";
        return (request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("/advancedsearch.php", StringComparison.Ordinal)
                ? searchBody
                : AudioMetadata)
        });
    }

    private static IConfigurationService ArchiveSettings()
    {
        var configurationService = new Mock<IConfigurationService>();
        configurationService
            .Setup(service => service.GetApplicationSettingsAsync())
            .ReturnsAsync(new ApplicationSettingsBuilder().WithExtractArchive().Build());
        return configurationService.Object;
    }

    private static Indexer ArchiveIndexer()
    {
        return new IndexerBuilder()
            .WithId(42)
            .WithName("Internet Archive")
            .WithType("DirectDownload")
            .WithImplementation("InternetArchive")
            .WithUrl("https://archive.org")
            .Build();
    }
}
