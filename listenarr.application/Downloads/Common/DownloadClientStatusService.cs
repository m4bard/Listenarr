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

using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Downloads.Common
{
    /// <summary>
    /// The per-client failure backoff for download clients. A client that keeps failing is avoided
    /// by selection for progressively longer; a client that answers walks back down one rung per
    /// answer. This is Readarr's ProviderStatusServiceBase
    /// (src/NzbDrone.Core/ThingiProvider/Status/ProviderStatusServiceBase.cs:60-140) with the
    /// download client settings of Download/DownloadClientStatusService.cs:18-19, and Sonarr's is
    /// the same (src/NzbDrone.Core/Download/DownloadClientStatusService.cs:18-19).
    /// </summary>
    /// <remarks>
    /// Where an indexer failure backoff is present it uses the same Readarr table; the two differ
    /// only where Readarr's two differ (indexers climb to a day with no initial grace, download
    /// clients stop at an hour after five minutes' grace), so the periods below are that table's
    /// first six rungs and the two could share it.
    /// </remarks>
    public sealed class DownloadClientStatusService : IDownloadClientStatusService
    {
        /// <summary>
        /// Cooldown at each rung. Readarr's EscalationBackOff.Periods
        /// (src/NzbDrone.Core/ThingiProvider/Status/EscalationBackOff.cs:5-17), up to the rung download
        /// clients stop at.
        /// </summary>
        public static readonly TimeSpan[] Periods =
        [
            TimeSpan.Zero,
            TimeSpan.FromSeconds(60),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(1)
        ];

        /// <summary>
        /// Highest rung a download client reaches, an hour. Readarr's
        /// DownloadClientStatusService.cs:19; indexers go on to a day.
        /// </summary>
        public const int MaximumEscalationLevel = 5;

        /// <summary>
        /// How long a client must have been failing before it is blocked at all. Readarr's
        /// DownloadClientStatusService.cs:18. One dropped connection does not take a client out.
        /// </summary>
        public static readonly TimeSpan MinimumTimeSinceInitialFailure = TimeSpan.FromMinutes(5);

        /// <summary>Rung the startup window caps a block to. ProviderStatusServiceBase.cs:129.</summary>
        public const int StartupGraceLevel = 2;

        // Readarr serialises recording with a lock (ProviderStatusServiceBase.cs:24,67,92). The
        // service is scoped, so the lock has to be static to cover the queue monitor, a grab and a
        // connection test landing on the same client at once. Without it two concurrent failures
        // read the same row and one escalation is lost.
        private static readonly SemaphoreSlim RecordLock = new(1, 1);

        private readonly IDownloadClientStatusRepository _repository;
        private readonly DownloadClientBackoffStartupWindow _startupWindow;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<DownloadClientStatusService> _logger;

        public DownloadClientStatusService(
            IDownloadClientStatusRepository repository,
            DownloadClientBackoffStartupWindow startupWindow,
            TimeProvider timeProvider,
            ILogger<DownloadClientStatusService> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _startupWindow = startupWindow ?? throw new ArgumentNullException(nameof(startupWindow));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlySet<string>> GetBlockedClientIdsAsync(CancellationToken ct = default)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var statuses = await _repository.GetAllAsync(ct);
            return statuses
                .Where(s => s.IsDisabled(now))
                .Select(s => s.ClientId)
                .ToHashSet(StringComparer.Ordinal);
        }

        public async Task<IReadOnlyList<DownloadClientStatusSnapshot>> GetStatusesAsync(CancellationToken ct = default)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var statuses = await _repository.GetAllAsync(ct);
            return statuses
                .Where(s => s.EscalationLevel > 0 || s.IsDisabled(now))
                .Select(s => new DownloadClientStatusSnapshot(
                    s.ClientId,
                    s.EscalationLevel,
                    s.InitialFailure,
                    s.MostRecentFailure,
                    s.DisabledTill,
                    s.IsDisabled(now)))
                .ToList();
        }

        public async Task RecordSuccessAsync(string clientId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return;
            }

            await RecordLock.WaitAsync(ct);
            try
            {
                var status = await _repository.GetByClientIdAsync(clientId, ct);

                // The steady state: a healthy client answering. No write, which keeps recording
                // affordable on a monitor poll that runs every few seconds.
                if (status == null || status.EscalationLevel == 0)
                {
                    return;
                }

                // Decrement rather than reset (ProviderStatusServiceBase.cs:76-77). A client that
                // alternates failure and success still accumulates, which a reset would hide.
                status.EscalationLevel--;
                status.DisabledTill = null;

                await _repository.UpsertAsync(status, ct);
                _logger.LogInformation(
                    "Download client {ClientId} answered; failure backoff now at rung {Level}",
                    clientId,
                    status.EscalationLevel);
            }
            finally
            {
                RecordLock.Release();
            }
        }

        public async Task RecordFailureAsync(string clientId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return;
            }

            await RecordLock.WaitAsync(ct);
            try
            {
                var status = await _repository.GetByClientIdAsync(clientId, ct)
                    ?? new DownloadClientStatus { ClientId = clientId };

                Escalate(status, _timeProvider.GetUtcNow());

                if (!await _repository.UpsertAsync(status, ct))
                {
                    _logger.LogDebug("Download client {ClientId} is not saved; failure not recorded", clientId);
                    return;
                }

                if (status.DisabledTill is { } till)
                {
                    _logger.LogWarning(
                        "Download client {ClientId} avoided until {DisabledTill:o} after repeated failures (rung {Level}, failing since {InitialFailure:o})",
                        clientId,
                        till,
                        status.EscalationLevel,
                        status.InitialFailure);
                }
                else
                {
                    _logger.LogInformation(
                        "Download client {ClientId} failed; not avoided yet (failing since {InitialFailure:o})",
                        clientId,
                        status.InitialFailure);
                }
            }
            finally
            {
                RecordLock.Release();
            }
        }

        /// <summary>
        /// ProviderStatusServiceBase.RecordFailure (:85-140) with escalate true, which is what every
        /// Readarr download client caller passes.
        /// </summary>
        private void Escalate(DownloadClientStatus status, DateTimeOffset now)
        {
            var utcNow = now.UtcDateTime;
            status.MostRecentFailure = utcNow;

            var escalate = true;
            if (status.EscalationLevel == 0)
            {
                // The first failure of a run lands on rung 1 and does not also climb.
                status.InitialFailure = utcNow;
                status.EscalationLevel = 1;
                escalate = false;
            }

            var inStartupGrace = _startupWindow.Contains(now);
            var inInitialGrace = (status.InitialFailure ?? utcNow) + MinimumTimeSinceInitialFailure > utcNow;

            if (escalate && !inInitialGrace && !inStartupGrace)
            {
                status.EscalationLevel = Math.Min(MaximumEscalationLevel, status.EscalationLevel + 1);
            }

            if (!inInitialGrace)
            {
                status.DisabledTill = utcNow + Periods[Math.Min(MaximumEscalationLevel, status.EscalationLevel)];
            }

            if (inStartupGrace && status.DisabledTill is { } till)
            {
                var ceiling = utcNow + Periods[StartupGraceLevel];
                if (till > ceiling)
                {
                    status.DisabledTill = ceiling;
                }
            }
        }
    }
}
