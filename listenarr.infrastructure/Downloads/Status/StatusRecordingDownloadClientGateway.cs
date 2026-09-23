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
    /// Shape only: passes every call through and records nothing yet.
    /// </summary>
    public sealed class StatusRecordingDownloadClientGateway(
        IDownloadClientGateway inner,
        IDownloadClientStatusService statusService,
        ILogger<StatusRecordingDownloadClientGateway> logger) : IDownloadClientGateway
    {
        public Task<(bool Success, string Message)> TestConnectionAsync(DownloadClientConfiguration client, CancellationToken ct = default)
        {
            _ = (statusService, logger);
            return inner.TestConnectionAsync(client, ct);
        }

        public Task<DownloadClientSubmissionResult> AddAsync(
            DownloadClientConfiguration client,
            PreparedDownloadSubmission submission,
            CancellationToken ct = default) =>
            inner.AddAsync(client, submission, ct);

        public Task<bool> RemoveAsync(DownloadClientConfiguration client, string id, bool deleteFiles = false, CancellationToken ct = default) =>
            inner.RemoveAsync(client, id, deleteFiles, ct);

        public Task<List<QueueItem>> GetQueueAsync(DownloadClientConfiguration client, CancellationToken ct = default) =>
            inner.GetQueueAsync(client, ct);

        public Task<QueueItem> GetQueueItemAsync(
            DownloadClientConfiguration client,
            Download download,
            QueueItem queueItem,
            CancellationToken ct = default) =>
            inner.GetQueueItemAsync(client, download, queueItem, ct);

        public Task<bool> MarkItemAsImportedAsync(DownloadClientConfiguration client, Download download, CancellationToken ct = default) =>
            inner.MarkItemAsImportedAsync(client, download, ct);

        public Task<List<Download>> FetchDownloadsAsync(DownloadClientConfiguration client, List<Download> downloads, CancellationToken ct = default) =>
            inner.FetchDownloadsAsync(client, downloads, ct);
    }
}
