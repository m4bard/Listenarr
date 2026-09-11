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

namespace Listenarr.Application.Audiobooks.Contracts.Repositories
{
    public sealed record AudiobookPathReferenceSnapshot(
        int AudiobookId,
        string? BasePath,
        string? FilePath);

    public sealed record MetadataRefreshCandidate(
        int AudiobookId,
        string? PrimaryAuthor,
        DateTime? LastMetadataRefreshAt);

    public interface IAudiobookRepository
    {
        Task<List<Audiobook>> GetAllAsync();
        Task<AudiobookPathReferenceSnapshot?> GetPathReferenceSnapshotAsync(
            int audiobookId,
            CancellationToken ct = default);
        Task<List<AudiobookPathReferenceSnapshot>> GetOtherPathReferenceSnapshotsAsync(
            int audiobookId,
            CancellationToken ct = default);
        Task<List<MetadataRefreshCandidate>> GetAudiobooksDueForMetadataRefreshAsync(
            DateTime staleBefore,
            int limit,
            CancellationToken ct = default);
        Task<List<int>> GetAudiobookIdsByAuthorNameAsync(
            string authorName,
            CancellationToken ct = default);

        /// <summary>
        /// The staleness predicate of the due query, restricted to the given ids and answered in
        /// SQL. An author-scoped run filters its own books with this instead of pulling the whole
        /// library's due set back to intersect it. Ordered by id; an empty input asks nothing.
        /// </summary>
        Task<List<int>> FilterAudiobookIdsDueForMetadataRefreshAsync(
            IReadOnlyCollection<int> audiobookIds,
            DateTime staleBefore,
            CancellationToken ct = default);

        Task<bool> StampMetadataRefreshAsync(
            int audiobookId,
            DateTime refreshedAtUtc,
            CancellationToken ct = default);

        /// <summary>
        /// Gives every row that has never been refreshed the time of the backfill, and returns
        /// how many rows it wrote.
        /// </summary>
        /// <remarks>
        /// Idempotent, and a no-op on every start after the first: it only touches rows whose
        /// timestamp is null. It exists because the refresh ships on, and a null timestamp reads
        /// as "never refreshed", so without this every book in an upgraded library would be due
        /// on the first cycle after the upgrade. Stamping the present buys the upgraded library
        /// one full staleness window before any of it comes due.
        /// </remarks>
        Task<int> BackfillMetadataRefreshTimestampsAsync(CancellationToken ct = default);
        Task<List<Audiobook>> GetLibraryAsync();
        Task<Dictionary<int, List<AudiobookSeriesMembership>>> GetAllSeriesMembershipsGroupedByAudiobookIdAsync(CancellationToken ct = default);
        Task<List<Audiobook>> GetByIdsWithFilesAsync(IEnumerable<int> ids, CancellationToken ct = default);
        Task<List<Audiobook>> GetMonitoredAudiobooksForSearchAsync(DateTime cutoff, CancellationToken ct = default);
        Task NormalizeJsonColumnsAsync(CancellationToken ct = default);
        Task<Audiobook?> GetByAsinAsync(string asin);
        Task<Audiobook?> GetByIsbnAsync(string isbn);
        Task<Audiobook?> GetByIdAsync(int id);
        Task<Audiobook?> GetByIdSnapshotAsync(int id, CancellationToken ct = default);
        Task<Audiobook?> GetForUpdateSnapshotAsync(int id, CancellationToken ct = default);
        Task<Audiobook?> GetForScanAsync(int id, CancellationToken ct = default);
        Task<Audiobook?> GetForScanSnapshotAsync(int id, CancellationToken ct = default);
        Task<bool> TryUpdateBasePathAsync(
            int audiobookId,
            string expectedBasePath,
            string newBasePath,
            CancellationToken ct = default);
        Task<bool> TryUpdateImageUrlAsync(
            int audiobookId,
            string? expectedImageUrl,
            string? newImageUrl,
            CancellationToken ct = default) => Task.FromResult(false);
        Task<string?> GetAuthorAsinByNameAsync(string name);
        Task<AuthorCacheEntry?> GetCachedAuthorByNameAsync(string name, string region);
        Task<AuthorCacheEntry?> GetCachedAuthorByAsinAsync(string asin, string region);
        Task<AuthorCacheEntry> UpsertCachedAuthorAsync(AuthorCacheEntry authorCacheEntry);
        Task<SeriesCacheEntry?> GetCachedSeriesByNameAsync(string name, string region);
        Task<SeriesCacheEntry?> GetCachedSeriesByAsinAsync(string asin, string region);
        Task<SeriesCacheEntry> UpsertCachedSeriesAsync(SeriesCacheEntry seriesCacheEntry);
        Task<Audiobook> AddAsync(Audiobook audiobook);
        Task<bool> UpdateAsync(Audiobook audiobook);
        Task<bool> RewritePathReferencesAsync(
            int audiobookId,
            string? sourceBasePath,
            string targetBasePath,
            FileSystemPathSemantics sourceSemantics,
            FileSystemPathSemantics targetSemantics,
            CancellationToken ct = default,
            FileSystemCaseSensitivityMode targetCaseSensitivityMode = FileSystemCaseSensitivityMode.Auto);
        Task<bool> RewriteMovedPathReferencesAsync(
            int audiobookId,
            string? sourceBasePath,
            string targetBasePath,
            FileSystemPathSemantics sourceSemantics,
            FileSystemPathSemantics targetSemantics,
            IReadOnlyDictionary<string, string> targetPhysicalObjectIdentities,
            DateTime targetPhysicalIdentityObservedAtUtc,
            CancellationToken ct = default,
            FileSystemCaseSensitivityMode targetCaseSensitivityMode = FileSystemCaseSensitivityMode.Auto);
        Task<bool> DeleteByIdAsync(int id);
        Task SaveChangesAsync(CancellationToken ct = default);
        Task<bool> UpdateWithIdentifierReplaceAsync(Audiobook audiobook, List<AudiobookExternalIdentifier> newIdentifiers, CancellationToken ct = default);
    }
}
