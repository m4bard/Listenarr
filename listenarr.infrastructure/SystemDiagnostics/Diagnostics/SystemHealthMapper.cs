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

namespace Listenarr.Infrastructure.SystemDiagnostics.Diagnostics
{
    internal static class SystemHealthMapper
    {
        public static ServiceHealth BuildServiceHealth(
            string version,
            string uptime,
            DownloadClientHealth downloadClientHealth,
            ExternalApiHealth externalApiHealth)
        {
            var overallStatus = "healthy";
            if (downloadClientHealth.Status == "error" || externalApiHealth.Status == "error")
            {
                overallStatus = "error";
            }
            else if (downloadClientHealth.Status == "warning" || externalApiHealth.Status == "warning")
            {
                overallStatus = "warning";
            }

            return new ServiceHealth
            {
                Status = overallStatus,
                Version = version,
                Uptime = uptime,
                DownloadClients = downloadClientHealth,
                ExternalApis = externalApiHealth
            };
        }

        /// <summary>
        /// Builds download-client health from configuration plus the last known
        /// reachability of each client, as recorded by the background download-queue
        /// poller (<see cref="Listenarr.Application.Downloads.Contracts.IDownloadClientStatusCache"/>).
        /// </summary>
        /// <param name="clients">The configured download clients.</param>
        /// <param name="polledStatuses">
        /// The most recent per-client poll results. A client with no entry here has not
        /// been covered by a poll cycle yet (e.g. the poller hasn't run, or the client
        /// was just added) and is reported as "unknown" rather than assumed connected.
        /// </param>
        public static DownloadClientHealth BuildDownloadClientHealth(
            IEnumerable<DownloadClientConfiguration> clients,
            IReadOnlyList<QueueClientStatus>? polledStatuses)
        {
            var clientList = clients?.ToList() ?? new List<DownloadClientConfiguration>();
            var statusById = (polledStatuses ?? new List<QueueClientStatus>())
                .Where(status => !string.IsNullOrEmpty(status.ClientId))
                .GroupBy(status => status.ClientId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var clientStatuses = new List<ClientStatus>();
            var connectedCount = 0;

            foreach (var client in clientList)
            {
                if (!client.IsEnabled)
                {
                    continue;
                }

                string status;
                string? failureReason = null;

                if (statusById.TryGetValue(client.Id, out var polled))
                {
                    // A recorded failure reason means the most recent live probe of
                    // this client did not succeed, whether or not a stale cached
                    // queue snapshot is still being served for display purposes.
                    if (!string.IsNullOrEmpty(polled.SnapshotFailureReason))
                    {
                        status = "disconnected";
                        failureReason = polled.SnapshotFailureReason;
                    }
                    else
                    {
                        status = "connected";
                    }
                }
                else
                {
                    status = "unknown";
                }

                if (status == "connected")
                {
                    connectedCount++;
                }

                clientStatuses.Add(new ClientStatus
                {
                    Name = client.Name,
                    Status = status,
                    Type = client.Type,
                    FailureReason = failureReason
                });
            }

            var totalEnabled = clientList.Count(c => c.IsEnabled);
            var overallStatus = BuildChildStatus(connectedCount, totalEnabled);

            return new DownloadClientHealth
            {
                Status = overallStatus,
                Connected = connectedCount,
                Total = totalEnabled,
                Clients = clientStatuses
            };
        }

        public static ExternalApiHealth BuildExternalApiHealth(IEnumerable<ApiConfiguration> apis)
        {
            var apiList = apis?.ToList() ?? new List<ApiConfiguration>();
            var apiStatuses = new List<ApiStatus>();
            var connectedCount = 0;

            foreach (var api in apiList)
            {
                if (!api.IsEnabled)
                {
                    continue;
                }

                var status = "connected";
                connectedCount++;

                apiStatuses.Add(new ApiStatus
                {
                    Name = api.Name,
                    Status = status,
                    Enabled = api.IsEnabled
                });
            }

            var totalEnabled = apiList.Count(c => c.IsEnabled);
            var overallStatus = BuildChildStatus(connectedCount, totalEnabled);

            return new ExternalApiHealth
            {
                Status = overallStatus,
                Connected = connectedCount,
                Total = totalEnabled,
                Apis = apiStatuses
            };
        }

        public static DownloadClientHealth BuildDownloadClientHealthError()
        {
            return new DownloadClientHealth
            {
                Status = "error",
                Connected = 0,
                Total = 0,
                Clients = new List<ClientStatus>()
            };
        }

        public static ExternalApiHealth BuildExternalApiHealthError()
        {
            return new ExternalApiHealth
            {
                Status = "error",
                Connected = 0,
                Total = 0,
                Apis = new List<ApiStatus>()
            };
        }

        private static string BuildChildStatus(int connectedCount, int totalEnabled)
        {
            if (connectedCount == 0 && totalEnabled > 0)
            {
                return "error";
            }

            if (connectedCount < totalEnabled)
            {
                return "warning";
            }

            return "healthy";
        }
    }
}
