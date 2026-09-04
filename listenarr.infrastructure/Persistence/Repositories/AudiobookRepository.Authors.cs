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
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories
{
    // Author identity and author catalog cache reads/writes.
    public partial class AudiobookRepository
    {
        public async Task<string?> GetAuthorAsinByNameAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            var target = NormalizeAuthorName(name);
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
                // author's identifier to every co-author credited on the same book.
                var bookAuthors = b.Authors
                    .Select(NormalizeAuthorName)
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
            var normalizedName = NormalizeAuthorName(name);
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

            var normalizedName = NormalizeAuthorName(authorCacheEntry.AuthorName);
            var normalizedRegion = AudiobookIdentifierNormalizer.NormalizeRegion(authorCacheEntry.Region) ?? "us";
            var normalizedAsin = NormalizeAsin(authorCacheEntry.AuthorAsin);

            AuthorCacheEntry? existing = null;

            if (!string.IsNullOrWhiteSpace(normalizedAsin))
            {
                existing = await _db.AuthorCacheEntries.FirstOrDefaultAsync(entry =>
                    entry.AuthorAsin != null &&
                    entry.AuthorAsin.ToUpper() == normalizedAsin &&
                    entry.Region == normalizedRegion);
            }

            if (existing == null && !string.IsNullOrWhiteSpace(normalizedName))
            {
                existing = await _db.AuthorCacheEntries.FirstOrDefaultAsync(entry =>
                    entry.AuthorNameNormalized == normalizedName &&
                    entry.Region == normalizedRegion);
            }

            var now = DateTime.UtcNow;
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
                ? NormalizeAuthorName(existing.AuthorName)
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

            await _db.SaveChangesAsync();
            return existing;
        }
        private static string NormalizeAuthorName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var cleaned = new string(value
                .Where(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))
                .ToArray());
            var parts = cleaned.Split(
                new[] { ' ', '\t', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries);

            return string.Join(' ', parts).ToLowerInvariant();
        }
    }
}
