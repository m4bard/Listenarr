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

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.SystemDiagnostics.Diagnostics
{
    /// <summary>
    /// Download client connectivity for the System page. Split out of SystemService.cs so that
    /// file stays inside the repository's focused-source-file limit.
    /// </summary>
    public partial class SystemService
    {
        private async Task<DownloadClientHealth> GetDownloadClientHealthAsync()
        {
            try
            {
                var clients = await _configurationService.GetDownloadClientConfigurationsAsync();
                var enabledClients = (clients ?? new List<DownloadClientConfiguration>())
                    .Where(client => client.IsEnabled)
                    .ToList();

                // Probe concurrently so the response costs one timeout rather than one per client.
                var probes = await Task.WhenAll(enabledClients.Select(ProbeDownloadClientAsync));

                return SystemHealthMapper.BuildDownloadClientHealth(probes);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Error getting download client health");
                return SystemHealthMapper.BuildDownloadClientHealthError();
            }
        }

        /// <summary>
        /// Runs the same connectivity check the download client Test button runs, bounded by our
        /// own timeout, and never throws. One misconfigured client must not take out the endpoint.
        /// </summary>
        private async Task<DownloadClientProbe> ProbeDownloadClientAsync(DownloadClientConfiguration client)
        {
            var cancellation = new CancellationTokenSource();
            Task<(bool Success, string Message)> probe;

            try
            {
                // Not awaited yet on purpose: the gateway resolves the adapter synchronously and
                // throws InvalidOperationException when no adapter is registered for the type, so
                // that failure arrives here rather than on the task.
                probe = _downloadClientGateway.TestConnectionAsync(client, cancellation.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                cancellation.Dispose();
                _logger.LogWarning(ex, "Could not probe download client {ClientName} for system health", LogRedaction.SanitizeText(client.Name));
                return new DownloadClientProbe(client.Name, client.Type, DownloadClientProbeStatuses.Unknown);
            }

            try
            {
                var (success, _) = await probe.WaitAsync(_downloadClientProbeTimeout);
                cancellation.Dispose();
                return new DownloadClientProbe(
                    client.Name,
                    client.Type,
                    success ? DownloadClientProbeStatuses.Connected : DownloadClientProbeStatuses.Disconnected);
            }
            catch (TimeoutException)
            {
                // Ours, not the adapter's. The adapter swallows its own cancellation and reports
                // failure, so a timeout can only be distinguished on this side of the call.
                cancellation.Cancel();
                AbandonProbe(probe, cancellation);
                _logger.LogWarning(
                    "Download client {ClientName} did not answer the system health probe within {TimeoutSeconds}s; reporting unknown",
                    LogRedaction.SanitizeText(client.Name),
                    _downloadClientProbeTimeout.TotalSeconds);
                return new DownloadClientProbe(client.Name, client.Type, DownloadClientProbeStatuses.Unknown);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                cancellation.Dispose();
                _logger.LogWarning(ex, "Download client {ClientName} probe failed for system health", LogRedaction.SanitizeText(client.Name));
                return new DownloadClientProbe(client.Name, client.Type, DownloadClientProbeStatuses.Unknown);
            }
        }

        /// <summary>
        /// Observes an abandoned probe so a later fault does not surface as an unobserved task
        /// exception, and disposes its cancellation source once the probe has actually stopped.
        /// </summary>
        private static void AbandonProbe(Task probe, CancellationTokenSource cancellation)
        {
            _ = probe.ContinueWith(
                completed =>
                {
                    _ = completed.Exception;
                    cancellation.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
