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

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Listenarr.Tests.Builders;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestGate;

/// <summary>
/// The MyAnonamouse provider over the real gate and the counting wire stub of
/// <see cref="IndexerRequestGateHarness"/>, plus the search body and request it enriches.
/// </summary>
internal static class ProviderWireFixture
{
    public const int MyAnonamouseIndexerId = 9;
    public const string SearchPath = "/tor/js/loadSearchJSONbasic.php";
    public const string DetailPath = "/tor/js/loadTorrentJSONBasic.php";

    public static MyAnonamouseSearchProvider CreateMyAnonamouseProvider(IIndexerRequestGate gate, HttpMessageHandler wire)
    {
        return new MyAnonamouseSearchProvider(
            NullLogger<MyAnonamouseSearchProvider>.Instance,
            new HttpClient(wire, disposeHandler: false) { BaseAddress = new Uri("https://indexer.example.invalid") },
            gate,
            Mock.Of<IIndexerRepository>());
    }

    public static Indexer CreateMyAnonamouseIndexer()
    {
        return new IndexerBuilder()
            .WithId(MyAnonamouseIndexerId)
            .WithName("MyAnonamouse")
            .WithType("Torrent")
            .WithImplementation("MyAnonamouse")
            .WithUrl("https://indexer.example.invalid")
            .WithSetting("mam_id", "test_mam")
            .Build();
    }

    public static SearchRequest EnrichRequest(int topResults)
    {
        return new SearchRequest
        {
            IncludeEnrichment = true,
            MyAnonamouse = new MyAnonamouseOptions { EnrichResults = true, EnrichTopResults = topResults }
        };
    }

    /// <summary>
    /// Answers the search with <paramref name="resultCount"/> results that all need enrichment,
    /// and every detail request with a small document.
    /// </summary>
    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> MyAnonamouseWire(int resultCount)
    {
        var results = string.Join(
            ",",
            Enumerable.Range(1, resultCount).Select(id => $$"""{ "id": "{{700 + id}}", "title": "Item {{id}}", "size": "1234" }"""));
        return (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == DetailPath
            ? Json("""{ "files": 3, "filetype": "m4b", "lang_code": "ENG" }""")
            : Json("[" + results + "]"));
    }

    public static HttpResponseMessage Json(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    /// <summary>
    /// Passes every call to the real gate and records its context, so a test can compare gate
    /// calls with the requests the wire stub saw.
    /// </summary>
    public sealed class CountingGate(IIndexerRequestGate inner) : IIndexerRequestGate
    {
        private readonly ConcurrentQueue<IndexerRequestContext> _contexts = new();

        public IReadOnlyList<IndexerRequestContext> Contexts => _contexts.ToArray();

        public Task<IndexerRequestResult> SendAsync(
            IndexerRequestContext context,
            Func<CancellationToken, Task<HttpResponseMessage>> dispatch,
            CancellationToken cancellationToken)
        {
            _contexts.Enqueue(context);
            return inner.SendAsync(context, dispatch, cancellationToken);
        }

        public Task<RequestDeferral?> GetDeferralAsync(int indexerId, SearchOrigin origin, CancellationToken cancellationToken)
        {
            return inner.GetDeferralAsync(indexerId, origin, cancellationToken);
        }
    }
}
