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
            // Precedence is error > warning > unknown > healthy. "unknown" only wins when
            // nothing is confirmed wrong but at least one probe could not produce an answer,
            // so the page never claims healthy on the strength of a question mark.
            var overallStatus = "healthy";
            if (downloadClientHealth.Status == "error" || externalApiHealth.Status == "error")
            {
                overallStatus = "error";
            }
            else if (downloadClientHealth.Status == "warning" || externalApiHealth.Status == "warning")
            {
                overallStatus = "warning";
            }
            else if (downloadClientHealth.Status == "unknown" || externalApiHealth.Status == "unknown")
            {
                overallStatus = "unknown";
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
        /// Maps already-probed download clients into the health payload. Probing happens in
        /// SystemService; this stays a pure function so the status arithmetic can be tested
        /// without a gateway, a socket or a clock.
        /// </summary>
        /// <param name="probes">One entry per enabled client. Disabled clients are not probed and are not reported.</param>
        public static DownloadClientHealth BuildDownloadClientHealth(IEnumerable<DownloadClientProbe> probes)
        {
            var probeList = probes?.ToList() ?? new List<DownloadClientProbe>();
            var clientStatuses = new List<ClientStatus>();
            var connectedCount = 0;
            var disconnectedCount = 0;
            var unknownCount = 0;

            foreach (var probe in probeList)
            {
                switch (probe.Status)
                {
                    case DownloadClientProbeStatuses.Connected:
                        connectedCount++;
                        break;
                    case DownloadClientProbeStatuses.Unknown:
                        unknownCount++;
                        break;
                    default:
                        disconnectedCount++;
                        break;
                }

                clientStatuses.Add(new ClientStatus
                {
                    Name = probe.Name,
                    Status = probe.Status,
                    Type = probe.Type
                });
            }

            var overallStatus = BuildProbedChildStatus(connectedCount, disconnectedCount, unknownCount);

            return new DownloadClientHealth
            {
                Status = overallStatus,
                Connected = connectedCount,
                Total = probeList.Count,
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

        /// <summary>
        /// Rolls per-client probe outcomes into one status. With no unknowns this is identical
        /// to the two-argument form used by the API card: everything down is an error, a mix is
        /// a warning, everything up is healthy. An unknown never counts as connected, and never
        /// hardens into an error on its own, because not knowing is not the same as being down.
        /// </summary>
        private static string BuildProbedChildStatus(int connectedCount, int disconnectedCount, int unknownCount)
        {
            if (disconnectedCount > 0)
            {
                return connectedCount == 0 && unknownCount == 0 ? "error" : "warning";
            }

            if (unknownCount > 0)
            {
                return "unknown";
            }

            return "healthy";
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
