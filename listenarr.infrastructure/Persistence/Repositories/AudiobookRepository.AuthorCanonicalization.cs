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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Listenarr.Infrastructure.Persistence.Repositories;

public partial class AudiobookRepository
{
    private const int AuthorCanonicalizationBatchSize = 200;

    /// <summary>
    /// Re-derives AuthorNameNormalized on AuthorCacheEntries and MonitoredAuthors using the
    /// shared author normalizer. Rows written by an earlier normalizer carry keys the current
    /// reader will never produce, which makes them unreachable by name and makes any join on
    /// that column silently skip exactly the drifted rows it was meant to find.
    /// Idempotent: a row whose stored key already equals the re-derived one is left alone.
    /// </summary>
    public async Task<AuthorNameKeyRederivationResult> RederiveAuthorNameKeysAsync(
        CancellationToken ct = default)
    {
        var cacheEntries = await _db.AuthorCacheEntries.ToListAsync(ct);
        var (cacheCorrected, cacheSkipped) = RederiveKeys(
            cacheEntries,
            entry => entry.AuthorName,
            entry => entry.AuthorNameNormalized,
            (entry, key) => entry.AuthorNameNormalized = key,
            entry => AudiobookIdentifierNormalizer.NormalizeRegion(entry.Region) ?? "us",
            entry => (entry.LastFetchedAt ?? entry.UpdatedAt, entry.Id));

        var monitoredAuthors = await _db.MonitoredAuthors.ToListAsync(ct);
        var (monitoredCorrected, monitoredSkipped) = RederiveKeys(
            monitoredAuthors,
            author => author.AuthorName,
            author => author.AuthorNameNormalized,
            (author, key) => author.AuthorNameNormalized = key,
            // MonitoredAuthors is unique on (name, region, language), so language is part of
            // the slot a re-derived key competes for.
            author => $"{AudiobookIdentifierNormalizer.NormalizeRegion(author.Region) ?? "us"}:{author.Language}",
            author => (author.UpdatedAt, author.Id));

        if (cacheCorrected > 0 || monitoredCorrected > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        return new AuthorNameKeyRederivationResult(
            cacheCorrected,
            monitoredCorrected,
            cacheSkipped + monitoredSkipped);
    }

    /// <summary>
    /// Adopts the cached author identity's spelling for stored per-book author strings that
    /// normalize identically to it. Name-first, never index-first: AuthorAsins is a
    /// deduplicated set of successful lookups and is not positionally parallel to Authors.
    /// The normalizer-equality condition means this can only ever change how an author is
    /// spelled, never which author a book is credited to.
    /// Offline, idempotent, and a no-op on a library with no drift.
    /// </summary>
    public async Task<int> CanonicalizeStoredAuthorNamesAsync(CancellationToken ct = default)
    {
        var canonicalNames = await BuildCanonicalAuthorNameMapAsync(ct);
        if (canonicalNames.Count == 0)
        {
            return 0;
        }

        var regionsByAudiobookId = await BuildAudiobookRegionMapAsync(ct);

        var updated = 0;
        var lastId = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await _db.Audiobooks
                .AsNoTracking()
                .Where(audiobook => audiobook.Id > lastId)
                .OrderBy(audiobook => audiobook.Id)
                .Take(AuthorCanonicalizationBatchSize)
                .Select(audiobook => new { audiobook.Id, audiobook.Authors })
                .ToListAsync(ct);
            if (page.Count == 0)
            {
                break;
            }

            lastId = page[^1].Id;
            var pageUpdates = 0;
            var attached = new List<EntityEntry<Audiobook>>();
            foreach (var row in page)
            {
                var region = regionsByAudiobookId.TryGetValue(row.Id, out var derived) ? derived : "us";
                var canonicalized = CanonicalizeAuthorList(row.Authors, region, canonicalNames);
                if (canonicalized == null)
                {
                    continue;
                }

                // Only the Authors column is marked modified. UpdateAsync must not be used here:
                // it restores BasePath, FilePath, FileSize and ImageUrl on detached entities for
                // reasons unrelated to this pass, and a whole-entity write from a stub would
                // blank every other column.
                var entry = _db.ChangeTracker.Entries<Audiobook>()
                    .FirstOrDefault(candidate => candidate.Entity.Id == row.Id);
                if (entry == null)
                {
                    entry = _db.Attach(new Audiobook { Id = row.Id, Authors = canonicalized });
                    attached.Add(entry);
                }
                else
                {
                    entry.Entity.Authors = canonicalized;
                }

                entry.Property(audiobook => audiobook.Authors).CurrentValue = canonicalized;
                entry.Property(audiobook => audiobook.Authors).IsModified = true;
                pageUpdates++;
            }

            if (pageUpdates > 0)
            {
                await _db.SaveChangesAsync(ct);
                // Release only the stubs this pass attached; entities the caller's scope was
                // already tracking are none of our business.
                foreach (var entry in attached)
                {
                    entry.State = EntityState.Detached;
                }

                updated += pageUpdates;
            }
        }

        return updated;
    }

