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

using Listenarr.Application.Search.Scheduling;
using Listenarr.Tests.Features.Infrastructure.HostedServices.Search.Sweep;
using Microsoft.AspNetCore.SignalR;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search
{
    // Follow-up to #757 (#949): an OperationCanceledException that is not the host stopping can be a
    // local cancellation or timeout. These drive the whole automatic-search cycle, on this stack's
    // due-book schedule and request gate, to prove a debug broadcast can no longer abort a book,
    // that a real shutdown still stops the cycle, and that a download-client timeout keeps the
    // fast retry.
    [Trait("Name", "AutomaticSearchProcessorCancellationTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class AutomaticSearchProcessorCancellationTests : AutomaticSearchSweepTestBase
    {
        private const string Timeout = "The request was canceled due to the configured HttpClient.Timeout";

        [Fact]
        public async Task RunCycleAsync_BroadcastTimesOut_StillQueuesTheDownload()
        {
            // Given: every debug broadcast throws a timeout-style TaskCanceledException
            // while the host is not stopping
            var broadcasts = 0;
            HubOverride = NewHubContext(() =>
            {
                broadcasts++;
                throw new TaskCanceledException(Timeout);
            });
            BuildHost();
            await ConfigureAsync();
            var indexer = await AddIndexerAsync("Only");
            var book = (await AddBooksAsync(1, StartUtc.AddMinutes(-5))).Single();
            Indexers.Script = (_, _) => Returns(AcceptableSource);

            // When: an automatic search cycle runs
            await RunCycleAsync();

            // Then: both broadcasts failed, the book was still grabbed, and it was recorded as
            // searched with no failure counted against it
            Assert.Equal(2, broadcasts);
            Downloads.Verify(d => d.StartDownloadAsync(
                    It.IsAny<SearchResult>(), DirectDownloadMetadataKeys.ClientId, book.Id, It.IsAny<CancellationToken>()),
                Times.Once);
            var (dueUtc, failureCount, lastSearchUtc, _) = await Database.ReadBookAsync(book.Id);
            Assert.Equal(0, failureCount);
            Assert.Equal(StartUtc, lastSearchUtc);
            Assert.Equal(StartUtc.AddHours(24), dueUtc);
            Assert.Equal(nameof(PairScheduleResult.Satisfied), (await Database.ReadPairAsync(book.Id, indexer))!.LastOutcome);
        }

        [Fact]
        public async Task RunCycleAsync_BroadcastCanceledByHostShutdown_StopsWithoutQueuing()
        {
            // Given: the host stops while a debug broadcast is in flight
            using var stopping = new CancellationTokenSource();
            HubOverride = NewHubContext(() =>
            {
                stopping.Cancel();
                throw new OperationCanceledException(stopping.Token);
            });
            BuildHost();
            await ConfigureAsync();
            await AddIndexerAsync("Only");
            var dueUtc = StartUtc.AddMinutes(-5);
            var book = (await AddBooksAsync(1, dueUtc)).Single();
            Indexers.Script = (_, _) => Returns(AcceptableSource);

            // When: an automatic search cycle runs with the host's stopping token
            await RunCycleAsync(stopping.Token);

            // Then: the cancellation is not swallowed by the broadcast catch. Nothing is grabbed
            // and the book keeps its due time, unsearched and with no failure counted.
            Downloads.Verify(d => d.StartDownloadAsync(
                    It.IsAny<SearchResult>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
                Times.Never);
            Assert.Equal((dueUtc, 0, (DateTime?)null, true), await Database.ReadBookAsync(book.Id));
        }

        [Fact]
        public async Task RunCycleAsync_DownloadClientTimesOut_RetriesTheBookNextCycle()
        {
            // Given: the download client times out on the first submission and accepts the second
            Downloads.Reset();
            Downloads.SetupSequence(d => d.StartDownloadAsync(
                    It.IsAny<SearchResult>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TaskCanceledException(Timeout))
                .ReturnsAsync("download-1");
            await ConfigureAsync();
            await AddIndexerAsync("Only");
            var book = (await AddBooksAsync(1, StartUtc.AddMinutes(-5))).Single();
            Indexers.Script = (_, _) => Returns(AcceptableSource);

            // When: the first cycle hits the timeout
            await RunCycleAsync();

            // Then: the timeout does not escape the cycle and the book is due again after the
            // short failure backoff, not parked behind the per-book cooldown
            var (retryUtc, failureCount, _, _) = await Database.ReadBookAsync(book.Id);
            Assert.Equal(1, failureCount);
            Assert.Equal(StartUtc.AddMinutes(15), retryUtc);

            // When: the next cycle runs once the backoff has passed
            Clock.Advance(TimeSpan.FromMinutes(15));
            await RunCycleAsync();

            // Then: the submission is retried, and only now is the book satisfied
            Downloads.Verify(d => d.StartDownloadAsync(
                    It.IsAny<SearchResult>(), DirectDownloadMetadataKeys.ClientId, book.Id, It.IsAny<CancellationToken>()),
                Times.Exactly(2));
            Assert.Equal(0, (await Database.ReadBookAsync(book.Id)).FailureCount);
        }

        private static IHubContext<DownloadHub> NewHubContext(Action onBroadcast)
        {
            var clientProxy = new Mock<IClientProxy>();
            clientProxy
                .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    onBroadcast();
                    return Task.CompletedTask;
                });
            var hubClients = new Mock<IHubClients>();
            hubClients.Setup(c => c.All).Returns(clientProxy.Object);
            var hubContext = new Mock<IHubContext<DownloadHub>>();
            hubContext.Setup(h => h.Clients).Returns(hubClients.Object);
            return hubContext.Object;
        }
    }
}
