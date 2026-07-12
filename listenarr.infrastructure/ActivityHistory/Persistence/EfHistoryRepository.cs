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

namespace Listenarr.Infrastructure.ActivityHistory.Persistence
{
    public class EfHistoryRepository : IHistoryRepository
    {
        private readonly ListenArrDbContext _db;

        public EfHistoryRepository(ListenArrDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<HistoryPage> QueryAsync(HistoryQuery query, CancellationToken ct = default)
        {
            var limit = Math.Clamp(query.Limit, 1, 500);
            var offset = Math.Max(0, query.Offset);
            IQueryable<History> filtered = _db.History.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(query.EventType))
                filtered = filtered.Where(h => h.EventType == query.EventType);
            if (query.Outcome.HasValue)
                filtered = filtered.Where(h => h.Outcome == query.Outcome.Value);
            if (query.From.HasValue)
                filtered = filtered.Where(h => h.Timestamp >= query.From.Value);
            if (query.To.HasValue)
                filtered = filtered.Where(h => h.Timestamp <= query.To.Value);
            if (query.AudiobookId.HasValue)
                filtered = filtered.Where(h => h.AudiobookId == query.AudiobookId.Value);
            if (!string.IsNullOrWhiteSpace(query.DownloadId))
                filtered = filtered.Where(h => h.DownloadId == query.DownloadId);
            if (!string.IsNullOrWhiteSpace(query.DownloadClientId))
                filtered = filtered.Where(h => h.DownloadClientId == query.DownloadClientId);
            if (!string.IsNullOrWhiteSpace(query.CorrelationId))
                filtered = filtered.Where(h => h.CorrelationId == query.CorrelationId);

            var total = await filtered.CountAsync(ct);
            var ascending = string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
            filtered = query.SortBy.ToLowerInvariant() switch
            {
                "eventtype" => ascending ? filtered.OrderBy(h => h.EventType) : filtered.OrderByDescending(h => h.EventType),
                "outcome" => ascending ? filtered.OrderBy(h => h.Outcome) : filtered.OrderByDescending(h => h.Outcome),
                "source" => ascending ? filtered.OrderBy(h => h.Source) : filtered.OrderByDescending(h => h.Source),
                _ => ascending ? filtered.OrderBy(h => h.Timestamp) : filtered.OrderByDescending(h => h.Timestamp)
            };

            var records = await filtered.Skip(offset).Take(limit).ToListAsync(ct);
            return new HistoryPage(records, total, limit, offset);
        }

        public Task<History?> GetByIdAsync(int id, CancellationToken ct = default) =>
            _db.History.AsNoTracking().FirstOrDefaultAsync(h => h.Id == id, ct);

        public async Task<List<History>> GetByCorrelationIdAsync(string correlationId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(correlationId)) return [];
            return await _db.History
                .AsNoTracking()
                .Where(h => h.CorrelationId == correlationId)
                .OrderBy(h => h.Timestamp)
                .ThenBy(h => h.Id)
                .ToListAsync(ct);
        }

        public async Task<History?> GetSucceededImportedByDownloadIdAsync(string downloadId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(downloadId)) return null;

            var normalizedId = downloadId.ToUpperInvariant();
            return await _db.History
                .AsNoTracking()
                .Where(h =>
                    h.DownloadId != null &&
                    h.DownloadId.ToUpper() == normalizedId &&
                    h.EventType == HistoryEvents.Imported &&
                    h.Outcome == HistoryOutcome.Succeeded)
                .OrderByDescending(h => h.Timestamp)
                .ThenByDescending(h => h.Id)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<DateTime?> GetOldestTimestampByDownloadIdAsync(string downloadId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(downloadId)) return null;

            var normalizedId = downloadId.ToUpperInvariant();
            return await _db.History
                .AsNoTracking()
                .Where(h => h.DownloadId != null && h.DownloadId.ToUpper() == normalizedId)
                .OrderBy(h => h.Timestamp)
                .Select(h => (DateTime?)h.Timestamp)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<List<History>> GetPagedAsync(int limit, int offset, CancellationToken ct = default)
        {
            return await _db.History
                .AsNoTracking()
                .OrderByDescending(h => h.Timestamp)
                .Skip(offset)
                .Take(limit)
                .ToListAsync(ct);
        }

