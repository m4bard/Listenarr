// csharp
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;

namespace Listenarr.Api.Services
{
    /// <summary>
    /// Moved AudiobookRepository implementation into the Infrastructure project.
    /// Keeps the original namespace Listenarr.Api.Services so existing code and DI registrations
    /// don't need to change.
    /// </summary>
    public class AudiobookRepository : IAudiobookRepository
    {
        private readonly ListenArrDbContext _db;
        public AudiobookRepository(ListenArrDbContext db)
        {
            _db = db;
        }

        public async Task<List<Audiobook>> GetAllAsync()
        {
            // Include Files so callers that fetch the full library will receive file records
            return await _db.Audiobooks
                .Include(a => a.Files)
                .OrderBy(a => a.Title)
                .ToListAsync();
        }

        public async Task<Audiobook?> GetByAsinAsync(string asin)
        {
            var normalizedAsin = NormalizeAsin(asin);
            if (string.IsNullOrWhiteSpace(normalizedAsin)) return null;

            return await _db.Audiobooks
                .Include(a => a.ExternalIdentifiers)
                .FirstOrDefaultAsync(a =>
                    (a.Asin != null && a.Asin.ToUpper() == normalizedAsin) ||
                    (a.ExternalIdentifiers != null && a.ExternalIdentifiers.Any(i =>
                        i.Type == AudiobookExternalIdentifierType.Asin &&
                        i.ValueNormalized == normalizedAsin)));
        }

        public async Task<Audiobook?> GetByIsbnAsync(string isbn)
        {
            var normalizedIsbn = NormalizeIsbn(isbn);
            if (string.IsNullOrWhiteSpace(normalizedIsbn)) return null;

            var audiobooks = await _db.Audiobooks
                .Include(a => a.ExternalIdentifiers)
                .ToListAsync();

            return audiobooks.FirstOrDefault(a =>
                (a.Isbn != null && a.Isbn.Any(i => NormalizeIsbn(i) == normalizedIsbn)) ||
                (a.ExternalIdentifiers != null && a.ExternalIdentifiers.Any(i =>
                    i.Type == AudiobookExternalIdentifierType.Isbn &&
                    string.Equals(i.ValueNormalized, normalizedIsbn, StringComparison.OrdinalIgnoreCase))));
        }

        public async Task<Audiobook?> GetByIdAsync(int id)
        {
            // Include QualityProfile and Files for callers that need full audiobook details
            return await _db.Audiobooks
                .Include(a => a.QualityProfile)
                .Include(a => a.Files)
                .Include(a => a.ExternalIdentifiers)
                .Include(a => a.SeriesMemberships)
                .FirstOrDefaultAsync(a => a.Id == id);
        }

        public async Task AddAsync(Audiobook audiobook)
        {
            _db.Audiobooks.Add(audiobook);
            await _db.SaveChangesAsync();
        }

