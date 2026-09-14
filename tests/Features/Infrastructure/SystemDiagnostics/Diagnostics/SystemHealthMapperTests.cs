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
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.SystemDiagnostics.Diagnostics
{
    [Trait("Area", "Infrastructure")]
    [Trait("Name", "SystemHealthMapperTests")]
    [Trait("Category", "SystemHealthMapper")]
    public class SystemHealthMapperTests : BaseTests
    {
        [Fact]
        [Trait("Method", "BuildDownloadClientHealth")]
        [Trait("Scenario", "ClientWithFailureReasonReportsDisconnected")]
        public void BuildDownloadClientHealth_ClientWithFailureReason_ReportsDisconnected()
        {
            var client = MakeClient("client-1", "qBittorrent", "qbittorrent");
            var polled = new List<QueueClientStatus>
            {
                MakePolledStatus(client.Id, snapshotState: "unavailable", failureReason: "timeout")
            };

            var health = SystemHealthMapper.BuildDownloadClientHealth(new[] { client }, polled);

            var status = Assert.Single(health.Clients);
            Assert.Equal("disconnected", status.Status);
            Assert.Equal("timeout", status.FailureReason);
            Assert.Equal(0, health.Connected);
            Assert.Equal(1, health.Total);
            Assert.Equal("error", health.Status);
        }

        [Fact]
        [Trait("Method", "BuildDownloadClientHealth")]
        [Trait("Scenario", "ClientServedFromCacheAfterFailedProbeReportsDisconnected")]
        public void BuildDownloadClientHealth_ClientServedFromCacheAfterFailedProbe_ReportsDisconnected()
        {
            // A "cached" snapshot state means the live probe for this cycle failed and
            // a recent fallback snapshot was used to keep the queue view populated. The
            // health endpoint must not read that as "connected" - the failure reason
            // recorded on the same cycle is what actually happened just now.
            var client = MakeClient("client-1", "Sabnzbd", "sabnzbd");
            var polled = new List<QueueClientStatus>
            {
                MakePolledStatus(client.Id, snapshotState: "cached", failureReason: "error")
            };

            var health = SystemHealthMapper.BuildDownloadClientHealth(new[] { client }, polled);

            var status = Assert.Single(health.Clients);
            Assert.Equal("disconnected", status.Status);
            Assert.Equal("error", status.FailureReason);
        }

        [Fact]
        [Trait("Method", "BuildDownloadClientHealth")]
        [Trait("Scenario", "ReachableClientReportsConnected")]
        public void BuildDownloadClientHealth_ReachableClient_ReportsConnected()
        {
            var client = MakeClient("client-1", "Transmission", "transmission");
            var polled = new List<QueueClientStatus>
            {
                MakePolledStatus(client.Id, snapshotState: "live", failureReason: null)
            };

            var health = SystemHealthMapper.BuildDownloadClientHealth(new[] { client }, polled);

            var status = Assert.Single(health.Clients);
            Assert.Equal("connected", status.Status);
            Assert.Null(status.FailureReason);
            Assert.Equal(1, health.Connected);
            Assert.Equal(1, health.Total);
            Assert.Equal("healthy", health.Status);
        }

        [Fact]
        [Trait("Method", "BuildDownloadClientHealth")]
        [Trait("Scenario", "ClientWithNoPollDataReportsUnknown")]
        public void BuildDownloadClientHealth_ClientWithNoPollData_ReportsUnknown()
        {
            // No poll cycle has covered this client yet (background poller hasn't run,
            // or the client was just added). This must not be silently reported as
            // connected the way the hardcoded literal used to.
            var client = MakeClient("client-1", "Nzbget", "nzbget");

            var health = SystemHealthMapper.BuildDownloadClientHealth(new[] { client }, new List<QueueClientStatus>());

            var status = Assert.Single(health.Clients);
            Assert.Equal("unknown", status.Status);
            Assert.Null(status.FailureReason);
            Assert.Equal(0, health.Connected);
        }

        [Fact]
        [Trait("Method", "BuildDownloadClientHealth")]
        [Trait("Scenario", "NullPolledStatusesTreatedAsNoData")]
        public void BuildDownloadClientHealth_NullPolledStatuses_TreatedAsNoData()
        {
            var client = MakeClient("client-1", "qBittorrent", "qbittorrent");

            var health = SystemHealthMapper.BuildDownloadClientHealth(new[] { client }, null);

            var status = Assert.Single(health.Clients);
            Assert.Equal("unknown", status.Status);
        }

        [Fact]
        [Trait("Method", "BuildDownloadClientHealth")]
        [Trait("Scenario", "DisabledClientsExcludedFromOutputAndTotals")]
        public void BuildDownloadClientHealth_DisabledClients_ExcludedFromOutputAndTotals()
        {
            var enabled = MakeClient("client-1", "qBittorrent", "qbittorrent");
            var disabled = MakeClient("client-2", "Transmission", "transmission");
            disabled.IsEnabled = false;

            var polled = new List<QueueClientStatus>
            {
                MakePolledStatus(enabled.Id, snapshotState: "live", failureReason: null),
                MakePolledStatus(disabled.Id, snapshotState: "live", failureReason: null)
            };

            var health = SystemHealthMapper.BuildDownloadClientHealth(new[] { enabled, disabled }, polled);

            var status = Assert.Single(health.Clients);
            Assert.Equal(enabled.Name, status.Name);
            Assert.Equal(1, health.Total);
        }

        [Fact]
        [Trait("Method", "BuildDownloadClientHealth")]
        [Trait("Scenario", "MixOfConnectedAndDisconnectedReportsWarning")]
        public void BuildDownloadClientHealth_MixOfConnectedAndDisconnected_ReportsWarning()
        {
            var up = MakeClient("client-1", "qBittorrent", "qbittorrent");
            var down = MakeClient("client-2", "Transmission", "transmission");

            var polled = new List<QueueClientStatus>
            {
                MakePolledStatus(up.Id, snapshotState: "live", failureReason: null),
                MakePolledStatus(down.Id, snapshotState: "unavailable", failureReason: "error")
            };

            var health = SystemHealthMapper.BuildDownloadClientHealth(new[] { up, down }, polled);

            Assert.Equal(1, health.Connected);
            Assert.Equal(2, health.Total);
            Assert.Equal("warning", health.Status);
        }

        [Fact]
        [Trait("Method", "BuildServiceHealth")]
        [Trait("Scenario", "DownloadClientErrorPropagatesToOverallStatus")]
        public void BuildServiceHealth_DownloadClientError_PropagatesToOverallStatus()
        {
            var downloadClientHealth = new DownloadClientHealth { Status = "error", Connected = 0, Total = 1 };
            var externalApiHealth = new ExternalApiHealth { Status = "healthy", Connected = 0, Total = 0 };

            var serviceHealth = SystemHealthMapper.BuildServiceHealth("1.0.0", "1h", downloadClientHealth, externalApiHealth);

            Assert.Equal("error", serviceHealth.Status);
        }

        private static DownloadClientConfiguration MakeClient(string id, string name, string type)
        {
            return new DownloadClientConfiguration
            {
                Id = id,
                Name = name,
                Type = type,
                IsEnabled = true
            };
        }

        private static QueueClientStatus MakePolledStatus(string clientId, string snapshotState, string? failureReason)
        {
            return new QueueClientStatus
            {
                ClientId = clientId,
                ClientName = clientId,
                ClientType = "unknown",
                SnapshotState = snapshotState,
                SnapshotFailureReason = failureReason,
                IsUnavailable = snapshotState == "unavailable",
                IsStaleSnapshot = snapshotState == "cached"
            };
        }
    }
}