    /// <summary>
    /// Returns the rewritten author list, or null when nothing in it needed correcting.
    /// </summary>
    private static List<string>? CanonicalizeAuthorList(
        List<string>? authors,
        string region,
        IReadOnlyDictionary<(string NormalizedName, string Region), string> canonicalNames)
    {
        if (authors == null || authors.Count == 0)
        {
            return null;
        }

        List<string>? rewritten = null;
        for (var index = 0; index < authors.Count; index++)
        {
            var stored = authors[index];
            var normalized = StringUtils.NormalizeAuthorName(stored);
            if (string.IsNullOrEmpty(normalized))
            {
                continue;
            }

            if (!canonicalNames.TryGetValue((normalized, region), out var canonical))
            {
                // No cache row for this author: leave it for the on-add path and for a rescan.
                // The pass never invents a canonical name.
                continue;
            }

            if (string.Equals(stored, canonical, StringComparison.Ordinal))
            {
                continue;
            }

            // Belt and braces: the map is keyed on the cache row's own re-derived key, so this
            // always holds, but the guard is what makes the write provably spelling-only.
            if (!string.Equals(
                    StringUtils.NormalizeAuthorName(canonical),
                    normalized,
                    StringComparison.Ordinal))
            {
                continue;
            }

            rewritten ??= new List<string>(authors);
            rewritten[index] = canonical;
        }

        return rewritten;
    }

    /// <summary>
    /// Maps (re-derived normalized name, region) to the cache row's display spelling. Keyed on a
    /// freshly derived key rather than the stored column so the map is correct even if the
    /// re-derivation pass has not run against this database yet.
    /// </summary>
    private async Task<Dictionary<(string NormalizedName, string Region), string>>
        BuildCanonicalAuthorNameMapAsync(CancellationToken ct)
    {
        var rows = await _db.AuthorCacheEntries
            .AsNoTracking()
            .Select(entry => new
            {
                entry.Id,
                entry.AuthorName,
                entry.Region,
                entry.LastFetchedAt,
                entry.UpdatedAt
            })
            .ToListAsync(ct);

        var map = new Dictionary<(string, string), string>();
        foreach (var group in rows
            .Select(row => new
            {
                row.Id,
                row.AuthorName,
                Normalized = StringUtils.NormalizeAuthorName(row.AuthorName),
                Region = AudiobookIdentifierNormalizer.NormalizeRegion(row.Region) ?? "us",
                Freshness = row.LastFetchedAt ?? row.UpdatedAt
            })
            .Where(row => !string.IsNullOrEmpty(row.Normalized)
                && !string.IsNullOrWhiteSpace(row.AuthorName))
            .GroupBy(row => (row.Normalized, row.Region)))
        {
            // The unique index makes this a single row in practice. Where it is not, pick the
            // same row GetCachedAuthorByNameAsync would return so reads and this pass agree.
            var winner = group
                .OrderByDescending(row => row.Freshness)
                .ThenByDescending(row => row.Id)
                .First();
            map[group.Key] = winner.AuthorName.Trim();
        }

        return map;
    }

    /// <summary>
    /// Derives each book's region from its own external identifiers, preferring the primary
    /// identifier, and falling back to the house default used throughout this repository.
    /// A book's region is stable, so the pass converges rather than oscillating between two
    /// cache rows that share a normalized name across regions.
    /// </summary>
    private async Task<Dictionary<int, string>> BuildAudiobookRegionMapAsync(CancellationToken ct)
    {
        var identifiers = await _db.AudiobookExternalIdentifiers
            .AsNoTracking()
            .Where(identifier => identifier.Region != null)
            .Select(identifier => new
            {
                identifier.AudiobookId,
                identifier.Region,
                identifier.IsPrimary,
                identifier.Id
            })
            .ToListAsync(ct);

        return identifiers
            .Select(identifier => new
            {
                identifier.AudiobookId,
                Region = AudiobookIdentifierNormalizer.NormalizeRegion(identifier.Region),
                identifier.IsPrimary,
                identifier.Id
            })
            .Where(identifier => !string.IsNullOrEmpty(identifier.Region))
            .GroupBy(identifier => identifier.AudiobookId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(identifier => identifier.IsPrimary)
                    .ThenBy(identifier => identifier.Id)
                    .First()
                    .Region!);
    }

    /// <summary>
    /// Re-derives one table's normalized-name column. A re-derived key that would collide with a
    /// slot another row already holds is left alone rather than written, because the uniqueness
    /// constraint would otherwise fail the whole startup pass. Processing order is deterministic,
    /// so the same row wins the slot on every boot.
    /// </summary>
    private static (int Corrected, int Skipped) RederiveKeys<T>(
        IReadOnlyCollection<T> rows,
        Func<T, string> readDisplayName,
        Func<T, string> readStoredKey,
        Action<T, string> writeStoredKey,
        Func<T, string> readSlot,
        Func<T, (DateTime Freshness, int Id)> readPrecedence)
    {
        var pending = new List<(T Row, string Target, string Slot)>();
        var occupied = new HashSet<(string Key, string Slot)>();
        foreach (var row in rows)
        {
            var slot = readSlot(row);
            var target = StringUtils.NormalizeAuthorName(readDisplayName(row));
            var stored = readStoredKey(row);
            if (string.IsNullOrEmpty(target) || string.Equals(target, stored, StringComparison.Ordinal))
            {
                occupied.Add((stored, slot));
                continue;
            }

            pending.Add((row, target, slot));
        }

        var corrected = 0;
        var skipped = 0;
        foreach (var candidate in pending
            .OrderByDescending(candidate => readPrecedence(candidate.Row).Freshness)
            .ThenByDescending(candidate => readPrecedence(candidate.Row).Id))
        {
            if (!occupied.Add((candidate.Target, candidate.Slot)))
            {
                // The row keeps the key it already has, so that slot stays taken too.
                occupied.Add((readStoredKey(candidate.Row), candidate.Slot));
                skipped++;
                continue;
            }

            writeStoredKey(candidate.Row, candidate.Target);
            corrected++;
        }

        return (corrected, skipped);
    }
}