        public async Task<int> CountAsync(CancellationToken ct = default)
        {
            return await _db.History.CountAsync(ct);
        }

        public async Task<List<History>> GetByAudiobookIdAsync(int audiobookId, CancellationToken ct = default)
        {
            return await _db.History
                .AsNoTracking()
                .Where(h => h.AudiobookId == audiobookId)
                .OrderByDescending(h => h.Timestamp)
                .ToListAsync(ct);
        }

        public async Task<List<History>> GetByEventTypeAsync(string eventType, int? limit = null, CancellationToken ct = default)
        {
            var query = _db.History
                .AsNoTracking()
                .Where(h => h.EventType == eventType)
                .OrderByDescending(h => h.Timestamp);

            return limit.HasValue
                ? await query.Take(limit.Value).ToListAsync(ct)
                : await query.ToListAsync(ct);
        }

        public async Task<List<History>> GetPendingMoveScanHandoffsAsync(CancellationToken ct = default)
        {
            return await _db.History
                .AsNoTracking()
                .Where(handoff => handoff.EventType == HistoryEvents.ScanQueued
                    && handoff.Outcome == HistoryOutcome.Requested
                    && handoff.Source == "Move"
                    && !_db.History.Any(terminal =>
                        terminal.CorrelationId == handoff.CorrelationId
                        && ((terminal.EventType == HistoryEvents.ScanCompleted
                                && terminal.Outcome == HistoryOutcome.Succeeded)
                            || (terminal.EventType == HistoryEvents.ScanFailed
                                && terminal.Outcome == HistoryOutcome.Failed))))
                .OrderBy(handoff => handoff.Timestamp)
                .ToListAsync(ct);
        }

        public async Task<List<History>> GetBySourceAsync(string source, int? limit = null, CancellationToken ct = default)
        {
            var query = _db.History
                .AsNoTracking()
                .Where(h => h.Source == source)
                .OrderByDescending(h => h.Timestamp);

            return limit.HasValue
                ? await query.Take(limit.Value).ToListAsync(ct)
                : await query.ToListAsync(ct);
        }

        public async Task<List<History>> GetRecentAsync(int limit, CancellationToken ct = default)
        {
            return await _db.History
                .AsNoTracking()
                .OrderByDescending(h => h.Timestamp)
                .Take(limit)
                .ToListAsync(ct);
        }

        public async Task<History> AddAsync(History entry, CancellationToken ct = default)
        {
            _db.History.Add(entry);
            await _db.SaveChangesAsync(ct);
            return entry;
        }

