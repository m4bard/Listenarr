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
using Listenarr.Domain.Common;

namespace Listenarr.Application.Audiobooks.Contracts
{
    public enum ScanAuthorizationMode
    {
        ResolveCurrentAudiobookPath,
        PreauthorizedPath,
        MoveHandoff
    }

    public static class ScanJobStatuses
    {
        public const string Completed = "Completed";

        /// <summary>
        /// The scan finished and found audio files in scope, but none of them could be
        /// attributed to the audiobook, so no file rows were written for them.
        /// </summary>
        public const string CompletedNoFilesAccepted = "CompletedNoFilesAccepted";

        public static bool IsCompletion(string? status) =>
            string.Equals(status, Completed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, CompletedNoFilesAccepted, StringComparison.OrdinalIgnoreCase);
    }

    public static class ScanJobPublicError
    {
        public static string? FromInternal(string? error) =>
            string.IsNullOrWhiteSpace(error)
                ? null
                : "The scan failed. Review the server logs for details.";

        // The no-files-accepted message is composed by the scan processor from counts
        // only, never from paths or exception text, so it is safe to show as written.
        public static string? FromInternal(string? error, string? status) =>
            string.Equals(status, ScanJobStatuses.CompletedNoFilesAccepted, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(error)
                ? error
                : FromInternal(error);
    }

    public sealed record ScanEnqueueCommand(
        Audiobook Audiobook,
        string? Path = null,
        PathIdentitySnapshot? PathIdentity = null,
        ScanPathPhysicalIdentity? PhysicalIdentity = null,
        string? CorrelationId = null,
        string? DownloadId = null,
        bool IsAuthoritativeScope = true,
        ScanAuthorizationMode AuthorizationMode = ScanAuthorizationMode.ResolveCurrentAudiobookPath);

    public interface IScanQueueService
    {
        Task<Guid> EnqueueScanAsync(ScanEnqueueCommand command);
        Task<Guid> EnqueueScanAsync(
            Audiobook audiobook,
            string? correlationId = null,
            string? downloadId = null);
        Task<Guid?> EnqueueMoveHandoffScanAsync(
            Audiobook audiobook,
            MoveScanHandoffClaim claim,
            ScanPathPhysicalIdentity physicalIdentity);
        Task<Guid?> RequeueScanAsync(Guid jobId);
        Task CommitTerminalJobStatusAsync(
            Guid jobId,
            Func<Task<(string Status, string? Error)>> persistTerminalState,
            CancellationToken cancellationToken = default);
        System.Threading.Channels.ChannelReader<ScanJob> Reader { get; }
        bool TryGetJob(Guid id, out ScanJob? job);
        void UpdateJobStatus(Guid id, string status, string? error = null, int? found = null, int? created = null);
    }
}
