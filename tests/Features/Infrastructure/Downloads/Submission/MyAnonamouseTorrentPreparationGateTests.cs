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
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks.Api;

namespace Listenarr.Tests.Features.Infrastructure.Downloads.Submission
{
    [Trait("Name", "MyAnonamouseTorrentPreparationGateTests")]
    [Trait("Category", "MyAnonamouseTorrentPreparation")]
    public class MyAnonamouseTorrentPreparationGateTests : BaseTests
    {
        private readonly RecordingGate _gate = new();
        private Indexer _indexer = new IndexerBuilder().Build();

        public override async Task InitializeAsync()
        {
            _services.AddSingleton<IIndexerRequestGate>(_gate);
            _services.AddHttpClient("", client =>
            {
                client.BaseAddress = new Uri("https://www.myanonamouse.net");
            })
                .AddHttpMessageHandler<MyAnonamouseApiMock>();
            Init();

            _indexer = await _indexerRepository.AddAsync(new IndexerBuilder()
                .WithId(1)
                .WithName("MyAnonamouse1")
                .WithUrl("https://www.myanonamouse.net")
                .WithImplementation("MyAnonamouse")
                .WithType("Torrent")
                .WithEnabled()
                .WithInteractiveSearch()
                .WithSetting("mam_id", "old_mam")
                .Build());
        }

        [Fact]
        public async Task Prepare_EachSendTheFetcherMakesIsOneDownloadFetchGateCall()
        {
            // Given: a torrent URL to prepare
            var searchResult = CreateSearchResult("https://www.myanonamouse.net/tor/redirectstart");

            // When
            await _provider.GetRequiredService<MyAnonamouseTorrentPreparationService>().PrepareAsync(searchResult);

            // Then: each send the fetcher made was one DownloadFetch gate call
            var wireRequests = _provider.GetRequiredService<MyAnonamouseApiMock>().GetCallCount();
            Assert.Equal(2, wireRequests);
            Assert.Equal(wireRequests, _gate.Contexts.Count);
            Assert.All(_gate.Contexts, context =>
            {
                Assert.Equal(IndexerRequestKind.DownloadFetch, context.Kind);
                Assert.Equal(_indexer.Id, context.IndexerId);
            });
            Assert.NotNull(searchResult.TorrentFileContent);
        }

        [Fact]
        public async Task Prepare_WhenTheGateDefers_PropagatesTheDeferralAndSendsNothing()
        {
            // Given: a gate that holds every request back
            _gate.DeferAll = true;
            var searchResult = CreateSearchResult("https://www.myanonamouse.net/tor/redirectstart");

            // When
            var preparation = _provider.GetRequiredService<MyAnonamouseTorrentPreparationService>().PrepareAsync(searchResult);

            // Then: the deferral reaches the caller and no request was sent
            var exception = await Assert.ThrowsAsync<IndexerRequestDeferredException>(() => preparation);
            Assert.Equal(RequestDeferralReason.CycleRequestBound, exception.Deferral.Reason);
            Assert.Single(_gate.Contexts);
            Assert.Equal(0, _provider.GetRequiredService<MyAnonamouseApiMock>().GetCallCount());
            Assert.Null(searchResult.TorrentFileContent);
        }

        private SearchResult CreateSearchResult(string torrentUrl)
        {
            return new SearchResult
            {
                Title = "Test Book",
                TorrentUrl = torrentUrl,
                IndexerId = _indexer.Id,
                TorrentFileContent = null
            };
        }

        // Records every request context, then sends or defers.
        private sealed class RecordingGate : IIndexerRequestGate
        {
            private readonly ConcurrentQueue<IndexerRequestContext> _contexts = new();

            public bool DeferAll { get; set; }

            public IReadOnlyList<IndexerRequestContext> Contexts => _contexts.ToArray();

            public async Task<IndexerRequestResult> SendAsync(
                IndexerRequestContext context,
                Func<CancellationToken, Task<HttpResponseMessage>> dispatch,
                CancellationToken cancellationToken)
            {
                _contexts.Enqueue(context);
                if (DeferAll)
                {
                    return new IndexerRequestResult.Deferred(
                        new RequestDeferral(DateTime.UtcNow, RequestDeferralReason.CycleRequestBound));
                }

                return new IndexerRequestResult.Sent(await dispatch(cancellationToken));
            }

            public Task<RequestDeferral?> GetDeferralAsync(int indexerId, SearchOrigin origin, CancellationToken cancellationToken)
            {
                return Task.FromResult<RequestDeferral?>(null);
            }
        }
    }
}
