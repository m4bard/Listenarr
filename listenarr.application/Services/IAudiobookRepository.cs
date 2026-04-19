// csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Listenarr.Domain.Models;

namespace Listenarr.Api.Services
{
    // IAudiobookRepository moved into the Application project (physical location changed).
    // Kept the original namespace to minimise churn across the codebase.
    public interface IAudiobookRepository
    {
        Task<List<Audiobook>> GetAllAsync();
        Task<List<Audiobook>> GetByIdsWithFilesAsync(IEnumerable<int> ids, CancellationToken ct = default);
        Task<List<Audiobook>> GetMonitoredAudiobooksForSearchAsync(DateTime cutoff, CancellationToken ct = default);
        Task NormalizeJsonColumnsAsync(CancellationToken ct = default);
        Task<Audiobook?> GetByAsinAsync(string asin);
        Task<Audiobook?> GetByIsbnAsync(string isbn);
        Task<Audiobook?> GetByIdAsync(int id);
        Task<string?> GetAuthorAsinByNameAsync(string name);
        Task<AuthorCacheEntry?> GetCachedAuthorByNameAsync(string name, string region);
        Task<AuthorCacheEntry?> GetCachedAuthorByAsinAsync(string asin, string region);
        Task<AuthorCacheEntry> UpsertCachedAuthorAsync(AuthorCacheEntry authorCacheEntry);
        Task<SeriesCacheEntry?> GetCachedSeriesByNameAsync(string name, string region);
        Task<SeriesCacheEntry?> GetCachedSeriesByAsinAsync(string asin, string region);
        Task<SeriesCacheEntry> UpsertCachedSeriesAsync(SeriesCacheEntry seriesCacheEntry);
        Task AddAsync(Audiobook audiobook);
        Task<bool> UpdateAsync(Audiobook audiobook);
        Task<bool> DeleteAsync(Audiobook audiobook);
        Task<bool> DeleteByIdAsync(int id);
        Task<int> DeleteBulkAsync(List<int> ids);
        Task SaveChangesAsync(CancellationToken ct = default);
        Task<bool> UpdateWithIdentifierReplaceAsync(Audiobook audiobook, List<AudiobookExternalIdentifier> newIdentifiers, CancellationToken ct = default);
    }
}
