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

using System.Reflection;
using Listenarr.Application.Interfaces;
using Listenarr.Domain.Models;
using Listenarr.Domain.Models.Configurations;
using Listenarr.Infrastructure.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Listenarr.Tests.Features.Infrastructure.Platform
{
    public class SystemServiceVersionTests
    {
        [Fact]
        public void GetSystemInfo_UsesHostApplicationVersion()
        {
            var systemService = CreateSystemService();

            var systemInfo = systemService.GetSystemInfo();

            Assert.Equal(GetExpectedApiVersion(), systemInfo.Version);
            Assert.NotEqual("1.0.0.0", systemInfo.Version);
        }

        [Fact]
        public async Task GetServiceHealthAsync_UsesHostApplicationVersion()
        {
            var systemService = CreateSystemService();

            var serviceHealth = await systemService.GetServiceHealthAsync();

            Assert.Equal(GetExpectedApiVersion(), serviceHealth.Version);
            Assert.NotEqual("1.0.0.0", serviceHealth.Version);
        }

        private static SystemService CreateSystemService()
        {
            var configurationService = new Mock<IConfigurationService>();
            configurationService
                .Setup(service => service.GetApiConfigurationsAsync())
                .ReturnsAsync(new List<ApiConfiguration>());
            configurationService
                .Setup(service => service.GetDownloadClientConfigurationsAsync())
                .ReturnsAsync(new List<DownloadClientConfiguration>());

            var applicationPathService = new Mock<IApplicationPathService>();

            var applicationVersionService = new Mock<IApplicationVersionService>();
            applicationVersionService
                .Setup(service => service.Resolve())
                .Returns(GetExpectedApiVersion());

            return new SystemService(
                configurationService.Object,
                NullLogger<SystemService>.Instance,
                applicationPathService.Object,
                applicationVersionService.Object);
        }

        private static string GetExpectedApiVersion()
        {
            var version = typeof(global::Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            Assert.False(string.IsNullOrWhiteSpace(version));

            var metadataIndex = version.IndexOf('+');
            return metadataIndex > 0
                ? version[..metadataIndex]
                : version;
        }
    }
}
