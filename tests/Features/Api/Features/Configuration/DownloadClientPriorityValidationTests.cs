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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Api.Features.Configuration
{
    /// <summary>
    /// The save endpoint binds the domain entity directly, so the bounds on the new Priority
    /// field are enforced in the controller rather than by a request model. Both directions
    /// are covered here, plus the case that decides whether an older client breaks.
    /// </summary>
    [Trait("Area", "DownloadClients")]
    [Trait("Name", "DownloadClientPriorityValidationTests")]
    [Trait("Category", "Api")]
    public class DownloadClientPriorityValidationTests : BaseTests
    {
        private static DownloadClientController CreateController(
            out Mock<IConfigurationService> configurationService)
        {
            configurationService = new Mock<IConfigurationService>();
            var gateway = new Mock<IDownloadClientGateway>();
            return new DownloadClientController(
                configurationService.Object,
                gateway.Object,
                NullLogger<DownloadClientController>.Instance);
        }

        private static DownloadClientConfiguration Client(int priority) => new()
        {
            Id = "client-1",
            Name = "Local",
            Type = "qbittorrent",
            Host = "host.local",
            Port = 8080,
            Priority = priority
        };

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(51)]
        [Trait("Scenario", "OutOfRangePriorityIsRejected")]
        public async Task APriorityOutsideTheAllowedRange_IsRejected(int priority)
        {
            var controller = CreateController(out var configurationService);

            var result = await controller.SaveDownloadClientConfiguration(Client(priority));

            Assert.IsType<BadRequestObjectResult>(result.Result);
            // The rejection has to happen before anything is written, or a bad value is
            // persisted and only the response is unhappy.
            configurationService.Verify(
                c => c.SaveDownloadClientConfigurationAsync(It.IsAny<DownloadClientConfiguration>()),
                Times.Never);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(25)]
        [InlineData(50)]
        [Trait("Scenario", "InRangePriorityIsAccepted")]
        public async Task APriorityInsideTheAllowedRange_ReachesTheSave(int priority)
        {
            // Control for the theory above. A controller that rejected everything, or that
            // rejected nothing, cannot satisfy both.
            var controller = CreateController(out var configurationService);
            configurationService
                .Setup(c => c.SaveDownloadClientConfigurationAsync(It.IsAny<DownloadClientConfiguration>()))
                .ReturnsAsync("client-1");

            var result = await controller.SaveDownloadClientConfiguration(Client(priority));

            Assert.IsNotType<BadRequestObjectResult>(result.Result);
            configurationService.Verify(
                c => c.SaveDownloadClientConfigurationAsync(
                    It.Is<DownloadClientConfiguration>(config => config.Priority == priority)),
                Times.Once);
        }

        [Fact]
        [Trait("Scenario", "AbsentPriorityDoesNotBreakAnOlderCaller")]
        public void ABodyWithNoPriorityField_DeserialisesToTheDefaultRatherThanZero()
        {
            // An older cached frontend, or an existing API script, posts no priority at all.
            // System.Text.Json leaves the property initializer alone when the key is absent,
            // so the value is 1 and the new bounds check does not turn a working call into a
            // 400. If this came back 0 the upgrade would break every such caller.
            var json = """
                {"id":"client-1","name":"Local","type":"qbittorrent","host":"host.local","port":8080}
                """;

            var parsed = JsonSerializer.Deserialize<DownloadClientConfiguration>(
                json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.NotNull(parsed);
            Assert.Equal(1, parsed.Priority);
        }
    }
}
