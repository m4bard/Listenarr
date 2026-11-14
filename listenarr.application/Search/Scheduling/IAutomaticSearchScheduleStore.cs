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

    /// <summary>
    /// Records one indexer's outcome for a book, creating the pair row when it is missing. The
    /// next attempt and counts follow <see cref="AutomaticSearchSchedulePolicy"/>. Once started, the
    /// write is not cancellable, so an outcome already in hand is kept when the cycle is cancelled
    /// while it is written; a <paramref name="ct"/> already cancelled before the call throws
    /// <see cref="OperationCanceledException"/> and writes nothing.
    /// </summary>
    Task PersistPairAsync(
        PairScheduleUpdate update,
        DateTime nowUtc,
        TimeSpan cooldown,
        CancellationToken ct = default);

    /// <summary>
    /// Records a book's outcome in one transaction: its due time, its book failure count and, when
    /// a pair completed, its last search time. For outcomes that wrote pair rows the due time is the
    /// earliest next attempt over the indexers enabled for automatic search, an indexer with no row
    /// counting as <paramref name="nowUtc"/>. Not cancellable once started, as
    /// <see cref="PersistPairAsync"/>.
    /// </summary>
    Task PersistBookAsync(
        BookScheduleUpdate update,
        DateTime nowUtc,
        TimeSpan cooldown,
        CancellationToken ct = default);
}
