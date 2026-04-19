using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Listenarr.Application.Repositories;
using Listenarr.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Listenarr.Infrastructure.Models;

namespace Listenarr.Api.Tests
{
    /// <summary>
    /// Lightweight test repository used by unit tests when a real IDbContextFactory is not provided.
    /// If a `ListenArrDbContext` is provided to the factory, operations are performed against it;
    /// otherwise this falls back to an in-memory dictionary to allow simple tests to run.
    /// </summary>
    public class TestDownloadRepository : IDownloadRepository
    {
        private readonly ListenArrDbContext? _db;
        private readonly ConcurrentDictionary<string, Download> _mem = new();

        public TestDownloadRepository(ListenArrDbContext? db = null)
        {
            _db = db;
        }

        public Task AddAsync(Download download)
        {
            if (_db != null)
            {
                _db.Downloads.Add(download);
                return _db.SaveChangesAsync();
            }

            _mem[download.Id] = download;
            return Task.CompletedTask;
        }

        public Task<Download?> FindAsync(string id)
        {
            if (_db != null)
                return _db.Downloads.FindAsync(id).AsTask();

            _mem.TryGetValue(id, out var d);
            return Task.FromResult(d);
        }

        public Task UpdateAsync(Download download)
        {
            if (_db != null)
            {
                _db.Downloads.Update(download);
                return _db.SaveChangesAsync();
            }

            _mem[download.Id] = download;
            return Task.CompletedTask;
        }

        public Task UpdateMetadataAsync(string id, string key, object? value)
        {
            if (_db != null)
            {
                var d = _db.Downloads.Find(id);
                if (d == null) return Task.CompletedTask;
                if (d.Metadata == null) d.Metadata = new Dictionary<string, object>();
                d.Metadata[key] = value ?? string.Empty;
                _db.Downloads.Update(d);
                return _db.SaveChangesAsync();
            }

            if (_mem.TryGetValue(id, out var mem))
            {
                if (mem.Metadata == null) mem.Metadata = new Dictionary<string, object>();
                mem.Metadata[key] = value ?? string.Empty;
            }

            return Task.CompletedTask;
        }

        public Task RemoveAsync(string id)
        {
            if (_db != null)
            {
                var d = _db.Downloads.Find(id);
                if (d != null)
                {
                    _db.Downloads.Remove(d);
                    return _db.SaveChangesAsync();
                }
                return Task.CompletedTask;
            }

            _mem.TryRemove(id, out _);
            return Task.CompletedTask;
        }

        public Task<List<Download>> GetAllAsync()
        {
            if (_db != null)
                return _db.Downloads.ToListAsync();

            return Task.FromResult(_mem.Values.ToList());
        }

        public Task<List<QueueTrackedDownload>> GetQueueDisplayCandidatesAsync()
        {
            if (_db != null)
            {
                var projected = _db.Downloads
                    .Where(IsQueueDisplayCandidate)
                    .Select(ToQueueTrackedDownloadProjection)
                    .ToList();
                return Task.FromResult(projected);
            }

            var list = _mem.Values
                .Where(IsQueueDisplayCandidate)
                .Select(ToQueueTrackedDownloadProjection)
                .ToList();
            return Task.FromResult(list);
        }

        private static bool IsQueueDisplayCandidate(Download d)
        {
            bool isDdl = d.DownloadClientId == "DDL";
            bool notMoved = d.Status != DownloadStatus.Moved;
            bool notFailed = d.Status != DownloadStatus.Failed;
            bool notCompletedWithPath = d.Status != DownloadStatus.Completed || string.IsNullOrEmpty(d.FinalPath);
            return (isDdl && notMoved) || (!isDdl && notMoved && notFailed && notCompletedWithPath);
        }

        public Task<List<QueueTrackedDownload>> GetQueueMatchingCandidatesAsync()
        {
            if (_db != null)
            {
                var projected = _db.Downloads
                    .Where(d => d.DownloadClientId != "DDL" && d.Status != DownloadStatus.Failed)
                    .Select(ToQueueTrackedDownloadProjection)
                    .ToList();
                return Task.FromResult(projected);
            }

            var list = _mem.Values
                .Where(d => d.DownloadClientId != "DDL" && d.Status != DownloadStatus.Failed)
                .Select(ToQueueTrackedDownloadProjection)
                .ToList();
            return Task.FromResult(list);
        }

        public Task<List<string>> GetKnownClientItemIdsAsync()
        {
            var metadataEntries = _db != null
                ? _db.Downloads
                    .AsNoTracking()
                    .Select(d => d.Metadata)
                    .ToList()
                : _mem.Values.Select(d => d.Metadata).ToList();

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var metadata in metadataEntries)
            {
                if (metadata == null)
                {
                    continue;
                }

                if (metadata.TryGetValue("ClientDownloadId", out var clientDownloadId) && !string.IsNullOrWhiteSpace(clientDownloadId?.ToString()))
                {
                    ids.Add(clientDownloadId.ToString()!);
                }

                if (metadata.TryGetValue("TorrentHash", out var torrentHash) && !string.IsNullOrWhiteSpace(torrentHash?.ToString()))
                {
                    ids.Add(torrentHash.ToString()!);
                }
            }