        public async Task<LeasedHistoryWriteResult> GetOrAddLeasedMoveHistoryAsync(
            History entry,
            Guid moveJobId,
            string leaseOwner,
            int leaseGeneration,
            DateTimeOffset now,
            CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.CorrelationId);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.EventType);
            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(ct)
                : null;
            var nowUtc = now.UtcDateTime;
            var leaseQuery = _db.MoveJobs.Where(job => job.Id == moveJobId
                && job.Status == MoveJobStatus.Running
                && job.LeaseOwner == leaseOwner
                && job.LeaseGeneration == leaseGeneration
                && job.LeaseExpiresAt != null
                && job.LeaseExpiresAt > nowUtc);
            var leaseOwned = _db.Database.IsRelational()
                ? await leaseQuery.ExecuteUpdateAsync(
                    updates => updates.SetProperty(job => job.UpdatedAt, job => job.UpdatedAt),
                    ct) == 1
                : await leaseQuery.AsNoTracking().AnyAsync(ct);
            if (!leaseOwned)
            {
                throw new MoveLeaseLostException(moveJobId, leaseGeneration);
            }

            var existing = await _db.History.AsNoTracking().FirstOrDefaultAsync(
                candidate => candidate.CorrelationId == entry.CorrelationId
                    && candidate.EventType == entry.EventType
                    && candidate.Source == entry.Source,
                ct);
            if (existing != null)
            {
                if (transaction != null)
                {
                    await transaction.CommitAsync(ct);
                }

                return new LeasedHistoryWriteResult(existing, Created: false);
            }

            _db.History.Add(entry);
            await _db.SaveChangesAsync(ct);
            if (transaction != null)
            {
                await transaction.CommitAsync(ct);
            }

            return new LeasedHistoryWriteResult(entry, Created: true);
        }

        public async Task UpdateAsync(History entry, CancellationToken ct = default)
        {
            _db.History.Update(entry);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var entry = await _db.History.FindAsync(new object[] { id }, ct);
            if (entry == null) return false;

            if (IsMoveScanHandoff(entry))
            {
                var terminalExists = await HasTerminalScanHistoryAsync(
                    entry.CorrelationId,
                    ct);
                if (!terminalExists)
                {
                    return false;
                }
            }

            if (IsTerminalScanHistory(entry))
            {
                var completedHandoffs = await _db.History
                    .Where(candidate => candidate.CorrelationId == entry.CorrelationId
                        && candidate.EventType == HistoryEvents.ScanQueued
                        && candidate.Outcome == HistoryOutcome.Requested
                        && candidate.Source == "Move")
                    .ToListAsync(ct);
                _db.History.RemoveRange(completedHandoffs);
            }

            _db.History.Remove(entry);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task DeleteAllAsync(CancellationToken ct = default)
        {
            var deletable = await _db.History
                .Where(history => !(history.EventType == HistoryEvents.ScanQueued
                    && history.Outcome == HistoryOutcome.Requested
                    && history.Source == "Move"
                    && !_db.History.Any(terminal =>
                        terminal.CorrelationId == history.CorrelationId
                        && ((terminal.EventType == HistoryEvents.ScanCompleted
                                && terminal.Outcome == HistoryOutcome.Succeeded)
                            || (terminal.EventType == HistoryEvents.ScanFailed
                                && terminal.Outcome == HistoryOutcome.Failed)))))
                .ToListAsync(ct);
            _db.History.RemoveRange(deletable);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
        {
            var old = await _db.History
                .Where(history => history.Timestamp < cutoff
                    && !(history.EventType == HistoryEvents.ScanQueued
                        && history.Outcome == HistoryOutcome.Requested
                        && history.Source == "Move"
                        && !_db.History.Any(terminal =>
                            terminal.CorrelationId == history.CorrelationId
                            && ((terminal.EventType == HistoryEvents.ScanCompleted
                                    && terminal.Outcome == HistoryOutcome.Succeeded)
                                || (terminal.EventType == HistoryEvents.ScanFailed
                                    && terminal.Outcome == HistoryOutcome.Failed)))))
                .ToListAsync(ct);
            _db.History.RemoveRange(old);
            await _db.SaveChangesAsync(ct);
            return old.Count;
        }

        private static bool IsMoveScanHandoff(History history) =>
            history.EventType == HistoryEvents.ScanQueued
            && history.Outcome == HistoryOutcome.Requested
            && history.Source == "Move";

        private static bool IsTerminalScanHistory(History history) =>
            history.EventType == HistoryEvents.ScanCompleted
                && history.Outcome == HistoryOutcome.Succeeded
            || history.EventType == HistoryEvents.ScanFailed
                && history.Outcome == HistoryOutcome.Failed;

        private Task<bool> HasTerminalScanHistoryAsync(
            string correlationId,
            CancellationToken cancellationToken) =>
            _db.History.AnyAsync(history =>
                history.CorrelationId == correlationId
                && ((history.EventType == HistoryEvents.ScanCompleted
                        && history.Outcome == HistoryOutcome.Succeeded)
                    || (history.EventType == HistoryEvents.ScanFailed
                        && history.Outcome == HistoryOutcome.Failed)),
                cancellationToken);

    }
}
