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
 */
using System.Text.Json;
using Listenarr.Application.Library.RecycleBin;
using Listenarr.Tests.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Api.Features.Configuration
{
    /// <summary>
    /// The settings path for the stalled-download floor: bounds checked at the endpoint,
    /// matching the shape already used for <see cref="StalledDownloadTimeoutSettingTests"/>.
    /// </summary>
    [Trait("Name", "StalledDownloadFloorPercentSettingTests")]
    [Trait("Category", "Configuration")]
    public class StalledDownloadFloorPercentSettingTests : BaseTests
    {
        [Theory]
        [InlineData(-1)]
        [InlineData(-0.01)]
        public async Task SaveApplicationSettings_RejectsAFloorBelowZero(decimal floorPercent)
        {
            // Strict, with nothing set up: a rejected value must not reach the save at all.
            var configurationService = new Mock<IConfigurationService>(MockBehavior.Strict);
            var broadcaster = new Mock<IHubBroadcaster>(MockBehavior.Strict);
            var controller = CreateController(configurationService.Object, broadcaster.Object);

            var result = await controller.SaveApplicationSettings(
                new ApplicationSettings { Version = 3, StalledDownloadFloorPercent = floorPercent });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            var payload = JsonSerializer.SerializeToElement(badRequest.Value);
            Assert.Equal("invalid_stalled_download_floor_percent", payload.GetProperty("code").GetString());
            configurationService.VerifyNoOtherCalls();
            broadcaster.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SaveApplicationSettings_RejectsAFloorAboveTheMax()
        {
            var configurationService = new Mock<IConfigurationService>(MockBehavior.Strict);
            var broadcaster = new Mock<IHubBroadcaster>(MockBehavior.Strict);
            var controller = CreateController(configurationService.Object, broadcaster.Object);

            var result = await controller.SaveApplicationSettings(
                new ApplicationSettings
                {
                    Version = 3,
                    StalledDownloadFloorPercent = ApplicationSettings.MaxStalledDownloadFloorPercent + 0.01m
                });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            var payload = JsonSerializer.SerializeToElement(badRequest.Value);
            Assert.Equal("invalid_stalled_download_floor_percent", payload.GetProperty("code").GetString());
            configurationService.VerifyNoOtherCalls();
            broadcaster.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(0)] // Off, explicitly accepted -- not a rejection, and not the same as missing.
        [InlineData(1)] // The model default.
        [InlineData(2.5)]
        public async Task SaveApplicationSettings_AcceptsAndEchoesAFloorInRange(decimal floorPercent)
        {
            // The control for the rejections above, including zero (off) at the low end and a
            // fractional value in the middle.
            var configurationService = new Mock<IConfigurationService>(MockBehavior.Strict);
            configurationService
                .Setup(service => service.SaveApplicationSettingsAsync(
                    It.Is<ApplicationSettings>(settings => settings.StalledDownloadFloorPercent == floorPercent)))
                .Returns(Task.CompletedTask);
            var broadcaster = new Mock<IHubBroadcaster>();
            var controller = CreateController(configurationService.Object, broadcaster.Object);

            var result = await controller.SaveApplicationSettings(
                new ApplicationSettings { Version = 3, StalledDownloadFloorPercent = floorPercent });

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(floorPercent, Assert.IsType<ApplicationSettings>(ok.Value).StalledDownloadFloorPercent);
            configurationService.Verify(service => service.SaveApplicationSettingsAsync(
                It.IsAny<ApplicationSettings>()), Times.Once);
        }

        [Fact]
        public void ApplicationSettings_SerializesTheFloorUnderTheNameTheFrontendReads()
        {
            // The frontend reads and writes stalledDownloadFloorPercent. A rename on either side
            // would quietly drop the value on every save.
            var json = JsonSerializer.SerializeToElement(
                new ApplicationSettings { StalledDownloadFloorPercent = 3.5m },
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.Equal(3.5m, json.GetProperty("stalledDownloadFloorPercent").GetDecimal());

            var back = JsonSerializer.Deserialize<ApplicationSettings>(
                """{ "stalledDownloadFloorPercent": 2 }""",
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal(2m, back!.StalledDownloadFloorPercent);
        }

        [Fact]
        public async Task ConfigurationService_StoresAndReadsBackTheFloor()
        {
            var configurationService = _provider.GetRequiredService<IConfigurationService>();
            var current = await configurationService.GetApplicationSettingsAsync();
            Assert.Equal(1m, current.StalledDownloadFloorPercent);

            current.StalledDownloadFloorPercent = 0m;
            await configurationService.SaveApplicationSettingsAsync(current);

            var reread = await configurationService.GetApplicationSettingsAsync();
            Assert.Equal(0m, reread.StalledDownloadFloorPercent);
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
