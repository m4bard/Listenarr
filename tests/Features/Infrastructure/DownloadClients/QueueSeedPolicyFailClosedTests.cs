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
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.DownloadClients
{
    /// <summary>
    /// The queue poll is what deferred client removal reads CanRemove from, so it must apply the
    /// same rule the item fetch does: an inherited seed policy that could not be read is not proof
    /// that a torrent has no limits, and the torrent must not be reported as removable.
    /// </summary>
    [Trait("Name", "QueueSeedPolicyFailClosedTests")]
    [Trait("Category", "DownloadClients")]
    public class QueueSeedPolicyFailClosedTests : BaseTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [Trait("Method", "GetQueueAsync")]
        public async Task QbittorrentGetQueueAsync_UnknownSeedPolicyCannotAuthorizeRemoval(bool knownPolicy)
        {
            using var http = new HttpClient(new DelegatingHandlerMock((request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                var response = path.EndsWith("/preferences")
                    ? knownPolicy ? """{"max_ratio_enabled":true,"max_ratio":2,"max_seeding_time_enabled":false,"max_seeding_time":30}""" : "{}"
                    : path.EndsWith("/login") ? "Ok."
                    : path.EndsWith("/files") ? "[]"
                    : """[{"hash":"EXACT","name":"Book","state":"stoppedUP","progress":1,"ratio":3,"ratio_limit":-2,"seeding_time_limit":-2}]""";
                return Task.FromResult(MockUtils.GetCannedResponse(response));
            }));
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(http);
            var logger = NullLogger<QbittorrentAdapter>.Instance;
            var workflow = new QbittorrentQueueFetchWorkflow(factory.Object, new QbittorrentAuthSession(logger), logger, "qbittorrent");
            var client = new DownloadClientConfiguration { Host = "localhost", Port = 8080, RemoveCompletedDownloads = "remove_and_delete" };

            var item = Assert.Single(await workflow.GetQueueAsync(client, []));

            Assert.Equal(knownPolicy, item.CanRemove);
        }

        [Theory]
        [InlineData("missing")]
        [InlineData("failed")]
        [InlineData("known")]
        [Trait("Method", "GetQueueAsync")]
        public async Task TransmissionGetQueueAsync_UnknownSessionPolicyCannotAuthorizeRemoval(string policy)
        {
            using var http = new HttpClient(new DelegatingHandlerMock(async (request, ct) =>
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                if (body.RootElement.GetProperty("method").GetString() == "session-get")
                {
                    if (policy == "failed") throw new IOException("session unavailable");
                    return MockUtils.GetCannedResponse(policy == "missing"
                        ? """{"result":"success","arguments":{}}"""
                        : """{"result":"success","arguments":{"seedRatioLimited":true,"seedRatioLimit":2,"idle-seeding-limit-enabled":false,"idle-seeding-limit":30}}""");
                }
                return MockUtils.GetCannedResponse("""
                    {"result":"success","arguments":{"torrents":[{"id":1,"hashString":"EXACT","name":"Book","status":0,"percentDone":1,"seedRatioMode":0,"seedIdleMode":0,"uploadRatio":3}]}}
                    """);
            }));
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(http);
            var logger = NullLogger<TransmissionAdapter>.Instance;
            var workflow = new TransmissionQueueFetchWorkflow(new TransmissionRpcClient(factory.Object, "transmission", logger), logger);
            var client = new DownloadClientConfiguration { Host = "localhost", Port = 9091, Type = "transmission", RemoveCompletedDownloads = "remove_and_delete" };

            var item = Assert.Single(await workflow.GetQueueAsync(client, []));

            Assert.Equal(policy == "known", item.CanRemove);
        }
    }
}
