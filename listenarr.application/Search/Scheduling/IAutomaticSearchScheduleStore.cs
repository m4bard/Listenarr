namespace Listenarr.Application.Search.Scheduling;

/// <summary>
/// Reads the automatic search's durable schedule: which books are due, and how each of their
/// indexers last went. Selection happens in SQL, so a cycle reads at most one page of books.
/// </summary>
public interface IAutomaticSearchScheduleStore
{
    /// <summary>
    /// Monitored books with a quality profile whose due time is at or before
    /// <paramref name="nowUtc"/>, oldest due first and then by id, with their quality profile
    /// loaded. Books in <paramref name="excludedIds"/> (already processed this cycle) are
    /// skipped. Returns at most <paramref name="pageSize"/> books.
    /// </summary>
    Task<IReadOnlyList<Audiobook>> GetDuePageAsync(
        DateTime nowUtc,
        IReadOnlyCollection<int> excludedIds,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// The per-indexer search state rows of the given books. A book and indexer with no row has
    /// never been searched there and is due on it.
    /// </summary>
    Task<IReadOnlyList<AudiobookIndexerSearchState>> GetPairStatesAsync(
        IReadOnlyCollection<int> audiobookIds,
        CancellationToken ct = default);
}
