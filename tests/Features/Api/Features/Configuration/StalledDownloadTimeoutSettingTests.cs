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
using Listenarr.Application.Library.RecycleBin;
using Listenarr.Tests.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Api.Features.Configuration
{
    /// <summary>
    /// The settings path for the stalled-download timeout: bounds checked at the endpoint, and the
    /// value carried through the save, the response and the stored row unchanged.
    /// </summary>
    [Trait("Name", "StalledDownloadTimeoutSettingTests")]
    [Trait("Category", "Configuration")]
    public class StalledDownloadTimeoutSettingTests : BaseTests
    {
        [Theory]
        [InlineData(-1)]
        [InlineData(ApplicationSettings.MaxStalledDownloadTimeoutHours + 1)]
        public async Task SaveApplicationSettings_RejectsATimeoutOutsideTheAcceptedRange(int hours)
        {
            // Strict, with nothing set up: a rejected value must not reach the save at all.
            var configurationService = new Mock<IConfigurationService>(MockBehavior.Strict);
            var broadcaster = new Mock<IHubBroadcaster>(MockBehavior.Strict);
            var controller = CreateController(configurationService.Object, broadcaster.Object);

            var result = await controller.SaveApplicationSettings(
                new ApplicationSettings { Version = 3, StalledDownloadTimeoutHours = hours });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            var payload = JsonSerializer.SerializeToElement(badRequest.Value);
            Assert.Equal("invalid_stalled_download_timeout", payload.GetProperty("code").GetString());
            configurationService.VerifyNoOtherCalls();
            broadcaster.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(24)]
        [InlineData(ApplicationSettings.MaxStalledDownloadTimeoutHours)]
        public async Task SaveApplicationSettings_AcceptsAndEchoesATimeoutInRange(int hours)
        {
            // The control for the rejection above, at both ends of the range and in the middle.
            var configurationService = new Mock<IConfigurationService>(MockBehavior.Strict);
            configurationService
                .Setup(service => service.SaveApplicationSettingsAsync(
                    It.Is<ApplicationSettings>(settings => settings.StalledDownloadTimeoutHours == hours)))
                .Returns(Task.CompletedTask);
            var broadcaster = new Mock<IHubBroadcaster>();
            var controller = CreateController(configurationService.Object, broadcaster.Object);

            var result = await controller.SaveApplicationSettings(
                new ApplicationSettings { Version = 3, StalledDownloadTimeoutHours = hours });

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(hours, Assert.IsType<ApplicationSettings>(ok.Value).StalledDownloadTimeoutHours);
            configurationService.Verify(service => service.SaveApplicationSettingsAsync(
                It.IsAny<ApplicationSettings>()), Times.Once);
        }

        [Fact]
        public void ApplicationSettings_SerializesTheTimeoutUnderTheNameTheFrontendReads()
        {
            // The frontend reads and writes stalledDownloadTimeoutHours. A rename on either side
            // would quietly drop the value on every save.
            var json = JsonSerializer.SerializeToElement(
                new ApplicationSettings { StalledDownloadTimeoutHours = 36 },
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.Equal(36, json.GetProperty("stalledDownloadTimeoutHours").GetInt32());

            var back = JsonSerializer.Deserialize<ApplicationSettings>(
                """{ "stalledDownloadTimeoutHours": 12 }""",
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal(12, back!.StalledDownloadTimeoutHours);
        }

        [Fact]
        public async Task ConfigurationService_StoresAndReadsBackTheTimeout()
        {
            var configurationService = _provider.GetRequiredService<IConfigurationService>();
            var current = await configurationService.GetApplicationSettingsAsync();
            Assert.Equal(0, current.StalledDownloadTimeoutHours);

            current.StalledDownloadTimeoutHours = 48;
            await configurationService.SaveApplicationSettingsAsync(current);

            var reread = await configurationService.GetApplicationSettingsAsync();
            Assert.Equal(48, reread.StalledDownloadTimeoutHours);
        }

        private static SettingsController CreateController(
            IConfigurationService configurationService,
            IHubBroadcaster broadcaster)
        {
            var recycleBinService = new Mock<IRecycleBinService>();
            recycleBinService
                .Setup(service => service.ValidatePathAsync(
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(RecycleBinPathValidation.Valid);

            return new SettingsController(
                configurationService,
                NullLogger<SettingsController>.Instance,
                broadcaster,
                recycleBinService.Object,
                Mock.Of<IFileNamingService>());
        }
    }
}
