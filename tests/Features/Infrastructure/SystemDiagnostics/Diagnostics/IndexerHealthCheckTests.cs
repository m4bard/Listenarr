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
using Listenarr.Infrastructure.SystemDiagnostics.Diagnostics;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.SystemDiagnostics.Diagnostics
{
    /// <summary>
    /// The two indexer health checks: an indexer in a failure-backoff cooldown whose failure run
    /// began recently, and one whose run began more than six hours ago. The second is the one that
    /// says nobody is watching, so it is the one that has to be seen to fire here: in ordinary use
    /// nobody waits six hours to find out whether a six-hour boundary works.
    /// </summary>
    /// <remarks>
    /// Indexer names are invented. The clock is fixed so every "hours ago" below is exact.
    /// </remarks>
    [Trait("Area", "Infrastructure")]
    [Trait("Name", "IndexerHealthCheckTests")]
    [Trait("Category", "IndexerHealth")]
    public sealed class IndexerHealthCheckTests : BaseTests
    {
        private static readonly DateTime Now = new(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        [Trait("Method", "BuildIndexerHealth")]
        [Trait("Scenario", "EveryIndexerHealthy")]
        public void BuildIndexerHealth_EveryIndexerHealthy_FiresNoCheck()
        {
            // Given: two indexers that have never failed. A health view that warns when nothing is
            // wrong teaches the operator to ignore it.
            var indexers = new[] { Healthy(1, "Alderbrook"), Healthy(2, "Birchfield") };

            // When
            var health = IndexerHealthMapper.BuildIndexerHealth(indexers, Now);

            // Then
            Assert.Equal("healthy", health.Status);
            Assert.Equal(2, health.Available);
            Assert.Equal(2, health.Total);
            Assert.Empty(health.Checks);
        }

        [Fact]
        [Trait("Method", "BuildIndexerHealth")]
        [Trait("Scenario", "OneIndexerRecentlyBlocked")]
        public void BuildIndexerHealth_OneIndexerBlockedForHalfAnHour_WarnsNearTermNamingIt()
        {
            // Given: one indexer whose failure run began thirty minutes ago, one healthy
            var indexers = new[]
            {
                Blocked(1, "Alderbrook", runBegan: Now.AddMinutes(-30)),
                Healthy(2, "Birchfield")
            };

            // When
            var health = IndexerHealthMapper.BuildIndexerHealth(indexers, Now);

            // Then: exactly the near-term check, a warning, naming only the blocked indexer
            var check = Assert.Single(health.Checks);
            Assert.Equal(IndexerHealthCheck.NearTermKind, check.Kind);
            Assert.Equal("warning", check.Status);
            Assert.Equal(new[] { "Alderbrook" }, check.IndexerNames);
            Assert.Equal("Indexers unavailable due to failures: Alderbrook", check.Message);
            Assert.Equal("warning", health.Status);
            Assert.Equal(1, health.Available);
            Assert.Equal(2, health.Total);
        }

        [Fact]
        [Trait("Method", "BuildIndexerHealth")]
        [Trait("Scenario", "FailureRunBackdatedSevenHours")]
        public void BuildIndexerHealth_FailureRunBeganSevenHoursAgo_NearTermQuietLongTermFires()
        {
            // Given: the same shape, backdated past the boundary. The block itself is fresh (the
            // indexer failed again on its last probe); what is old is the run.
            var indexers = new[]
            {
                Blocked(1, "Alderbrook", runBegan: Now.AddHours(-7)),
                Healthy(2, "Birchfield")
            };

            // When
            var health = IndexerHealthMapper.BuildIndexerHealth(indexers, Now);

            // Then
            var check = Assert.Single(health.Checks);
            Assert.Equal(IndexerHealthCheck.LongTermKind, check.Kind);
            Assert.Equal("warning", check.Status);
            Assert.Equal(new[] { "Alderbrook" }, check.IndexerNames);
            Assert.Equal("Indexers unavailable due to failures for more than 6 hours: Alderbrook", check.Message);
            Assert.DoesNotContain(health.Checks, c => c.Kind == IndexerHealthCheck.NearTermKind);
        }

        [Theory]
        [Trait("Method", "BuildIndexerHealth")]
        [Trait("Scenario", "SixHourBoundary")]
        [InlineData(-359, IndexerHealthCheck.NearTermKind)] // a minute inside
        [InlineData(-360, IndexerHealthCheck.LongTermKind)] // exactly on it: counted as long-term
        [InlineData(-361, IndexerHealthCheck.LongTermKind)] // a minute past
        public void BuildIndexerHealth_AtTheSixHourBoundary_ExactlyOneCheckClaimsTheIndexer(int runBeganMinutes, string expectedKind)
        {
            // Given: a run that began within a minute of six hours ago. Each indexer is in exactly
            // one of the two checks; a boundary that both or neither claims hides an outage.
            var indexers = new[] { Blocked(1, "Alderbrook", runBegan: Now.AddMinutes(runBeganMinutes)), Healthy(2, "Birchfield") };

            // When
            var health = IndexerHealthMapper.BuildIndexerHealth(indexers, Now);

            // Then
            var check = Assert.Single(health.Checks);
            Assert.Equal(expectedKind, check.Kind);
        }

        [Fact]
        [Trait("Method", "BuildIndexerHealth")]
        [Trait("Scenario", "EveryIndexerBlockedNearTerm")]
        public void BuildIndexerHealth_EveryEnabledIndexerBlockedRecently_IsAnError()
        {
            // Given: nothing left to ask
            var indexers = new[]
            {
                Blocked(1, "Alderbrook", runBegan: Now.AddMinutes(-10)),
                Blocked(2, "Birchfield", runBegan: Now.AddMinutes(-40))
            };

            // When
            var health = IndexerHealthMapper.BuildIndexerHealth(indexers, Now);

            // Then: error, not warning, with the all-indexers wording
            var check = Assert.Single(health.Checks);
            Assert.Equal(IndexerHealthCheck.NearTermKind, check.Kind);
            Assert.Equal("error", check.Status);
            Assert.Equal("All indexers are unavailable due to failures", check.Message);
            Assert.Equal(new[] { "Alderbrook", "Birchfield" }, check.IndexerNames);
            Assert.Equal("error", health.Status);
            Assert.Equal(0, health.Available);
        }

        [Fact]
        [Trait("Method", "BuildIndexerHealth")]
        [Trait("Scenario", "EveryIndexerBlockedLongTerm")]
        public void BuildIndexerHealth_EveryEnabledIndexerBlockedForHours_IsALongTermError()
        {
            var indexers = new[]
            {
                Blocked(1, "Alderbrook", runBegan: Now.AddHours(-9)),
                Blocked(2, "Birchfield", runBegan: Now.AddHours(-26))
            };

            var health = IndexerHealthMapper.BuildIndexerHealth(indexers, Now);

            var check = Assert.Single(health.Checks);
            Assert.Equal(IndexerHealthCheck.LongTermKind, check.Kind);
            Assert.Equal("error", check.Status);
            Assert.Equal("All indexers are unavailable due to failures for more than 6 hours", check.Message);
            Assert.Equal("error", health.Status);
        }

        [Fact]
        [Trait("Method", "BuildIndexerHealth")]
        [Trait("Scenario", "EveryIndexerBlockedSplitAcrossBoundary")]
        public void BuildIndexerHealth_EveryIndexerBlockedButSplitAcrossTheBoundary_OverallStatusIsStillError()
        {
            // Given: one near-term, one long-term. Neither check on its own covers every indexer, so
            // each is only a warning, as in the family's checks. The aggregate must still say
            // nothing is left, or the all-blocked case goes quiet whenever the runs began at
            // different times.
            var indexers = new[]
            {
                Blocked(1, "Alderbrook", runBegan: Now.AddMinutes(-20)),
                Blocked(2, "Birchfield", runBegan: Now.AddHours(-8))
            };

            var health = IndexerHealthMapper.BuildIndexerHealth(indexers, Now);

            Assert.Equal(2, health.Checks.Count);
            Assert.All(health.Checks, c => Assert.Equal("warning", c.Status));
            Assert.Equal(new[] { "Alderbrook" }, health.Checks.Single(c => c.Kind == IndexerHealthCheck.NearTermKind).IndexerNames);
            Assert.Equal(new[] { "Birchfield" }, health.Checks.Single(c => c.Kind == IndexerHealthCheck.LongTermKind).IndexerNames);
            Assert.Equal("error", health.Status);
            Assert.Equal(0, health.Available);
        }

        [Fact]
        [Trait("Method", "BuildIndexerHealth")]
        [Trait("Scenario", "CooldownExpiredOrWalkingDown")]
        public void BuildIndexerHealth_OldFailureRunButNoCooldownRunning_FiresNoCheck()
        {
            // Given: two indexers with an old failure run still on record but not blocked now. One's
            // cooldown ran out a minute ago; the other answered and is walking back down the ladder.
            // Both are being asked, so neither is unavailable.
            var expired = Blocked(1, "Alderbrook", runBegan: Now.AddHours(-10));
            expired.DisabledTill = Now.AddMinutes(-1);
            var walkingDown = Blocked(2, "Birchfield", runBegan: Now.AddHours(-10));
            walkingDown.DisabledTill = null;
            walkingDown.EscalationLevel = 3;

            var health = IndexerHealthMapper.BuildIndexerHealth(new[] { expired, walkingDown }, Now);

            Assert.Empty(health.Checks);
            Assert.Equal("healthy", health.Status);
            Assert.Equal(2, health.Available);
        }

        [Fact]
        [Trait("Method", "BuildIndexerHealth")]
        [Trait("Scenario", "DisabledIndexerIgnored")]
        public void BuildIndexerHealth_DisabledIndexerWithAStaleCooldown_IsNeitherCountedNorReported()
        {
            // Given: an indexer the operator switched off while it was blocked. It is not asked
            // anything, so its cooldown is not a health problem.
            var switchedOff = Blocked(1, "Alderbrook", runBegan: Now.AddHours(-12));
            switchedOff.IsEnabled = false;

            var health = IndexerHealthMapper.BuildIndexerHealth(new[] { switchedOff, Healthy(2, "Birchfield") }, Now);

            Assert.Empty(health.Checks);
            Assert.Equal(1, health.Total);
            Assert.Equal(1, health.Available);
            Assert.Equal("healthy", health.Status);
        }

        [Fact]
        [Trait("Method", "BuildIndexerHealth")]
        [Trait("Scenario", "BlockedWithoutRunStart")]
        public void BuildIndexerHealth_BlockedButNoRecordedRunStart_IsStillReportedNearTerm()
        {
            // Given: a cooldown with no InitialFailure. The ladder never writes this, but a row can
            // carry it (a hand-edited database, a partial write). The family's checks would drop it
            // from both; a blocked indexer missing from health is exactly the silence this exists
            // to end, so it is reported as a current failure instead.
            var orphan = Blocked(1, "Alderbrook", runBegan: Now);
            orphan.InitialFailure = null;

            var health = IndexerHealthMapper.BuildIndexerHealth(new[] { orphan, Healthy(2, "Birchfield") }, Now);

            var check = Assert.Single(health.Checks);
            Assert.Equal(IndexerHealthCheck.NearTermKind, check.Kind);
            Assert.Equal(new[] { "Alderbrook" }, check.IndexerNames);
        }

        [Fact]
        [Trait("Method", "BuildIndexerHealth")]
        [Trait("Scenario", "NoIndexers")]
        public void BuildIndexerHealth_NoIndexersConfigured_IsHealthyWithNothingToReport()
        {
            var health = IndexerHealthMapper.BuildIndexerHealth(Array.Empty<Indexer>(), Now);

            Assert.Equal("healthy", health.Status);
            Assert.Equal(0, health.Total);
            Assert.Empty(health.Checks);
        }

        [Theory]
        [Trait("Method", "BuildServiceHealth")]
        [Trait("Scenario", "IndexerStatusPropagates")]
        [InlineData("warning", "warning")]
        [InlineData("error", "error")]
        [InlineData("healthy", "healthy")]
        public void BuildServiceHealth_IndexerStatus_PropagatesToOverallStatus(string indexerStatus, string expectedOverall)
        {
            var downloadClients = new DownloadClientHealth { Status = "healthy" };
            var externalApis = new ExternalApiHealth { Status = "healthy" };
            var indexers = new IndexerHealth { Status = indexerStatus };

            var serviceHealth = SystemHealthMapper.BuildServiceHealth("1.0.0", "1h", downloadClients, externalApis, indexers);

            Assert.Equal(expectedOverall, serviceHealth.Status);
            Assert.Same(indexers, serviceHealth.Indexers);
        }

        [Fact]
        [Trait("Method", "GetServiceHealthAsync")]
        [Trait("Scenario", "ReadsIndexersAtTheServiceClock")]
        public async Task GetServiceHealthAsync_ReadsIndexersFromTheRepositoryAtTheServiceClock()
        {
            // Given: a run that began seven hours before the service's clock. Evaluated against the
            // real clock instead, the fixed 2026 date would put it in the past or future by an
            // arbitrary amount, so the long-term kind here proves the injected clock was used.
            var repository = new Mock<IIndexerRepository>();
            repository
                .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Indexer>
                {
                    Blocked(1, "Alderbrook", runBegan: Now.AddHours(-7)),
                    Healthy(2, "Birchfield")
                });

            var service = CreateSystemService(repository.Object, new FixedClock(Now));

            var health = await service.GetServiceHealthAsync();

            var check = Assert.Single(health.Indexers.Checks);
            Assert.Equal(IndexerHealthCheck.LongTermKind, check.Kind);
            Assert.Equal("warning", health.Indexers.Status);
            Assert.Equal("warning", health.Status);
        }

        [Fact]
        [Trait("Method", "GetServiceHealthAsync")]
        [Trait("Scenario", "RepositoryFailureIsAnError")]
        public async Task GetServiceHealthAsync_IndexerRepositoryThrows_ReportsIndexerHealthErrorWithoutFailingTheRest()
        {
            var repository = new Mock<IIndexerRepository>();
            repository
                .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("database is locked"));

            var service = CreateSystemService(repository.Object, new FixedClock(Now));

            var health = await service.GetServiceHealthAsync();

            Assert.Equal("error", health.Indexers.Status);
            Assert.Equal("error", health.Status);
            // Control: the rest of the report still came back
            Assert.Equal("healthy", health.DownloadClients.Status);
        }

        private static Indexer Healthy(int id, string name) => new()
        {
            Id = id,
            Name = name,
            IsEnabled = true
        };

        private static Indexer Blocked(int id, string name, DateTime runBegan) => new()
        {
            Id = id,
            Name = name,
            IsEnabled = true,
            InitialFailure = runBegan,
            MostRecentFailure = Now.AddSeconds(-30),
            EscalationLevel = 4,
            DisabledTill = Now.AddMinutes(25),
            LastFailureReason = "Timeout"
        };

        private static SystemService CreateSystemService(IIndexerRepository indexerRepository, TimeProvider clock)
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
                clock);
        }

        private sealed class FixedClock(DateTime utcNow) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
        }
    }
}
