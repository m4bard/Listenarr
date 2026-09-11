/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories;

public partial class AudiobookRepository
{
    /// <summary>
    /// Books whose provider metadata has never been refreshed, or was refreshed before
    /// <paramref name="staleBefore"/>. Never-refreshed first, then oldest first, so progress is
    /// monotonic and needs no run-level bookkeeping.
    /// </summary>
    public async Task<List<MetadataRefreshCandidate>> GetAudiobooksDueForMetadataRefreshAsync(
        DateTime staleBefore,
        int limit,
        CancellationToken ct = default)
    {
        if (limit <= 0)
        {
            return [];
        }

        // Authors is a JSON-backed list column, so the projection materializes the two scalar
        // columns SQLite can order on and picks the primary author afterwards.
        var rows = await _db.Audiobooks
            .AsNoTracking()
            .Where(audiobook => audiobook.LastMetadataRefreshAt == null
                || audiobook.LastMetadataRefreshAt < staleBefore)
            .OrderBy(audiobook => audiobook.LastMetadataRefreshAt.HasValue)
            .ThenBy(audiobook => audiobook.LastMetadataRefreshAt)
            .ThenBy(audiobook => audiobook.Id)
            .Take(limit)
            .Select(audiobook => new
            {
                audiobook.Id,
                audiobook.Authors,
                audiobook.LastMetadataRefreshAt
            })
            .ToListAsync(ct);

        return rows
            .Select(row => new MetadataRefreshCandidate(
                row.Id,
                row.Authors?.FirstOrDefault(author => !string.IsNullOrWhiteSpace(author)),
                row.LastMetadataRefreshAt))
            .ToList();
    }

    /// <summary>
    /// Ids of every book whose author list contains <paramref name="authorName"/> after the same
    /// normalization the monitored-author rows use.
    /// </summary>
    public async Task<List<int>> GetAudiobookIdsByAuthorNameAsync(
        string authorName,
        CancellationToken ct = default)
    {
        var target = NormalizeAuthorName(authorName);
        if (string.IsNullOrEmpty(target))
        {
            return [];
        }

        // Materialize first: SQLite cannot translate list-property checks on our JSON columns.
        var candidates = await _db.Audiobooks
            .AsNoTracking()
            .Select(audiobook => new { audiobook.Id, audiobook.Authors })
            .ToListAsync(ct);

        return candidates
            .Where(candidate => candidate.Authors != null
                && candidate.Authors.Any(author => NormalizeAuthorName(author) == target))
            .Select(candidate => candidate.Id)
            .OrderBy(id => id)
            .ToList();
    }

    /// <summary>
    /// The due query's staleness predicate over a given set of ids: one IN clause plus the same
    /// null-or-older test. The author scope filters with this so it never materializes the whole
    /// library's due set, and every row it does read is a single integer.
    /// </summary>
    public async Task<List<int>> FilterAudiobookIdsDueForMetadataRefreshAsync(
        IReadOnlyCollection<int> audiobookIds,
        DateTime staleBefore,
        CancellationToken ct = default)
    {
        if (audiobookIds == null || audiobookIds.Count == 0)
        {
            return [];
        }

        // A List is what EF turns into an IN clause. An IReadOnlyCollection is not guaranteed to
        // translate, and falling back to client evaluation here would defeat the point.
        var ids = audiobookIds as List<int> ?? [.. audiobookIds];

        return await _db.Audiobooks
            .AsNoTracking()
            .Where(audiobook => ids.Contains(audiobook.Id)
                && (audiobook.LastMetadataRefreshAt == null
                    || audiobook.LastMetadataRefreshAt < staleBefore))
            .OrderBy(audiobook => audiobook.Id)
            .Select(audiobook => audiobook.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Records that a book has been through a refresh. Only called for outcomes that should not
    /// be tried again this staleness window; a deferral or a conflict leaves the value alone,
    /// because stamping a rate-limited miss would hide the book for a month.
    /// </summary>
    public async Task<bool> StampMetadataRefreshAsync(
        int audiobookId,
        DateTime refreshedAtUtc,
        CancellationToken ct = default)
    {
        var updated = await _db.Audiobooks
            .Where(audiobook => audiobook.Id == audiobookId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    audiobook => audiobook.LastMetadataRefreshAt,
                    refreshedAtUtc),
                ct);

        return updated > 0;
    }
}
