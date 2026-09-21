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

using Listenarr.Application.Calendar;
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
    /// <summary>
    /// Counts from one re-derivation pass over the normalized author-name columns.
    /// Skipped rows are those whose re-derived key is already held by another row in the same
    /// uniqueness slot; they keep the key they have rather than failing the pass.
    /// </summary>
    /// <summary>One book whose stored author credits name a role.</summary>
    public sealed record StoredAuthorCreditChange(
        int AudiobookId,
        IReadOnlyList<string> Before,
        IReadOnlyList<string> After);

    /// <summary>What one credit cleanup pass did, or would have done.</summary>
    public sealed record StoredAuthorCreditCleanupResult(
        int Examined,
        IReadOnlyList<StoredAuthorCreditChange> Changes)
    {
        public static StoredAuthorCreditCleanupResult Nothing { get; } =
            new(0, Array.Empty<StoredAuthorCreditChange>());
    }

    public sealed record AuthorNameKeyRederivationResult(
        int AuthorCacheEntriesCorrected,
        int MonitoredAuthorsCorrected,
        int Skipped);

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

        /// <summary>
        /// Projects the audiobooks whose stored PublishedDate falls inside a coarse string range,
        /// for the calendar window query.
        /// </summary>
        /// <remarks>
        /// PublishedDate is a TEXT column holding inconsistently formatted ISO 8601 values, so the
        /// bounds here are deliberately coarse (see <see cref="CalendarWindow"/>) and the caller
        /// narrows to the exact day range after parsing.
        /// </remarks>
        /// <param name="coarseLowerBound">Inclusive lower bound for the string comparison.</param>
        /// <param name="coarseUpperBound">Inclusive upper bound for the string comparison.</param>
        /// <param name="includeUnmonitored">
        /// When false, unmonitored audiobooks are excluded in SQL, matching the *arr calendars.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        Task<List<CalendarAudiobookRow>> GetCalendarRowsAsync(
            string coarseLowerBound,
            string coarseUpperBound,
            bool includeUnmonitored,
            CancellationToken ct = default);
        Task NormalizeJsonColumnsAsync(CancellationToken ct = default);
        Task<AuthorNameKeyRederivationResult> RederiveAuthorNameKeysAsync(CancellationToken ct = default);
        Task<int> CanonicalizeStoredAuthorNamesAsync(CancellationToken ct = default);
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

        /// <summary>
        /// Cached author rows carrying an ASIN, least recently identity-checked first, never
        /// examined ahead of everything else. The bound is the caller's per-run ceiling.
        /// </summary>
        /// <remarks>
        /// Only rows with an ASIN are returned, because a row with none cannot be holding a
        /// stranger's. The ordering is the whole resumption story: a run stamps what it examined,
        /// so the next run starts where it stopped rather than at the beginning, and an install
        /// that has never run the pass is one long queue of nulls.
        /// </remarks>
        Task<List<AuthorCacheEntry>> GetAuthorCacheEntriesDueForIdentityCheckAsync(
            int limit,
            CancellationToken ct = default);

        /// <summary>
        /// Writes one identity decision onto a cached author row: the ASIN, and the biography and
        /// portrait that came with it, plus the cursor. Returns false when the row is gone.
        /// </summary>
        /// <remarks>
        /// Deliberately not <see cref="UpsertCachedAuthorAsync"/>, for two reasons that both
        /// matter. That path merges and never clears, so it cannot express "this ASIN belongs to
        /// somebody else, so this author has none", which is the commonest correct outcome here.
        /// And its ASIN-collision guard refuses to move an ASIN to a different name, which is
        /// exactly what a repair sometimes has to do -- the guard is right for an ingestion write
        /// and wrong for a correction.
        ///
        /// It writes identity and the cursor and nothing else. A row's catalogue, its similar
        /// authors, its name and its cache timestamps are somebody else's business, and a repair
        /// that touched them would be indistinguishable from a cache refresh.
        /// </remarks>
        Task<bool> ApplyAuthorCacheIdentityAsync(
            int id,
            string? authorAsin,
            string? description,
            string? imageUrl,
            DateTime checkedAt,
            CancellationToken ct = default);

        /// <summary>
        /// Stamps a cached author row as identity-checked without changing anything on it, for a
        /// row a run examined and found correct.
        /// </summary>
        Task<bool> StampAuthorCacheIdentityCheckedAsync(
            int id,
            DateTime checkedAt,
            CancellationToken ct = default);

        /// <summary>
        /// Removes contributor roles from the author credits already stored on books, up to
        /// <paramref name="limit"/> books whose stored list disagrees with the rule.
        /// </summary>
        /// <remarks>
        /// The credit rule cleans what arrives; rows written before it landed keep their roles
        /// until the book happens to be refreshed, and on an install that has been running a
        /// while that is most of them. This is the same rule applied to what is already there.
        ///
        /// <paramref name="apply"/> false examines and reports and writes nothing, which is what
        /// the repair pass's preview calls. Preview and write are one method on purpose: two
        /// would be two chances for what is reported and what is done to drift apart.
        ///
        /// No cursor, and it does not need one. The rule is idempotent and local, so a cleaned
        /// book stops being a candidate and successive runs converge without anything having to
        /// remember where the last one stopped.
        /// </remarks>
        Task<StoredAuthorCreditCleanupResult> CleanRoleSuffixesFromStoredAuthorsAsync(
            int limit,
            bool apply,
            CancellationToken ct = default);
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