        public async Task<bool> UpdateAsync(Audiobook audiobook)
        {
            // Defensive: preserve existing BasePath if the incoming audiobook doesn't provide one
            try
            {
                var existing = await _db.Audiobooks.AsNoTracking().FirstOrDefaultAsync(a => a.Id == audiobook.Id);
                if (existing != null && string.IsNullOrEmpty(audiobook.BasePath) && !string.IsNullOrEmpty(existing.BasePath))
                {
                    audiobook.BasePath = existing.BasePath;
                }
            }
            catch (Exception caughtEx_1) when (caughtEx_1 is not OperationCanceledException && caughtEx_1 is not OutOfMemoryException && caughtEx_1 is not StackOverflowException) {
                // If anything goes wrong reading existing record, fall back to update behavior
                            System.Diagnostics.Debug.WriteLine("Suppressed non-fatal exception in catch block.");
            }

            _db.Audiobooks.Update(audiobook);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(Audiobook audiobook)
        {
            _db.Audiobooks.Remove(audiobook);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteByIdAsync(int id)
        {
            var audiobook = await GetByIdAsync(id);
            if (audiobook == null)
                return false;

            return await DeleteAsync(audiobook);
        }

        public async Task<int> DeleteBulkAsync(List<int> ids)
        {
            var audiobooks = await _db.Audiobooks
                .Where(a => ids.Contains(a.Id))
                .ToListAsync();

            if (!audiobooks.Any())
                return 0;

            _db.Audiobooks.RemoveRange(audiobooks);
            await _db.SaveChangesAsync();
            return audiobooks.Count;
        }

        public async Task<string?> GetAuthorAsinByNameAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            var target = NormalizeAuthorName(name);

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

                foreach (var author in b.Authors ?? new List<string>())
                {
                    if (NormalizeAuthorName(author) == target)
                    {
                        var asin = b.AuthorAsins?.FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(asin)) return asin;
                    }
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

        public async Task<SeriesCacheEntry?> GetCachedSeriesByNameAsync(string name, string region)
        {
            var normalizedName = NormalizeSeriesName(name);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return null;
            }

            var normalizedRegion = AudiobookIdentifierNormalizer.NormalizeRegion(region) ?? "us";

            return await _db.SeriesCacheEntries
                .AsNoTracking()
                .OrderByDescending(entry => entry.LastFetchedAt ?? entry.UpdatedAt)
                .FirstOrDefaultAsync(entry =>
                    entry.SeriesNameNormalized == normalizedName &&
                    entry.Region == normalizedRegion);
        }

        public async Task<SeriesCacheEntry?> GetCachedSeriesByAsinAsync(string asin, string region)
        {
            var normalizedAsin = NormalizeAsin(asin);
            if (string.IsNullOrWhiteSpace(normalizedAsin))
            {
                return null;
            }

            var normalizedRegion = AudiobookIdentifierNormalizer.NormalizeRegion(region) ?? "us";

            return await _db.SeriesCacheEntries
                .AsNoTracking()
                .OrderByDescending(entry => entry.LastFetchedAt ?? entry.UpdatedAt)
                .FirstOrDefaultAsync(entry =>
                    entry.SeriesAsin != null &&
                    entry.SeriesAsin.ToUpper() == normalizedAsin &&
                    entry.Region == normalizedRegion);
        }

        public async Task<SeriesCacheEntry> UpsertCachedSeriesAsync(SeriesCacheEntry seriesCacheEntry)
        {
            ArgumentNullException.ThrowIfNull(seriesCacheEntry);

            var normalizedName = NormalizeSeriesName(seriesCacheEntry.SeriesName);
            var normalizedRegion = AudiobookIdentifierNormalizer.NormalizeRegion(seriesCacheEntry.Region) ?? "us";
            var normalizedAsin = NormalizeAsin(seriesCacheEntry.SeriesAsin);

            SeriesCacheEntry? existing = null;

            if (!string.IsNullOrWhiteSpace(normalizedAsin))
            {
                existing = await _db.SeriesCacheEntries.FirstOrDefaultAsync(entry =>
                    entry.SeriesAsin != null &&
                    entry.SeriesAsin.ToUpper() == normalizedAsin &&
                    entry.Region == normalizedRegion);
            }

            if (existing == null && !string.IsNullOrWhiteSpace(normalizedName))
            {
                existing = await _db.SeriesCacheEntries.FirstOrDefaultAsync(entry =>
                    entry.SeriesNameNormalized == normalizedName &&
                    entry.Region == normalizedRegion);
            }

            var now = DateTime.UtcNow;
            if (existing == null)
            {
                existing = new SeriesCacheEntry
                {
                    CreatedAt = now
                };

                _db.SeriesCacheEntries.Add(existing);
            }

            existing.SeriesName = string.IsNullOrWhiteSpace(seriesCacheEntry.SeriesName)
                ? (string.IsNullOrWhiteSpace(existing.SeriesName) ? normalizedName : existing.SeriesName)
                : seriesCacheEntry.SeriesName.Trim();
            existing.SeriesNameNormalized = string.IsNullOrWhiteSpace(normalizedName)
                ? NormalizeSeriesName(existing.SeriesName)
                : normalizedName;
            existing.SeriesAsin = string.IsNullOrWhiteSpace(normalizedAsin)
                ? existing.SeriesAsin
                : normalizedAsin;
            existing.Region = normalizedRegion;
            existing.ImageUrl = seriesCacheEntry.ImageUrl ?? existing.ImageUrl;
            existing.Description = seriesCacheEntry.Description ?? existing.Description;

            if (seriesCacheEntry.CatalogBooks != null)
            {
                existing.CatalogBooks = seriesCacheEntry.CatalogBooks;
            }

            existing.LastFetchedAt = seriesCacheEntry.LastFetchedAt ?? existing.LastFetchedAt ?? now;
            existing.UpdatedAt = now;

            await _db.SaveChangesAsync();
            return existing;
        }

        private static string NormalizeAsin(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }

        private static string NormalizeIsbn(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
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

        private static string NormalizeSeriesName(string? value)
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
