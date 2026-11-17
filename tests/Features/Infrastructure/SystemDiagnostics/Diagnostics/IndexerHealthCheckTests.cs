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

using Listenarr.Domain.SystemDiagnostics;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Infrastructure.Search.RequestGate;
using Listenarr.Tests.Common;
using Listenarr.Tests.Features.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.SystemDiagnostics.Diagnostics
{
    /// <summary>
    /// The indexer health check reads the request gate's non-charging deferral peek: an enabled
    /// indexer a search would be held back from is named, with when its cooldown ends and why.
    /// </summary>
    /// <remarks>
    /// Indexer names are invented. The clock is fixed so every cooldown end below is exact.
    /// </remarks>
    [Trait("Area", "Infrastructure")]
    [Trait("Name", "IndexerHealthCheckTests")]
    [Trait("Category", "IndexerHealth")]
    public sealed class IndexerHealthCheckTests : BaseTests
    {
        private static readonly DateTime Now = new(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);
        private static readonly RequestDeferral RateLimited = new(Now.AddHours(1), RequestDeferralReason.RateLimited);

        [Fact]
        [Trait("Method", "BuildIndexerHealth")]
        [Trait("Scenario", "NoCooldown")]
        public void BuildIndexerHealth_NoIndexerInCooldown_FiresNoCheck()
        {
            var indexers = new[] { Enabled(1, "Alderbrook"), Enabled(2, "Birchfield") };

            var health = IndexerHealthMapper.BuildIndexerHealth(indexers, new Dictionary<int, RequestDeferral>());

            Assert.Equal(("healthy", 2, 2), (health.Status, health.Available, health.Total));
            Assert.Empty(health.Checks);
            Assert.Empty(health.Cooldowns);
        }

        [Fact]
        [Trait("Method", "BuildIndexerHealth")]
        [Trait("Scenario", "OneOfTwoInCooldown")]
        public void BuildIndexerHealth_OneOfTwoInCooldown_WarnsNamingIt_WithUntilAndReason()
        {
            var indexers = new[] { Enabled(1, "Alderbrook"), Enabled(2, "Birchfield") };

            var health = IndexerHealthMapper.BuildIndexerHealth(indexers, new Dictionary<int, RequestDeferral> { [1] = RateLimited });

            var check = Assert.Single(health.Checks);
            Assert.Equal(IndexerHealthCheck.StatusKind, check.Kind);
            Assert.Equal("warning", check.Status);
            Assert.Equal(new[] { "Alderbrook" }, check.IndexerNames);
            Assert.Equal("Indexers unavailable while in a request cooldown: Alderbrook", check.Message);
            var cooldown = Assert.Single(health.Cooldowns);
            Assert.Equal((1, "Alderbrook", Now.AddHours(1), "RateLimited"), (cooldown.IndexerId, cooldown.Name, cooldown.UntilUtc, cooldown.Reason));
            Assert.Equal(("warning", 1, 2), (health.Status, health.Available, health.Total));
        }

        [Fact]
        [Trait("Method", "BuildIndexerHealth")]
        [Trait("Scenario", "EveryIndexerInCooldown")]
        public void BuildIndexerHealth_EveryEnabledIndexerInCooldown_IsAnError()
        {
            var indexers = new[] { Enabled(1, "Alderbrook"), Enabled(2, "Birchfield") };
            var failing = new RequestDeferral(Now.AddMinutes(5), RequestDeferralReason.FailureLadder);

            var health = IndexerHealthMapper.BuildIndexerHealth(
                indexers,
                new Dictionary<int, RequestDeferral> { [1] = RateLimited, [2] = failing });

            var check = Assert.Single(health.Checks);
            Assert.Equal("error", check.Status);
            Assert.Equal("All indexers are unavailable: each is in a request cooldown", check.Message);
            Assert.Equal(new[] { "RateLimited", "FailureLadder" }, health.Cooldowns.Select(c => c.Reason));
            Assert.Equal(("error", 0, 2), (health.Status, health.Available, health.Total));
        }

        [Fact]
        [Trait("Method", "BuildIndexerHealth")]
        [Trait("Scenario", "DisabledIndexerInCooldown")]
        public void BuildIndexerHealth_DisabledIndexerInCooldown_IsNotCounted()
        {
            var disabled = Enabled(2, "Birchfield");
            disabled.IsEnabled = false;

            var health = IndexerHealthMapper.BuildIndexerHealth(
                new[] { Enabled(1, "Alderbrook"), disabled },
                new Dictionary<int, RequestDeferral> { [2] = RateLimited });

            Assert.Equal(("healthy", 1, 1), (health.Status, health.Available, health.Total));
            Assert.Empty(health.Checks);
        }

        [Fact]
        [Trait("Method", "GetServiceHealthAsync")]
        [Trait("Scenario", "PersistedCooldownThroughRealGate")]
        public async Task GetServiceHealthAsync_PersistedCooldown_WarnsWithUntilAndReasonFromTheRealGate()
        {
            // Given: a cooldown a previous process left in IndexerRequestStates, read by a new
            // cache and gate as after a restart.
            await using var database = await IndexerRequestStateTestDatabase.CreateAsync(indexerCount: 2);
            var coolingId = database.IndexerIds[0];
            var store = new EfIndexerRequestStateStore(database.Factory);
            await store.UpsertAsync(new IndexerRequestState
            {
                IndexerId = coolingId,
                CooldownUntilUtc = Now.AddMinutes(90),
                CooldownReason = nameof(RequestDeferralReason.RateLimited),
                UpdatedUtc = Now
            });
            var gate = new IndexerRequestGate(
                new IndexerRequestStateCache(store, NullLogger<IndexerRequestStateCache>.Instance),
                [],
                new MutableTimeProvider(new DateTimeOffset(Now)),
                NullLogger<IndexerRequestGate>.Instance);
            await using var context = database.CreateContext();

            // When
            var health = await CreateSystemService(new EfIndexerRepository(context), gate).GetServiceHealthAsync();

            // Then
            var cooldown = Assert.Single(health.Indexers.Cooldowns);
            Assert.Equal(
                (coolingId, "Indexer 1", Now.AddMinutes(90), "RateLimited"),
                (cooldown.IndexerId, cooldown.Name, cooldown.UntilUtc, cooldown.Reason));
            Assert.Equal("warning", Assert.Single(health.Indexers.Checks).Status);
            Assert.Equal(("warning", 1, 2), (health.Indexers.Status, health.Indexers.Available, health.Indexers.Total));
            Assert.Equal("warning", health.Status);
        }

        [Fact]
        [Trait("Method", "GetServiceHealthAsync")]
        [Trait("Scenario", "GateReportsNoDeferral")]
        public async Task GetServiceHealthAsync_GateReportsNoDeferral_IsHealthy()
        {
            // Control for the test above: the same indexers behind a gate that defers nothing.
            await using var database = await IndexerRequestStateTestDatabase.CreateAsync(indexerCount: 2);
            await using var context = database.CreateContext();

            var health = await CreateSystemService(new EfIndexerRepository(context), new PassThroughIndexerRequestGate(new MutableTimeProvider(new DateTimeOffset(Now)))).GetServiceHealthAsync();

            Assert.Equal(("healthy", 2, 2), (health.Indexers.Status, health.Indexers.Available, health.Indexers.Total));
            Assert.Empty(health.Indexers.Checks);
            Assert.Empty(health.Indexers.Cooldowns);
        }

        private static Indexer Enabled(int id, string name) => new()
        {
            Id = id,
            Name = name,
            IsEnabled = true,
            Implementation = "Torznab",
            Url = "https://indexer.example.invalid"
        };

        private static SystemService CreateSystemService(IIndexerRepository indexerRepository, IIndexerRequestGate gate)
        {
            var configurationService = new Mock<IConfigurationService>();
            configurationService
                .Setup(service => service.GetApiConfigurationsAsync())
                .ReturnsAsync(new List<ApiConfiguration>());
            configurationService
                .Setup(service => service.GetDownloadClientConfigurationsAsync())
                .ReturnsAsync(new List<DownloadClientConfiguration>());
            var applicationVersionService = new Mock<IApplicationVersionService>();
            applicationVersionService.Setup(service => service.Resolve()).Returns("1.0.0");

            return new SystemService(
                configurationService.Object,
                NullLogger<SystemService>.Instance,
                new Mock<IApplicationPathService>().Object,
                applicationVersionService.Object,
                new Mock<IRootFolderService>().Object,
                new DiskSpaceProbe(NullLogger<DiskSpaceProbe>.Instance),
                new DownloadClientStatusCache(),
                indexerRepository,
                gate);
        }
    }
}
