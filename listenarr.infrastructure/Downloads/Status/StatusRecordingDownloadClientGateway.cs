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

namespace Listenarr.Infrastructure.Downloads.Status
{
    /// <summary>
    /// The download client gateway, recording each call's outcome against the client's persisted
    /// failure status at the one boundary every client call crosses. The base gateway stays free of
    /// persistence, as its own contract says it must.
    /// </summary>
    /// <remarks>
    /// A subclass rather than a wrapper around the interface because existing code and tests cast
    /// the registered gateway to <see cref="DownloadClientGateway"/> to reach its adapter lookup, and
    /// a wrapper would break every one of those casts.
    ///
    /// Which calls count follows Readarr, which records a download client's status in three places:
    /// the queue poll (DownloadMonitoringService.cs:90-100, success and any failure), the connection
    /// test (DownloadClientFactory.cs:82-89) and a successful grab (DownloadService.cs:99). Here:
    /// <list type="bullet">
    /// <item>Submission. A success de-escalates. A failure escalates only when the client could not
    /// be reached (see <see cref="DownloadClientFailureClassifier"/>); a client refusing one release
    /// is not a client failure. Readarr records no failure on a grab at all, so this is stricter
    /// than the family and never looser.</item>
    /// <item>The monitor poll (<see cref="FetchDownloadsAsync"/>). Any failure escalates; an answer
    /// de-escalates, but only when the client was actually asked, since the gateway returns without
    /// a call when no download carries a client id.</item>
    /// <item>The display snapshot (<see cref="GetQueueAsync"/>). A thrown failure escalates, but an
    /// answer is not taken as health: the qBittorrent adapter answers an unreachable client there
    /// with an empty list rather than an exception.</item>
    /// <item>The connection test, both outcomes, for a saved client only (the repository writes
    /// nothing for an id with no client behind it).</item>
    /// </list>
    /// Removal, import marking and per-item lookups are not recorded, as in Readarr. Recording never
    /// changes the outcome of the call it observes: a status write that fails is logged and dropped,
    /// because turning an accepted grab into an error would send it again.
    /// </remarks>
    public sealed class StatusRecordingDownloadClientGateway(
        IRemotePathMappingService remotePathMappingService,
        IDownloadClientAdapterFactory factory,
        IFileSystem fileSystem,
        IFileSystemSemanticsResolver semanticsResolver,
        ILogger<DownloadClientGateway> gatewayLogger,
        IDownloadClientStatusService statusService,
        ILogger<StatusRecordingDownloadClientGateway> logger)
        : DownloadClientGateway(remotePathMappingService, factory, fileSystem, semanticsResolver, gatewayLogger)
    {
        public override async Task<(bool Success, string Message)> TestConnectionAsync(DownloadClientConfiguration client, CancellationToken ct = default)
        {
            (bool Success, string Message) result;
            try
            {
                result = await base.TestConnectionAsync(client, ct);
            }
            catch (Exception ex) when (IsRecordable(ex, ct))
            {
                await RecordAsync(client, succeeded: false, ct);
                throw;
            }

            await RecordAsync(client, result.Success, ct);
            return result;
        }

        public override async Task<DownloadClientSubmissionResult> AddAsync(
            DownloadClientConfiguration client,
            PreparedDownloadSubmission submission,
            CancellationToken ct = default)
        {
            DownloadClientSubmissionResult result;
            try
            {
                result = await base.AddAsync(client, submission, ct);
            }
            catch (Exception ex) when (DownloadClientFailureClassifier.IsClientUnavailable(ex, ct))
            {
                await RecordAsync(client, succeeded: false, ct);
                throw;
            }

            await RecordAsync(client, succeeded: true, ct);
            return result;
        }

        public override async Task<List<QueueItem>> GetQueueAsync(DownloadClientConfiguration client, CancellationToken ct = default)
        {
            try
            {
                return await base.GetQueueAsync(client, ct);
            }
            catch (Exception ex) when (IsRecordable(ex, ct))
            {
                await RecordAsync(client, succeeded: false, ct);
                throw;
            }
        }

        public override async Task<List<Download>> FetchDownloadsAsync(DownloadClientConfiguration client, List<Download> downloads, CancellationToken ct = default)
        {
            List<Download> result;
            try
            {
                result = await base.FetchDownloadsAsync(client, downloads, ct);
            }
            catch (Exception ex) when (IsRecordable(ex, ct))
            {
                await RecordAsync(client, succeeded: false, ct);
                throw;
            }

            if (downloads.Any(d => d.GetExternalId() != null))
            {
                await RecordAsync(client, succeeded: true, ct);
            }

            return result;
        }

        /// <summary>
        /// Any failure except one the caller asked for by cancelling, and except the ones no
        /// handler should intercept.
        /// </summary>
        private static bool IsRecordable(Exception ex, CancellationToken ct) =>
            !ct.IsCancellationRequested
            && ex is not OutOfMemoryException
            && ex is not StackOverflowException;

        private async Task RecordAsync(DownloadClientConfiguration client, bool succeeded, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(client?.Id))
            {
                return;
            }

            try
            {
                if (succeeded)
                {
                    await statusService.RecordSuccessAsync(client.Id, ct);
                }
                else
                {
                    await statusService.RecordFailureAsync(client.Id, ct);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogWarning(
                    ex,
                    "Could not record a {Outcome} for download client {ClientId}; the call's own result stands",
                    succeeded ? "success" : "failure",
                    client.Id);
            }
        }
    }
}
