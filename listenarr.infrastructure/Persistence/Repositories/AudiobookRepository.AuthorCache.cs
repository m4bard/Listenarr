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
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Reading and writing author identity: which Audible ASIN is bound to which author name.
    /// The binding is durable and self-confirming -- once a row is cached the remote lookup is
    /// never consulted for that author again -- so everything here is biased towards declining
    /// rather than guessing.
    /// </summary>
    public partial class AudiobookRepository
    {
        /// <summary>
        /// Answers with a book's author ASIN only where the book pairs the two unambiguously:
        /// one credited author and one recorded ASIN. AuthorAsins is a deduplicated set of
        /// successful lookups and is not positionally parallel to Authors, so on any other book
        /// there is no right answer to return -- the information needed to pick one was never
        /// stored. Declining is deliberate, and the hits it gives up are the wrong ones.
        /// </summary>
        public async Task<string?> GetAuthorAsinByNameAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            var target = StringUtils.NormalizeAuthorName(name);
            if (string.IsNullOrWhiteSpace(target)) return null;

            // Materialize first because SQLite cannot translate list-property checks on our JSON-backed columns.
            var candidates = await _db.Audiobooks
                .AsNoTracking()
                .ToListAsync();

            foreach (var b in candidates)
            {
                if (b.AuthorAsins == null || b.AuthorAsins.Count == 0 || b.Authors == null || b.Authors.Count == 0)
                {
                    continue;
                }

                // AuthorAsins is a de-duplicated bag, not a positional mirror of Authors. Enrichment
                // skips any name the metadata source cannot resolve and drops repeats, so position i
                // in one list says nothing about position i in the other. A book can therefore only
                // attribute an ASIN to a name when it credits a single author and carries a single
                // ASIN. Matching any credited name and then taking the first ASIN handed one
                // author's identifier to every co-author credited on the same book -- and comparing
                // Authors.Count directly (rather than the distinct normalized count) rejected a book
                // that credits the same person twice under different casing, which is one author,
                // not two.
                var bookAuthors = b.Authors
                    .Select(StringUtils.NormalizeAuthorName)
                    .Where(author => !string.IsNullOrWhiteSpace(author))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var bookAsins = b.AuthorAsins
                    .Where(asin => !string.IsNullOrWhiteSpace(asin))
                    .Select(asin => asin.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (bookAuthors.Count != 1 || bookAsins.Count != 1)
                {
                    continue;
                }

                if (bookAuthors[0] == target)
                {
                    return bookAsins[0];
                }
            }

            return null;
        }

        public async Task<AuthorCacheEntry?> GetCachedAuthorByNameAsync(string name, string region)
        {
            var normalizedName = StringUtils.NormalizeAuthorName(name);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return null;
            }

            var normalizedRegion = AudiobookIdentifierNormalizer.NormalizeRegion(region) ?? "us";

            return await _db.AuthorCacheEntries
                .AsNoTracking()
                // COALESCE form — SQLite EF can't translate Nullable.GetValueOrDefault (it throws,
                // and the caller's best-effort catch then silently disables this cache).
                .OrderByDescending(entry => entry.LastFetchedAt ?? entry.UpdatedAt)
                .FirstOrDefaultAsync(entry =>
                    entry.AuthorNameNormalized == normalizedName &&
                    entry.Region == normalizedRegion);
        }

        public async Task<AuthorCacheEntry?> GetCachedAuthorByAsinAsync(string asin, string region)
        {
            var normalizedAsin = NormalizeAsin(asin);
            if (string.IsNullOrWhiteSpace(normalizedAsin))
            {
                return null;
            }

            var normalizedRegion = AudiobookIdentifierNormalizer.NormalizeRegion(region) ?? "us";

            return await _db.AuthorCacheEntries
                .AsNoTracking()
                // COALESCE form — SQLite EF can't translate Nullable.GetValueOrDefault (it throws,
                // and the caller's best-effort catch then silently disables this cache).
                .OrderByDescending(entry => entry.LastFetchedAt ?? entry.UpdatedAt)
                .FirstOrDefaultAsync(entry =>
                    entry.AuthorAsin != null &&
                    entry.AuthorAsin.ToUpper() == normalizedAsin &&
                    entry.Region == normalizedRegion);
        }

        public async Task<AuthorCacheEntry> UpsertCachedAuthorAsync(AuthorCacheEntry authorCacheEntry)
        {
            ArgumentNullException.ThrowIfNull(authorCacheEntry);

            var normalizedName = StringUtils.NormalizeAuthorName(authorCacheEntry.AuthorName);
            var normalizedRegion = AudiobookIdentifierNormalizer.NormalizeRegion(authorCacheEntry.Region) ?? "us";
            var normalizedAsin = NormalizeAsin(authorCacheEntry.AuthorAsin);

            for (var attempt = 1; ; attempt++)
            {
                AuthorCacheEntry? existing = null;

                if (!string.IsNullOrWhiteSpace(normalizedAsin))
                {
                    existing = await _db.AuthorCacheEntries.FirstOrDefaultAsync(entry =>
                        entry.AuthorAsin != null &&
                        entry.AuthorAsin.ToUpper() == normalizedAsin &&
                        entry.Region == normalizedRegion);

                    if (existing != null &&
                        !string.IsNullOrWhiteSpace(normalizedName) &&
                        !StringUtils.MatchesAuthorKey(existing.AuthorNameNormalized, existing.AuthorName, normalizedName))
                    {
                        // The ASIN-first match becomes a miss here, and only here. Falling through to
                        // the name lookup lets this write land on its own row, or make one, so both
                        // rows survive and neither is renamed. Sharing an ASIN across rows stays
                        // allowed -- two spellings of one author legitimately produce two rows with
                        // one ASIN, which is why (AuthorAsin, Region) is not a unique index.
                        //
                        // Logged on the first attempt only. A retry re-reads and refuses again on
                        // the same facts, and one logical write that printed this three times would
                        // read as three separate refusals. This line is the whole operator-facing
                        // surface for a refused binding, so how many times it appears is part of
                        // what it says.
                        if (attempt == 1)
                        {
                            _logger?.LogWarning(
                                "Refusing to rebind cached author ASIN {AuthorAsin} in region {Region}: it is already "
                                + "associated with {ExistingAuthor}, and this write names {IncomingAuthor}. "
                                + "Both rows are kept.",
                                normalizedAsin,
                                normalizedRegion,
                                existing.AuthorName,
                                authorCacheEntry.AuthorName);
                        }

                        existing = null;
                    }
                }

                if (existing == null && !string.IsNullOrWhiteSpace(normalizedName))
                {
                    existing = await _db.AuthorCacheEntries.FirstOrDefaultAsync(entry =>
                        entry.AuthorNameNormalized == normalizedName &&
                        entry.Region == normalizedRegion);
                }

                var now = DateTime.UtcNow;
                var inserting = existing == null;
                if (existing == null)
                {
                    existing = new AuthorCacheEntry
                    {
                        CreatedAt = now
                    };

                    _db.AuthorCacheEntries.Add(existing);
                }

                existing.AuthorName = string.IsNullOrWhiteSpace(authorCacheEntry.AuthorName)
                    ? (string.IsNullOrWhiteSpace(existing.AuthorName) ? normalizedName : existing.AuthorName)
                    : authorCacheEntry.AuthorName.Trim();
                existing.AuthorNameNormalized = string.IsNullOrWhiteSpace(normalizedName)
                    ? StringUtils.NormalizeAuthorName(existing.AuthorName)
                    : normalizedName;
                existing.AuthorAsin = string.IsNullOrWhiteSpace(normalizedAsin)
                    ? existing.AuthorAsin
                    : normalizedAsin;
                existing.Region = normalizedRegion;
                existing.ImageUrl = authorCacheEntry.ImageUrl ?? existing.ImageUrl;
                existing.Description = authorCacheEntry.Description ?? existing.Description;

                if (authorCacheEntry.SimilarAuthors != null)
                {
                    existing.SimilarAuthors = authorCacheEntry.SimilarAuthors;
                }

                if (authorCacheEntry.CatalogBooks != null)
                {
                    existing.CatalogBooks = authorCacheEntry.CatalogBooks;
                }

                existing.LastFetchedAt = authorCacheEntry.LastFetchedAt ?? existing.LastFetchedAt ?? now;
                existing.UpdatedAt = now;

                try
                {
                    await _db.SaveChangesAsync();
                    return existing;
                }
                // Only an insert that lost a race is retried, and only when the key it wrote can
                // be looked up again. A violation on the update path means the row resolved by
                // ASIN is being re-keyed onto a name some other row already owns. Re-reading
                // resolves the same row every pass, so retrying would spin and it surfaces
                // instead.
                //
                // Note which route gets there. On this build it is NOT the ASIN naming somebody
                // else: the refusal above turns that into a miss before any write happens, so the
                // case the canary-era version of this comment described cannot occur here. What
                // remains is a row whose stored key an earlier normalizer wrote, matched on its
                // re-derived display name by StringUtils.MatchesAuthorKey and then re-keyed to
                // what the current normalizer produces. If a second row already holds that key,
                // the update collides.
                catch (UniqueConstraintViolationException) when (
                    inserting
                    && !string.IsNullOrWhiteSpace(normalizedName)
                    && attempt < CacheUpsertAttempts)
                {
                    _db.Entry(existing).State = EntityState.Detached;
                }
            }
        }

        /// <inheritdoc />
        public async Task<List<AuthorCacheEntry>> GetAuthorCacheEntriesDueForIdentityCheckAsync(
            DateTime checkedBefore,
            int limit,
            CancellationToken ct = default)
        {
            if (limit <= 0)
            {
                return new List<AuthorCacheEntry>();
            }

            // Nulls first, then oldest. SQLite sorts NULL before any value on an ascending
            // order, which is the order wanted here, but it is ordered explicitly rather than
            // relying on that: a provider that sorted them last would silently make a library
            // that has never run the pass look entirely up to date.
            return await _db.AuthorCacheEntries
                .AsNoTracking()
                .Where(entry => entry.AuthorAsin != null && entry.AuthorAsin != string.Empty)
                .Where(entry => entry.AuthorIdentityCheckedAt == null
                    || entry.AuthorIdentityCheckedAt < checkedBefore)
                .OrderBy(entry => entry.AuthorIdentityCheckedAt.HasValue)
                .ThenBy(entry => entry.AuthorIdentityCheckedAt)
                .ThenBy(entry => entry.Id)
                .Take(limit)
                .ToListAsync(ct);
        }

        /// <inheritdoc />
        public async Task<bool> ApplyAuthorCacheIdentityAsync(
            int id,
            string? authorAsin,
            string? description,
            string? imageUrl,
            DateTime checkedAt,
            CancellationToken ct = default)
        {
            var existing = await _db.AuthorCacheEntries.FirstOrDefaultAsync(entry => entry.Id == id, ct);
            if (existing == null)
            {
                return false;
            }

            // Null rather than the empty string NormalizeAsin answers with, because "this author
            // has no identifier" is the commonest correct outcome here and the column is nullable
            // precisely so it can say so. An empty string is a third state every reader would
            // then have to know about.
            var normalized = NormalizeAsin(authorAsin);
            existing.AuthorAsin = string.IsNullOrWhiteSpace(normalized) ? null : normalized;
            existing.Description = description;
            existing.ImageUrl = imageUrl;
            existing.AuthorIdentityCheckedAt = checkedAt;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        /// <inheritdoc />
        public async Task<bool> StampAuthorCacheIdentityCheckedAsync(
            int id,
            DateTime checkedAt,
            CancellationToken ct = default)
        {
            var existing = await _db.AuthorCacheEntries.FirstOrDefaultAsync(entry => entry.Id == id, ct);
            if (existing == null)
            {
                return false;
            }

            // The cursor moves and nothing else does, UpdatedAt included. A row that was already
            // right must come out of the pass byte for byte what it went in as, apart from the
            // stamp saying it was looked at, or "the repair left the correct rows alone" is not
            // a claim anybody can check.
            existing.AuthorIdentityCheckedAt = checkedAt;
            await _db.SaveChangesAsync(ct);
            return true;
        }
    }
}
