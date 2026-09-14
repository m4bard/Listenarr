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

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Downloads.Queue
{
    [Trait("Area", "Infrastructure")]
    [Trait("Name", "QueueMonitorProcessorStatusCacheTests")]
    [Trait("Category", "QueueMonitorProcessor")]
    public class QueueMonitorProcessorStatusCacheTests : BaseTests
    {
        [Fact]
        [Trait("Method", "RunCycleAsync")]
        [Trait("Scenario", "PublishesPolledClientStatusesEveryCycle")]
        public async Task RunCycleAsync_PublishesPolledClientStatuses_EveryCycle()
        {
            var polledStatuses = new List<QueueClientStatus>
            {
                new()
                {
                    ClientId = "client-1",
                    ClientName = "qBittorrent",
                    ClientType = "qbittorrent",
                    SnapshotState = "unavailable",
                    IsUnavailable = true,
                    SnapshotFailureReason = "timeout"
                }
            };

            var snapshot = new QueueSnapshot
            {
                Items = new List<QueueItem>(),
                Clients = polledStatuses
            };

            var downloadQueueService = new Mock<IDownloadQueueService>();
            downloadQueueService.Setup(service => service.GetQueueSnapshotAsync()).ReturnsAsync(snapshot);

            var serviceProvider = new Mock<IServiceProvider>();
            serviceProvider
                .Setup(provider => provider.GetService(typeof(IDownloadQueueService)))
                .Returns(downloadQueueService.Object);

            var scope = new Mock<IServiceScope>();
            scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);

            var scopeFactory = new Mock<IServiceScopeFactory>();
            scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

            var statusCache = new DownloadClientStatusCache();

            var processor = new QueueMonitorProcessor(
                scopeFactory.Object,
                new Mock<IHubContext<DownloadHub>>().Object,
                statusCache,
                NullLogger<QueueMonitorProcessor>.Instance);

            await processor.RunCycleAsync(CancellationToken.None);

            var cached = statusCache.GetStatuses();
            var cachedStatus = Assert.Single(cached);
            Assert.Equal("client-1", cachedStatus.ClientId);
            Assert.Equal("timeout", cachedStatus.SnapshotFailureReason);
            Assert.True(cachedStatus.IsUnavailable);
        }
    }
}
