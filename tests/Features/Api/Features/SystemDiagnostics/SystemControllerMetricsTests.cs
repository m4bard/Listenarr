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

using System.Net;
using Listenarr.Domain.SystemDiagnostics;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Api.Features.SystemDiagnostics
{
    [Trait("Name", "SystemControllerMetricsTests")]
    [Trait("Category", "Api")]
    public sealed class SystemControllerMetricsTests : BaseTests, IClassFixture<ListenarrWebApplicationFactory>
    {
        private readonly ListenarrWebApplicationFactory _factory;

        public SystemControllerMetricsTests(ListenarrWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        [Trait("Method", "GetMetrics")]
        [Trait("Scenario", "ReturnsProviderSnapshot")]
        public void GetMetrics_ReturnsTheProviderSnapshot()
        {
            // Given: a provider stub with a canned snapshot
            var expected = new MetricsSnapshot
            {
                CapturedAtUtc = DateTime.UtcNow,
                Counters = new Dictionary<string, double> { ["worker.cycle.completed"] = 7 },
                Gauges = new Dictionary<string, double> { ["queue.depth"] = 3 },
                Timings = new Dictionary<string, MetricTimingSummary>
                {
                    ["scan.duration"] = new MetricTimingSummary { Count = 2, SumMs = 40, MinMs = 10, MaxMs = 30 }
                }
            };
            var metricsProvider = new Mock<IAppMetricsSnapshotProvider>(MockBehavior.Strict);
            metricsProvider.Setup(provider => provider.GetSnapshot()).Returns(expected);
            var controller = new SystemController(
                Mock.Of<ISystemService>(),
                Mock.Of<ISystemReadinessService>(),
                metricsProvider.Object,
                NullLogger<SystemController>.Instance,
                Mock.Of<IFileSystem>());

            // When
            var result = controller.GetMetrics();

            // Then
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Same(expected, Assert.IsType<MetricsSnapshot>(ok.Value));
            metricsProvider.VerifyAll();
        }

        [Fact]
        [Trait("Method", "GetMetrics")]
        [Trait("Scenario", "RequiresAuthenticationWhenEnabled")]
        public async Task GetMetrics_Returns401_WhenUnauthenticated_AndAuthRequired()
        {
            // Given: authentication turned on, matching the family of AuthenticationEnforcerMiddleware
            // tests for other non-anonymous endpoints (e.g. /library, /configuration/startupconfig).
            using var authenticatedFactory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IStartupConfigService>(_ =>
                        new StartupConfigServiceMock(new StartupConfig { AuthenticationRequired = "Enabled" }));
                });
            });
            var apiBase = TestUtils.ResolveApiBasePath(authenticatedFactory.Services);
            using var client = authenticatedFactory.CreateClient(
                new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // When: the metrics endpoint is hit with no session and no API key
            using var response = await client.GetAsync($"{apiBase}/system/metrics");

            // Then: it is rejected the same way every other non-anonymous endpoint is, proving
            // this endpoint was not given [AllowAnonymous].
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
