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

using Listenarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.ActivityHistory.Services
{
    /// <summary>
    /// Implementation of download history service.
    /// Records all download events to provide idempotency and audit trail.
    /// </summary>
    public class DownloadHistoryService : IDownloadHistoryService
    {
        private readonly ListenArrDbContext _context;
        private readonly ILogger<DownloadHistoryService> _logger;
        private readonly IDownloadClientAdapterFactory _adapterFactory;

        public DownloadHistoryService(
            ListenArrDbContext context,
            ILogger<DownloadHistoryService> logger,
            IDownloadClientAdapterFactory adapterFactory)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _adapterFactory = adapterFactory ?? throw new ArgumentNullException(nameof(adapterFactory));
        }

        /// <summary>
        /// What a history row can say about the download client behind an event: the client's
        /// configured name, and the protocol it speaks.
        /// </summary>
        /// <param name="Name">
        /// The configured name, or null when the client no longer exists. Null rather than a
        /// placeholder, because <see cref="AddUnifiedAsync"/> already has a fallback for a name
        /// it cannot get and a literal would defeat it.
        /// </param>
        /// <param name="Protocol">The protocol, or Unknown when it cannot be resolved.</param>
        private sealed record DownloadClientFacts(string? Name, DownloadProtocol Protocol);

        /// <summary>
        /// Read what the download client configuration can tell us about an event. The protocol
        /// comes from the caller when it has one, and RecordGrabbedAsync always does because the
        /// submission carries it; otherwise the adapter registered for the client's type is
        /// asked, which is the same answer the queue and the submission path use. The name is
        /// only ever the configured one. Both fall back to unknown rather than to a guess.
        /// </summary>
        private async Task<DownloadClientFacts> ResolveClientAsync(
            string clientId,
            DownloadProtocol? declaredProtocol)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return new DownloadClientFacts(null, declaredProtocol ?? DownloadProtocol.Unknown);
            }

            var configuration = await _context.DownloadClientConfigurations
                .AsNoTracking()
                .Where(candidate => candidate.Id == clientId)
                .Select(candidate => new { candidate.Name, candidate.Type })
                .FirstOrDefaultAsync();

            if (configuration == null)
            {
                _logger.LogDebug(
                    "No download client {ClientId} to name a history row after", clientId);
                return new DownloadClientFacts(null, declaredProtocol ?? DownloadProtocol.Unknown);
            }

            var name = string.IsNullOrWhiteSpace(configuration.Name) ? null : configuration.Name;

            if (declaredProtocol.HasValue)
            {
                return new DownloadClientFacts(name, declaredProtocol.Value);
            }

            if (string.IsNullOrWhiteSpace(configuration.Type))
            {
                _logger.LogDebug(
                    "Download client {ClientId} has no type to resolve a history protocol from", clientId);
                return new DownloadClientFacts(name, DownloadProtocol.Unknown);
            }

            try
            {
                return new DownloadClientFacts(name, _adapterFactory.GetByType(configuration.Type).Protocol);
            }
            catch (InvalidOperationException)
            {
                _logger.LogDebug(
                    "No adapter registered for download client type {ClientType}, recording the history protocol as unknown",
                    configuration.Type);
                return new DownloadClientFacts(name, DownloadProtocol.Unknown);
            }
        }

        /// <summary>
        /// Check if a download was already imported successfully.
        /// Core idempotency check: if we have an Imported event, don't import again.
        /// </summary>
        public async Task<bool> IsAlreadyImportedAsync(string downloadId, string clientId)
        {
            if (string.IsNullOrWhiteSpace(downloadId) || string.IsNullOrWhiteSpace(clientId))
                return false;

            var normalizedId = downloadId.ToUpperInvariant();

            var imported = await _context.History
                .Where(h => h.DownloadId == normalizedId &&
                           h.DownloadClientId == clientId &&
                           h.EventType == HistoryEvents.Imported &&
                           h.Outcome == HistoryOutcome.Succeeded)
                .AnyAsync();

            if (imported)
                _logger.LogInformation(
                    "Download {DownloadId} from client {ClientId} was already imported (idempotency check)",
                    downloadId, clientId);

            return imported;
        }

        /// <summary>
        /// Check if a download was recently grabbed from a source.
        /// Validates that we're not re-importing an old download that reappeared.
        /// </summary>
        public async Task<bool> HasRecentGrabbedAsync(string downloadId, string clientId, int withinSeconds = 604800)
        {
            if (string.IsNullOrWhiteSpace(downloadId) || string.IsNullOrWhiteSpace(clientId))
                return false;

            var normalizedId = downloadId.ToUpperInvariant();
            var cutoffTime = DateTime.UtcNow.AddSeconds(-withinSeconds);

            var recentGrab = await _context.History
                .Where(h => h.DownloadId == normalizedId &&
                           h.DownloadClientId == clientId &&
                           h.EventType == HistoryEvents.Grabbed &&
                           h.Timestamp >= cutoffTime)
                .AnyAsync();

            return recentGrab;
        }

        public async Task RecordGrabbedAsync(string downloadId, string clientId, string title,
            DownloadProtocol protocol, int? audiobookId = null,
            string? indexer = null, string? quality = null, long? size = null)
        {
            var client = await ResolveClientAsync(clientId, protocol);
            var history = new DownloadHistory
            {
                DownloadId = downloadId.ToUpperInvariant(),
                EventType = DownloadHistoryEventType.Grabbed,
                Status = DownloadItemStatus.Queued,
                EventDate = DateTime.UtcNow,
                DownloadClient = client.Name ?? string.Empty,
                DownloadClientId = clientId,
                Protocol = client.Protocol,
                Title = title,
                WasImported = false
            };

            // A size of zero is what an indexer that reported nothing looks like, and storing it
            // would render as an empty release rather than as an unknown one.
            await AddUnifiedAsync(
                history,
                audiobookId,
                new GrabbedRelease(
                    string.IsNullOrWhiteSpace(indexer) ? null : indexer.Trim(),
                    string.IsNullOrWhiteSpace(quality) ? null : quality.Trim(),
                    size is > 0 ? size : null));

            _logger.LogInformation(
                "Recorded Grabbed event for {DownloadId} ({Title}) from client {ClientId}",
                downloadId, title, clientId);
        }

        public async Task RecordDownloadCompleteAsync(string downloadId, string clientId, string title,
            string? outputPath = null, DownloadProtocol? protocol = null)
        {
            var client = await ResolveClientAsync(clientId, protocol);
            var history = new DownloadHistory
            {
                DownloadId = downloadId.ToUpperInvariant(),
                EventType = DownloadHistoryEventType.DownloadCompleted,
                Status = DownloadItemStatus.Completed,
                EventDate = DateTime.UtcNow,
                DownloadClient = client.Name ?? string.Empty,
                DownloadClientId = clientId,
                Protocol = client.Protocol,
                Title = title,
                OutputPath = outputPath,
                WasImported = false
            };

            await AddUnifiedAsync(history);

            _logger.LogInformation(
                "Recorded DownloadCompleted event for {DownloadId} ({Title})",
                downloadId, title);
        }

        public async Task RecordDownloadFailedAsync(string downloadId, string clientId, string title,
            string? errorMessage = null, DownloadProtocol? protocol = null)
        {
            var client = await ResolveClientAsync(clientId, protocol);
            var history = new DownloadHistory
            {
                DownloadId = downloadId.ToUpperInvariant(),
                EventType = DownloadHistoryEventType.DownloadFailed,
                Status = DownloadItemStatus.Failed,
                EventDate = DateTime.UtcNow,
                DownloadClient = client.Name ?? string.Empty,
                DownloadClientId = clientId,
                Protocol = client.Protocol,
                Title = title,
                ErrorMessage = errorMessage,
                WasImported = false
            };

            await AddUnifiedAsync(history);

            _logger.LogWarning(
                "Recorded DownloadFailed event for {DownloadId} ({Title}): {Error}",
                downloadId, title, errorMessage ?? "No error message");
        }

        public async Task RecordImportedAsync(string downloadId, string clientId, string title,
            int? audiobookId = null, DownloadProtocol? protocol = null)
        {
            var client = await ResolveClientAsync(clientId, protocol);
            var history = new DownloadHistory
            {
                DownloadId = downloadId.ToUpperInvariant(),
                EventType = DownloadHistoryEventType.Imported,
                Status = DownloadItemStatus.Imported,
                EventDate = DateTime.UtcNow,
                DownloadClient = client.Name ?? string.Empty,
                DownloadClientId = clientId,
                Protocol = client.Protocol,
                Title = title,
                WasImported = true,
                ImportedAt = DateTime.UtcNow
            };

            await AddUnifiedAsync(history, audiobookId);

            _logger.LogInformation(
                "Recorded Imported event for {DownloadId} ({Title}) audiobook {AudiobookId}",
                downloadId, title, audiobookId);
        }

        public async Task RecordImportFailedAsync(string downloadId, string clientId, string title,
            string? errorMessage = null)
        {
            var client = await ResolveClientAsync(clientId, null);
            var history = new DownloadHistory
            {
                DownloadId = downloadId.ToUpperInvariant(),
                EventType = DownloadHistoryEventType.ImportFailed,
                Status = DownloadItemStatus.ImportFailed,
                EventDate = DateTime.UtcNow,
                DownloadClient = client.Name ?? string.Empty,
                DownloadClientId = clientId,
                Protocol = client.Protocol,
                Title = title,
                ErrorMessage = errorMessage,
                WasImported = false
            };

            await AddUnifiedAsync(history);

            _logger.LogWarning(
                "Recorded ImportFailed event for {DownloadId} ({Title}): {Error}",
                downloadId, title, errorMessage ?? "No error message");
        }

        public async Task RecordPausedAsync(string downloadId, string clientId, string title)
        {
            var client = await ResolveClientAsync(clientId, null);
            var history = new DownloadHistory
            {
                DownloadId = downloadId.ToUpperInvariant(),
                EventType = DownloadHistoryEventType.Paused,
                Status = DownloadItemStatus.Paused,
                EventDate = DateTime.UtcNow,
                DownloadClient = client.Name ?? string.Empty,
                DownloadClientId = clientId,
                Protocol = client.Protocol,
                Title = title,
                WasImported = false
            };

            await AddUnifiedAsync(history);

            _logger.LogInformation("Recorded Paused event for {DownloadId} ({Title})", downloadId, title);
        }

        public async Task RecordResumedAsync(string downloadId, string clientId, string title)
        {
            var client = await ResolveClientAsync(clientId, null);
            var history = new DownloadHistory
            {
                DownloadId = downloadId.ToUpperInvariant(),
                EventType = DownloadHistoryEventType.Resumed,
                Status = DownloadItemStatus.Downloading,
                EventDate = DateTime.UtcNow,
                DownloadClient = client.Name ?? string.Empty,
                DownloadClientId = clientId,
                Protocol = client.Protocol,
                Title = title,
                WasImported = false
            };

            await AddUnifiedAsync(history);

            _logger.LogInformation("Recorded Resumed event for {DownloadId} ({Title})", downloadId, title);
        }

        public async Task RecordRemovedAsync(string downloadId, string clientId, string title)
        {
            var client = await ResolveClientAsync(clientId, null);
            var history = new DownloadHistory
            {
                DownloadId = downloadId.ToUpperInvariant(),
                EventType = DownloadHistoryEventType.Removed,
                Status = DownloadItemStatus.Removed,
                EventDate = DateTime.UtcNow,
                DownloadClient = client.Name ?? string.Empty,
                DownloadClientId = clientId,
                Protocol = client.Protocol,
                Title = title,
                WasImported = false
            };

            await AddUnifiedAsync(history);

            _logger.LogInformation("Recorded Removed event for {DownloadId} ({Title})", downloadId, title);
        }

        public async Task<List<DownloadHistory>> GetHistoryAsync(string downloadId, string clientId)
        {
            if (string.IsNullOrWhiteSpace(downloadId) || string.IsNullOrWhiteSpace(clientId))
                return new();

            var normalizedId = downloadId.ToUpperInvariant();

            return (await _context.History
                    .AsNoTracking()
                    .Where(h => h.DownloadId == normalizedId && h.DownloadClientId == clientId)
                    .OrderBy(h => h.Timestamp)
                    .ToListAsync())
                .Select(ToLegacy)
                .ToList();
        }

        public async Task<DownloadHistory?> GetLatestEventAsync(string downloadId, string clientId)
        {
            if (string.IsNullOrWhiteSpace(downloadId) || string.IsNullOrWhiteSpace(clientId))
                return null;

            var normalizedId = downloadId.ToUpperInvariant();

            var entry = await _context.History
                .AsNoTracking()
                .Where(h => h.DownloadId == normalizedId && h.DownloadClientId == clientId)
                .OrderByDescending(h => h.Timestamp)
                .FirstOrDefaultAsync();
            return entry == null ? null : ToLegacy(entry);
        }

        public async Task<int> CleanupOldEntriesAsync(int retentionDays = 0)
        {
            if (retentionDays < 0)
                throw new ArgumentOutOfRangeException(nameof(retentionDays), "Retention days cannot be negative.");
            if (retentionDays == 0)
            {
                _logger.LogInformation("Download history retention is unlimited; no entries were deleted");
                return 0;
            }

            var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

            // Use ToList() first for in-memory DB compatibility, then delete
            var oldEntries = await _context.History
                .Where(h => h.DownloadId != null && h.Timestamp < cutoffDate)
                .ToListAsync();

            _context.History.RemoveRange(oldEntries);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Cleaned up {Count} old download history entries (older than {Days} days)",
                oldEntries.Count, retentionDays);

            return oldEntries.Count;
        }

        /// <summary>
        /// What the search result behind a grab knew about the release. Carried separately from
        /// <see cref="DownloadHistory"/> because that type maps to the frozen DownloadHistories
        /// table, which takes no new writes and so has nowhere to put these.
        /// </summary>
        private sealed record GrabbedRelease(string? Indexer, string? Quality, long? Size);

        /// <summary>
        /// Writes the canonical history row for a download event. The audiobook key is passed
        /// separately because <see cref="History.AudiobookId"/> is the integer library key that
        /// per-book history queries filter on, while <see cref="DownloadHistory.AudiobookId"/> is
        /// the legacy compatibility identifier and cannot carry it.
        /// </summary>
        private async Task AddUnifiedAsync(
            DownloadHistory history,
            int? audiobookId = null,
            GrabbedRelease? release = null)
        {
            var normalizedId = history.DownloadId.ToUpperInvariant();
            _context.History.Add(new History
            {
                DownloadId = normalizedId,
                DownloadClientId = history.DownloadClientId,
                AudiobookId = audiobookId,
                AudiobookExternalId = history.AudiobookId?.ToString(),
                SourceTitle = history.Title,
                AudiobookTitle = history.Title,
                EventType = HistoryEvents.FromDownloadEvent(history.EventType),
                Outcome = history.EventType is DownloadHistoryEventType.DownloadFailed or DownloadHistoryEventType.ImportFailed
                    ? HistoryOutcome.Failed
                    : HistoryOutcome.Succeeded,
                Timestamp = history.EventDate,
                Source = string.IsNullOrWhiteSpace(history.DownloadClient) ? "Download" : history.DownloadClient,
                Message = history.ErrorMessage,
                Error = history.ErrorMessage,
                Data = history.Data == null ? null : System.Text.Json.JsonSerializer.Serialize(history.Data),
                CorrelationId = normalizedId,
                // Without this the protocol each Record method works out is discarded on the way
                // to the table, and every row read back through ToLegacy reports the first member
                // of the enum, which is Torrent.
                Protocol = history.Protocol,
                // Only a grab has a release behind it, so these stay null for every other event
                // rather than being filled in with something the event did not know.
                Indexer = release?.Indexer,
                Quality = release?.Quality,
                Size = release?.Size
            });
            await _context.SaveChangesAsync();
        }

        private static DownloadHistory ToLegacy(History history) => new()
        {
            Id = history.Id,
            DownloadId = history.DownloadId ?? string.Empty,
            DownloadClientId = history.DownloadClientId ?? string.Empty,
            DownloadClient = history.Source ?? "Unknown",
            Protocol = history.Protocol ?? DownloadProtocol.Unknown,
            EventType = HistoryEvents.ToDownloadEvent(history.EventType),
            Status = history.EventType == HistoryEvents.Imported
                ? DownloadItemStatus.Imported
                : history.Outcome == HistoryOutcome.Failed
                    ? DownloadItemStatus.Failed
                    : DownloadItemStatus.Unknown,
            EventDate = history.Timestamp,
            Title = history.SourceTitle ?? history.AudiobookTitle ?? string.Empty,
            ErrorMessage = history.Error,
            WasImported = history.EventType == HistoryEvents.Imported && history.Outcome == HistoryOutcome.Succeeded,
            ImportedAt = history.EventType == HistoryEvents.Imported && history.Outcome == HistoryOutcome.Succeeded
                ? history.Timestamp
                : null
        };
    }
}
