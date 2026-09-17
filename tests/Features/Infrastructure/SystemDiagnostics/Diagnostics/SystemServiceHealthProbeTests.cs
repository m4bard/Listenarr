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

using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.SystemDiagnostics.Diagnostics
{
    /// <summary>
    /// The System page reports a per-client connection status. Before these tests the status was
    /// the constant "connected" for every enabled client, so a dead client displayed as healthy.
    /// Every test here fails against that constant.
    /// </summary>
    [Trait("Area", "Infrastructure")]
    [Trait("Name", "SystemServiceHealthProbeTests")]
    [Trait("Category", "SystemService")]
    public class SystemServiceHealthProbeTests : BaseTests
    {
        private static readonly TimeSpan ShortProbeTimeout = TimeSpan.FromMilliseconds(100);

        [Fact]
        [Trait("Method", "GetServiceHealthAsync")]
        [Trait("Scenario", "DistinguishesReachableClientFromUnreachableOne")]
        public async Task GetServiceHealthAsync_DistinguishesReachableClientFromUnreachableOne()
        {
            var gateway = new Mock<IDownloadClientGateway>();
            gateway
                .Setup(g => g.TestConnectionAsync(It.Is<DownloadClientConfiguration>(c => c.Name == "reachable"), It.IsAny<CancellationToken>()))
                .ReturnsAsync((true, "ok"));
            gateway
                .Setup(g => g.TestConnectionAsync(It.Is<DownloadClientConfiguration>(c => c.Name == "unreachable"), It.IsAny<CancellationToken>()))
                .ReturnsAsync((false, "Could not connect to the host and/or port."));

            var systemService = CreateSystemService(
                gateway,
                Client("reachable", "qbittorrent"),
                Client("unreachable", "sabnzbd"));

            var health = await systemService.GetServiceHealthAsync();

            var reachable = Assert.Single(health.DownloadClients.Clients, c => c.Name == "reachable");
            var unreachable = Assert.Single(health.DownloadClients.Clients, c => c.Name == "unreachable");

            Assert.Equal("connected", reachable.Status);
            Assert.Equal("disconnected", unreachable.Status);
            Assert.Equal(1, health.DownloadClients.Connected);
            Assert.Equal(2, health.DownloadClients.Total);
            Assert.Equal("warning", health.DownloadClients.Status);
            Assert.Equal("warning", health.Status);
        }

        [Fact]
        [Trait("Method", "GetServiceHealthAsync")]
        [Trait("Scenario", "ReportsErrorWhenEveryClientIsUnreachable")]
        public async Task GetServiceHealthAsync_ReportsErrorWhenEveryClientIsUnreachable()
        {
            var gateway = new Mock<IDownloadClientGateway>();
            gateway
                .Setup(g => g.TestConnectionAsync(It.IsAny<DownloadClientConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((false, "Connection failed."));

            var systemService = CreateSystemService(gateway, Client("first", "qbittorrent"), Client("second", "nzbget"));

            var health = await systemService.GetServiceHealthAsync();

            Assert.All(health.DownloadClients.Clients, c => Assert.Equal("disconnected", c.Status));
            Assert.Equal(0, health.DownloadClients.Connected);
            Assert.Equal(2, health.DownloadClients.Total);
            Assert.Equal("error", health.DownloadClients.Status);
            Assert.Equal("error", health.Status);
        }

        [Fact]
        [Trait("Method", "GetServiceHealthAsync")]
        [Trait("Scenario", "ReportsHealthyWhenEveryClientAnswers")]
        public async Task GetServiceHealthAsync_ReportsHealthyWhenEveryClientAnswers()
        {
            var gateway = new Mock<IDownloadClientGateway>();
            gateway
                .Setup(g => g.TestConnectionAsync(It.IsAny<DownloadClientConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((true, "ok"));

            var systemService = CreateSystemService(gateway, Client("first", "qbittorrent"), Client("second", "nzbget"));

            var health = await systemService.GetServiceHealthAsync();

            Assert.All(health.DownloadClients.Clients, c => Assert.Equal("connected", c.Status));
            Assert.Equal(2, health.DownloadClients.Connected);
            Assert.Equal("healthy", health.DownloadClients.Status);
            Assert.Equal("healthy", health.Status);
        }

        [Fact]
        [Trait("Method", "GetServiceHealthAsync")]
        [Trait("Scenario", "ReportsUnknownRatherThanDisconnectedWhenProbeOutrunsTimeout")]
        public async Task GetServiceHealthAsync_ReportsUnknownRatherThanDisconnectedWhenProbeOutrunsTimeout()
        {
            // A probe that never answers. The endpoint must give up on it and say so honestly
            // rather than assert the client is down, which is a different false statement.
            var neverAnswers = new TaskCompletionSource<(bool Success, string Message)>();
            var gateway = new Mock<IDownloadClientGateway>();
            gateway
                .Setup(g => g.TestConnectionAsync(It.IsAny<DownloadClientConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns(neverAnswers.Task);

            var systemService = CreateSystemService(gateway, Client("hangs", "transmission"));

            var health = await systemService.GetServiceHealthAsync();

            var client = Assert.Single(health.DownloadClients.Clients);
            Assert.Equal("unknown", client.Status);
            Assert.NotEqual("connected", client.Status);
            Assert.NotEqual("disconnected", client.Status);
            Assert.Equal(0, health.DownloadClients.Connected);
            Assert.Equal(1, health.DownloadClients.Total);
            Assert.Equal("unknown", health.DownloadClients.Status);
            Assert.Equal("unknown", health.Status);

            neverAnswers.TrySetResult((true, "too late"));
        }

        [Fact]
        [Trait("Method", "GetServiceHealthAsync")]
        [Trait("Scenario", "CancelsTheProbeItAbandons")]
        public async Task GetServiceHealthAsync_CancelsTheProbeItAbandons()
        {
            // The abandoned probe must not be left running against the client indefinitely.
            var observed = new TaskCompletionSource<bool>();
            var gateway = new Mock<IDownloadClientGateway>();
            gateway
                .Setup(g => g.TestConnectionAsync(It.IsAny<DownloadClientConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((DownloadClientConfiguration _, CancellationToken ct) =>
                {
                    ct.Register(() => observed.TrySetResult(true));
                    return new TaskCompletionSource<(bool Success, string Message)>().Task;
                });

            var systemService = CreateSystemService(gateway, Client("hangs", "transmission"));

            await systemService.GetServiceHealthAsync();

            var cancelled = await Task.WhenAny(observed.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(observed.Task, cancelled);
            Assert.True(await observed.Task);
        }

        [Fact]
        [Trait("Method", "GetServiceHealthAsync")]
        [Trait("Scenario", "ReportsUnknownWhenNoAdapterIsRegisteredForTheClientType")]
        public async Task GetServiceHealthAsync_ReportsUnknownWhenNoAdapterIsRegisteredForTheClientType()
        {
            // DownloadClientGateway.ResolveAdapter throws synchronously rather than returning
            // false. One unrecognised client type must not take out the whole endpoint.
            var gateway = new Mock<IDownloadClientGateway>();
            gateway
                .Setup(g => g.TestConnectionAsync(It.Is<DownloadClientConfiguration>(c => c.Name == "bogus"), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("No download client adapter registered for bogus."));
            gateway
                .Setup(g => g.TestConnectionAsync(It.Is<DownloadClientConfiguration>(c => c.Name == "fine"), It.IsAny<CancellationToken>()))
                .ReturnsAsync((true, "ok"));

            var systemService = CreateSystemService(gateway, Client("bogus", "not-a-real-type"), Client("fine", "qbittorrent"));

            var health = await systemService.GetServiceHealthAsync();

            Assert.Equal("unknown", Assert.Single(health.DownloadClients.Clients, c => c.Name == "bogus").Status);
            Assert.Equal("connected", Assert.Single(health.DownloadClients.Clients, c => c.Name == "fine").Status);
            Assert.Equal(1, health.DownloadClients.Connected);
            Assert.Equal(2, health.DownloadClients.Total);
        }

        [Fact]
        [Trait("Method", "GetServiceHealthAsync")]
        [Trait("Scenario", "ReportsWarningWhenSomeClientsAreDownAndOthersAreUnknown")]
        public async Task GetServiceHealthAsync_ReportsWarningWhenSomeClientsAreDownAndOthersAreUnknown()
        {
            // Nothing is confirmed working, but not everything is confirmed down either, so this
            // is not the "error" the all-unreachable case earns.
            var gateway = new Mock<IDownloadClientGateway>();
            gateway
                .Setup(g => g.TestConnectionAsync(It.Is<DownloadClientConfiguration>(c => c.Name == "down"), It.IsAny<CancellationToken>()))
                .ReturnsAsync((false, "Connection failed."));
            gateway
                .Setup(g => g.TestConnectionAsync(It.Is<DownloadClientConfiguration>(c => c.Name == "hangs"), It.IsAny<CancellationToken>()))
                .Returns(new TaskCompletionSource<(bool Success, string Message)>().Task);

            var systemService = CreateSystemService(gateway, Client("down", "qbittorrent"), Client("hangs", "sabnzbd"));

            var health = await systemService.GetServiceHealthAsync();

            Assert.Equal(0, health.DownloadClients.Connected);
            Assert.Equal("warning", health.DownloadClients.Status);
            Assert.Equal("warning", health.Status);
        }

        [Fact]
        [Trait("Method", "GetServiceHealthAsync")]
        [Trait("Scenario", "DoesNotProbeDisabledClients")]
        public async Task GetServiceHealthAsync_DoesNotProbeDisabledClients()
        {
            var gateway = new Mock<IDownloadClientGateway>();
            gateway
                .Setup(g => g.TestConnectionAsync(It.IsAny<DownloadClientConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((true, "ok"));

            var disabled = Client("disabled", "qbittorrent");
            disabled.IsEnabled = false;

            var systemService = CreateSystemService(gateway, Client("enabled", "qbittorrent"), disabled);

            var health = await systemService.GetServiceHealthAsync();

            Assert.Equal("enabled", Assert.Single(health.DownloadClients.Clients).Name);
            Assert.Equal(1, health.DownloadClients.Total);
            gateway.Verify(
                g => g.TestConnectionAsync(It.Is<DownloadClientConfiguration>(c => c.Name == "disabled"), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        [Trait("Method", "GetServiceHealthAsync")]
        [Trait("Scenario", "ProbesEveryEnabledClientExactlyOnce")]
        public async Task GetServiceHealthAsync_ProbesEveryEnabledClientExactlyOnce()
        {
            // Guards the whole point: a suite that passes whether or not the probe runs is
            // worthless here, so assert the probe is actually reached.
            var gateway = new Mock<IDownloadClientGateway>();
            gateway
                .Setup(g => g.TestConnectionAsync(It.IsAny<DownloadClientConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((true, "ok"));

            var systemService = CreateSystemService(gateway, Client("first", "qbittorrent"), Client("second", "nzbget"));

            await systemService.GetServiceHealthAsync();

            gateway.Verify(
                g => g.TestConnectionAsync(It.IsAny<DownloadClientConfiguration>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        [Fact]
        [Trait("Method", "GetServiceHealthAsync")]
        [Trait("Scenario", "CarriesTheClientTypeThroughToThePayload")]
        public async Task GetServiceHealthAsync_CarriesTheClientTypeThroughToThePayload()
        {
            var gateway = new Mock<IDownloadClientGateway>();
            gateway
                .Setup(g => g.TestConnectionAsync(It.IsAny<DownloadClientConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((true, "ok"));

            var systemService = CreateSystemService(gateway, Client("only", "transmission"));

            var health = await systemService.GetServiceHealthAsync();

            var client = Assert.Single(health.DownloadClients.Clients);
            Assert.Equal("only", client.Name);
            Assert.Equal("transmission", client.Type);
        }

        [Fact]
        [Trait("Method", "GetServiceHealthAsync")]
        [Trait("Scenario", "ReportsHealthyWhenNoClientsAreConfigured")]
        public async Task GetServiceHealthAsync_ReportsHealthyWhenNoClientsAreConfigured()
        {
            var gateway = new Mock<IDownloadClientGateway>();

            var systemService = CreateSystemService(gateway);

            var health = await systemService.GetServiceHealthAsync();

            Assert.Empty(health.DownloadClients.Clients);
            Assert.Equal(0, health.DownloadClients.Total);
            Assert.Equal("healthy", health.DownloadClients.Status);
            Assert.Equal("healthy", health.Status);
            gateway.Verify(
                g => g.TestConnectionAsync(It.IsAny<DownloadClientConfiguration>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        [Trait("Method", "Constructor")]
        [Trait("Scenario", "ResolvesFromTheServiceProvider")]
        public void SystemService_ResolvesFromTheServiceProvider()
        {
            // The probe timeout is an optional constructor parameter. This asserts the container
            // can still build SystemService, which a plain "new" in the other tests would not.
            var services = new ServiceCollection();
            services.AddSingleton(new Mock<IConfigurationService>().Object);
            services.AddSingleton<ILogger<SystemService>>(NullLogger<SystemService>.Instance);
            services.AddSingleton(new Mock<IApplicationPathService>().Object);
            services.AddSingleton(new Mock<IApplicationVersionService>().Object);
            services.AddSingleton(new Mock<IRootFolderService>().Object);
            services.AddSingleton<IDiskSpaceProbe>(new DiskSpaceProbe(NullLogger<DiskSpaceProbe>.Instance));
            services.AddSingleton(new Mock<IDownloadClientGateway>().Object);
            services.AddScoped<ISystemService, SystemService>();

            using var provider = services.BuildServiceProvider();

            Assert.IsType<SystemService>(provider.GetRequiredService<ISystemService>());
        }

        private static DownloadClientConfiguration Client(string name, string type) =>
            new() { Name = name, Type = type, IsEnabled = true };

        private static SystemService CreateSystemService(
            Mock<IDownloadClientGateway> gateway,
            params DownloadClientConfiguration[] clients)
        {
            var configurationService = new Mock<IConfigurationService>();
            configurationService
                .Setup(service => service.GetApiConfigurationsAsync())
                .ReturnsAsync(new List<ApiConfiguration>());
            configurationService
                .Setup(service => service.GetDownloadClientConfigurationsAsync())
                .ReturnsAsync(clients.ToList());

            var applicationVersionService = new Mock<IApplicationVersionService>();
            applicationVersionService.Setup(service => service.Resolve()).Returns("test");

            var rootFolderService = new Mock<IRootFolderService>();
            rootFolderService.Setup(service => service.GetAllAsync()).ReturnsAsync(new List<RootFolder>());

            return new SystemService(
                configurationService.Object,
                NullLogger<SystemService>.Instance,
                new Mock<IApplicationPathService>().Object,
                applicationVersionService.Object,
                rootFolderService.Object,
                new DiskSpaceProbe(NullLogger<DiskSpaceProbe>.Instance),
                gateway.Object,
                ShortProbeTimeout);
        }
    }
}
