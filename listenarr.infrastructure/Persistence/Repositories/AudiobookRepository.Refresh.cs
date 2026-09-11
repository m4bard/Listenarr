/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories;

public partial class AudiobookRepository
{
    /// <summary>
    /// A book being written for the first time counts as just refreshed, because it is: the
    /// metadata it is being created from was fetched from the provider moments ago.
    /// </summary>
    /// <remarks>
    /// Without this a new book goes in with a null timestamp, which the refresh queue reads
    /// as "never refreshed" and sorts to the very front. A book added on Tuesday is then
    /// re-fetched on Tuesday, ahead of a library of books that have genuinely gone stale,
    /// and the budget it spends doing it is budget those books do not get.
    /// <para>
    /// Set here and in the atomic add's commit store, the only two places a row is inserted.
    /// Not in SaveChanges: stamping every Added entity would also stamp the rows a test or a
    /// fixture inserts to stand for an upgraded library, which is the one case that has to
    /// keep arriving with no timestamp.
    /// </para>
    /// </remarks>
    internal static void StampNewAudiobookRefreshTimestamp(Audiobook audiobook)
    {
        audiobook.LastMetadataRefreshAt ??= DateTime.UtcNow;
    }

    /// <summary>
    /// Books whose provider metadata has never been refreshed, or was refreshed before
    /// <paramref name="staleBefore"/>. Never-refreshed first, then oldest first, so progress is
    /// monotonic and needs no run-level bookkeeping.
    /// </summary>
    /// <remarks>
    /// The ordering is one plain ascending term and the id, and that is deliberate. SQLite sorts
    /// nulls first ascending on its own, so an explicit "has a value" term ahead of it buys the
    /// same order and costs the index: a non-unique SQLite index carries the rowid as its last
    /// column, and Id is the rowid, so <c>ORDER BY "LastMetadataRefreshAt", "Id"</c> is exactly
    /// the order IX_Audiobooks_LastMetadataRefreshAt is already in. Put a synthetic first term in
    /// front of it and no index matches the sort any more, so the plan grows a temp B-tree over
    /// every due row before it can take the first few.
    /// </remarks>
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
            .OrderBy(audiobook => audiobook.LastMetadataRefreshAt)
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
    /// <remarks>
    /// Two queries, neither of which reads the whole table. The first expands the JSON author
    /// list with <c>json_each</c> and narrows to the rows that could possibly match; the second
    /// reads the author JSON of only those rows, and the exact comparison is still the C#
    /// normalizer, which is the only thing that defines what a match is.
    /// <para>
    /// Reading every row's author JSON back to answer a question about one author cost the whole
    /// table on every per-author trigger, and a deserialization per row on top of it.
    /// </para>
    /// </remarks>
    public async Task<List<int>> GetAudiobookIdsByAuthorNameAsync(
        string authorName,
        CancellationToken ct = default)
    {
        var target = NormalizeAuthorName(authorName);
        if (string.IsNullOrEmpty(target))
        {
            return [];
        }

        var narrowed = await NarrowAuthorCandidateIdsAsync(target, ct);
        if (narrowed is { Count: 0 })
        {
            return [];
        }

        var rows = _db.Audiobooks.AsNoTracking();
        if (narrowed != null)
        {
            rows = rows.Where(audiobook => narrowed.Contains(audiobook.Id));
        }

        var candidates = await rows
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
    /// Ids whose author list could contain <paramref name="normalizedTarget"/>, or null when the
    /// target gives nothing to narrow on and the caller has to read the table after all.
    /// </summary>
    /// <remarks>
    /// The normalizer only deletes characters and lowercases them; it never reorders or inserts.
    /// So every ASCII letter and digit of the normalized target must appear, in that order, in
    /// the lowercased stored value, which is what the <c>%a%b%c%</c> pattern asks. That makes the
    /// result a superset and never drops a row the exact comparison would have kept, including
    /// the punctuation-only differences ("A. Writer" against "a writer") the normalizer exists
    /// for. Non-ASCII characters are left out of the pattern rather than matched, because
    /// SQLite's <c>lower</c> is ASCII-only and folding them there would disagree with
    /// <c>ToLowerInvariant</c>.
    /// <para>
    /// A row whose author column is not valid JSON is kept rather than dropped. <c>json_each</c>
    /// cannot expand one, but the value converter can still read it: it tolerates legacy bare
    /// strings that begin with a digit or with t, f, n or -, wrapping them into a single-item
    /// list. Such a row matches in C# and would have been discarded here, which is the one way
    /// this narrowing could stop being a superset. There are few of them and they only appear on
    /// upgraded databases, so they cost one extra row read each.
    /// </para>
    /// </remarks>
    private async Task<List<int>?> NarrowAuthorCandidateIdsAsync(
        string normalizedTarget,
        CancellationToken ct)
    {
        var pattern = BuildAuthorSubsequencePattern(normalizedTarget);
        if (pattern == null || !_db.Database.IsRelational())
        {
            return null;
        }

        return await _db.Database
            .SqlQueryRaw<int>(
                """
                SELECT a."Id" AS "Value"
                FROM "Audiobooks" AS a
                WHERE a."Authors" IS NOT NULL
                  AND (
                      NOT json_valid(a."Authors")
                      OR EXISTS (
                          SELECT 1 FROM json_each(a."Authors") AS j
                          WHERE j."value" IS NOT NULL AND lower(j."value") LIKE {0}
                      )
                  )
                """,
                pattern)
            .ToListAsync(ct);
    }

    /// <summary>
    /// <c>%j%u%l%e%s%v%e%r%n%e%</c> for "jules verne". Null when the name carries no ASCII
    /// letters or digits at all, which would make the pattern a bare wildcard.
    /// </summary>
    private static string? BuildAuthorSubsequencePattern(string normalizedTarget)
    {
        var pattern = new StringBuilder("%");
        var usable = 0;
        foreach (var character in normalizedTarget)
        {
            // Already lowercased by the normalizer. LIKE's own metacharacters cannot appear:
            // the normalizer keeps only letters, digits and the spaces it puts between words.
            if (character is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                pattern.Append(character).Append('%');
                usable++;
            }
        }

        return usable == 0 ? null : pattern.ToString();
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

    /// <summary>
    /// Gives every never-refreshed row the time of this backfill, and returns how many rows it
    /// wrote.
    /// </summary>
    /// <remarks>
    /// A startup primitive rather than data repair inside the migration that adds the column:
    /// migrations here stay direct EF scaffolds, and anything that touches rows belongs on the
    /// startup path where it can be read, tested and run again.
    /// <para>
    /// The time of the backfill, and not a date read off the book. Seeding from a file's
    /// CreatedAt or from LastSearchTime sounds better than it is: both are usually older than
    /// the staleness window, so every such row arrives already due, and an upgrade that was
    /// meant to start the clock instead schedules the whole library at once. Stamping the
    /// present gives an upgraded install one full staleness window of grace, after which its
    /// books age into the queue the way books added after the upgrade do.
    /// </para>
    /// <para>
    /// The cost is that an upgraded library comes due in a clump one window later rather than
    /// spread over the period it was built. That clump is metered by the run budget, which is
    /// what bounds provider traffic; arriving due on the first cycle after an upgrade is not
    /// metered by anything the operator chose.
    /// </para>
    /// </remarks>
    public async Task<int> BackfillMetadataRefreshTimestampsAsync(CancellationToken ct = default)
    {
        var backfilledAt = DateTime.UtcNow;

        return await _db.Audiobooks
            .Where(audiobook => audiobook.LastMetadataRefreshAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    audiobook => audiobook.LastMetadataRefreshAt,
                    backfilledAt),
                ct);
    }
}
