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
    /// The settings path for DownloadClientHistoryLimit (item 357's new setting). A value of
    /// zero or less currently reaches the save with no error and then, downstream in
    /// NzbgetHistoryReader, collapses the client-side .Take(limit) to an empty sequence --
    /// silently turning off NZBGet history processing. Neither Sonarr nor Readarr validate this
    /// setting either (read: it is never exposed through any API Resource class in either family
    /// repo -- it is settable only by hand-editing config.xml, which has no save-path validation
    /// to add to in the first place). Listenarr exposes it on the settings page, so it needs its
    /// own floor. 1 is the chosen minimum, by construction: "keep at most N" is meaningless at
    /// N &lt;= 0.
    /// </summary>
    [Trait("Name", "DownloadClientHistoryLimitSettingTests")]
    [Trait("Category", "Configuration")]
    public class DownloadClientHistoryLimitSettingTests : BaseTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task SaveApplicationSettings_RejectsAHistoryLimitBelowOne(int limit)
        {
            // Strict, with nothing set up: a rejected value must not reach the save at all.
            var configurationService = new Mock<IConfigurationService>(MockBehavior.Strict);
            var broadcaster = new Mock<IHubBroadcaster>(MockBehavior.Strict);
            var controller = CreateController(configurationService.Object, broadcaster.Object);

            var result = await controller.SaveApplicationSettings(
                new ApplicationSettings { Version = 3, DownloadClientHistoryLimit = limit });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            var payload = JsonSerializer.SerializeToElement(badRequest.Value);
            Assert.Equal("invalid_download_client_history_limit", payload.GetProperty("code").GetString());
            configurationService.VerifyNoOtherCalls();
            broadcaster.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(1)]
        [InlineData(60)]
        [InlineData(1000)]
        public async Task SaveApplicationSettings_AcceptsAndEchoesAValidHistoryLimit(int limit)
        {
            // The control for the rejection above: a positive value, the shipped default, and an
            // arbitrary larger one all still save successfully.
            var configurationService = new Mock<IConfigurationService>(MockBehavior.Strict);
            configurationService
                .Setup(service => service.SaveApplicationSettingsAsync(
                    It.Is<ApplicationSettings>(settings => settings.DownloadClientHistoryLimit == limit)))
                .Returns(Task.CompletedTask);
            var broadcaster = new Mock<IHubBroadcaster>();
            var controller = CreateController(configurationService.Object, broadcaster.Object);

            var result = await controller.SaveApplicationSettings(
                new ApplicationSettings { Version = 3, DownloadClientHistoryLimit = limit });

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(limit, Assert.IsType<ApplicationSettings>(ok.Value).DownloadClientHistoryLimit);
            configurationService.Verify(service => service.SaveApplicationSettingsAsync(
                It.IsAny<ApplicationSettings>()), Times.Once);
        }

        private static SettingsController CreateController(
            IConfigurationService configurationService,
            IHubBroadcaster broadcaster)
        {
            return new SettingsController(
                configurationService,
                NullLogger<SettingsController>.Instance,
                broadcaster);
        }
    }
}