            return Task.FromResult(ids.ToList());
        }

        public Task<List<Download>> GetByClientAsync(string clientId)
        {
            if (_db != null)
                return _db.Downloads.Where(d => d.DownloadClientId == clientId).ToListAsync();

            var list = _mem.Values.Where(d => d.DownloadClientId == clientId).ToList();
            return Task.FromResult(list);
        }

        public Task<List<Download>> GetByIdsAsync(IEnumerable<string> ids)
        {
            var idSet = ids?.ToList() ?? new List<string>();
            if (_db != null)
                return _db.Downloads.Where(d => idSet.Contains(d.Id)).ToListAsync();

            var list = _mem.Values.Where(d => idSet.Contains(d.Id)).ToList();
            return Task.FromResult(list);
        }

        public Task<List<Download>> GetByAudiobookIdAsync(int audiobookId, System.Threading.CancellationToken ct = default)
        {
            if (_db != null)
                return _db.Downloads.Where(d => d.AudiobookId == audiobookId).ToListAsync(ct);

            var list = _mem.Values.Where(d => d.AudiobookId == audiobookId).ToList();
            return Task.FromResult(list);
        }

        public Task<List<Download>> GetCompletionCandidatesAsync(int limit)
        {
            if (_db != null)
                return _db.Downloads
                    .Where(d => d.Status == DownloadStatus.Completed || d.Status == DownloadStatus.ImportPending || d.Status == DownloadStatus.Processing)
                    .OrderByDescending(d => d.CompletedAt)
                    .Take(limit)
                    .ToListAsync();

            var list = _mem.Values
                .Where(d => d.Status == DownloadStatus.Completed || d.Status == DownloadStatus.ImportPending || d.Status == DownloadStatus.Processing)
                .OrderByDescending(d => d.CompletedAt)
                .Take(limit)
                .ToList();
            return Task.FromResult(list);
        }

        public Task<List<Download>> GetActiveForMonitoringAsync()
        {
            bool IsActive(Download d) =>
                d.Status == DownloadStatus.Queued ||
                d.Status == DownloadStatus.Downloading ||
                d.Status == DownloadStatus.Paused ||
                d.Status == DownloadStatus.Processing ||
                ((d.Status == DownloadStatus.Completed || d.Status == DownloadStatus.ImportPending) && string.IsNullOrEmpty(d.FinalPath)) ||
                (d.Status == DownloadStatus.Moved && !string.IsNullOrEmpty(d.DownloadClientId));

            if (_db != null)
                return _db.Downloads.Where(d =>
                    d.Status == DownloadStatus.Queued ||
                    d.Status == DownloadStatus.Downloading ||
                    d.Status == DownloadStatus.Paused ||
                    d.Status == DownloadStatus.Processing ||
                    ((d.Status == DownloadStatus.Completed || d.Status == DownloadStatus.ImportPending) && (d.FinalPath == null || d.FinalPath == "")) ||
                    (d.Status == DownloadStatus.Moved && d.DownloadClientId != null && d.DownloadClientId != ""))
                    .ToListAsync();

            return Task.FromResult(_mem.Values.Where(IsActive).ToList());
        }

        public Task<List<Download>> GetRecentAsync(int count)
        {
            if (_db != null)
                return _db.Downloads.OrderByDescending(d => d.StartedAt).Take(count).ToListAsync();

            return Task.FromResult(_mem.Values.OrderByDescending(d => d.StartedAt).Take(count).ToList());
        }

        public Task<List<int>> GetActiveAudiobookIdsAsync(IEnumerable<DownloadStatus> statuses)
        {
            var statusList = statuses.ToList();
            if (_db != null)
                return _db.Downloads
                    .Where(d => d.AudiobookId.HasValue && statusList.Contains(d.Status))
                    .Select(d => d.AudiobookId!.Value)
                    .Distinct()
                    .ToListAsync();

            var list = _mem.Values
                .Where(d => d.AudiobookId.HasValue && statusList.Contains(d.Status))
                .Select(d => d.AudiobookId!.Value)
                .Distinct()
                .ToList();
            return Task.FromResult(list);
        }

        private static QueueTrackedDownload ToQueueTrackedDownloadProjection(Download download)
        {
            return new QueueTrackedDownload
            {
                Id = download.Id,
                DownloadClientId = download.DownloadClientId,
                Title = download.Title,
                Artist = download.Artist,
                Status = download.Status,
                StartedAt = download.StartedAt,
                TotalSize = download.TotalSize,
                DownloadedSize = download.DownloadedSize,
                DownloadPath = download.DownloadPath,
                FinalPath = download.FinalPath,
                Metadata = download.Metadata
            };
        }
    }
}
