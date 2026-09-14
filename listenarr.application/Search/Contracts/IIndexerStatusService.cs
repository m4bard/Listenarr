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

namespace Listenarr.Application.Search.Contracts;

/// <summary>
/// Per-indexer failure backoff: which indexers are in a cooldown right now, and the recording of
/// each query's outcome that moves them on and off the ladder.
/// </summary>
public interface IIndexerStatusService
{
    /// <summary>
    /// Ids of every indexer whose cooldown has not yet expired. Empty on a healthy install, which
    /// is the case worth keeping cheap.
    /// </summary>
    Task<IReadOnlySet<int>> GetBlockedIndexerIdsAsync(CancellationToken ct = default);

    /// <summary>
    /// Records one query's outcome against one indexer and returns the resulting backoff state.
    /// </summary>
    /// <param name="indexer">
    /// The indexer as it was read for this search. Its backoff columns are the state being moved
    /// from, so no second read is needed on the common path.
    /// </param>
    /// <param name="observation">How the indexer answered.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IndexerBackoffState> RecordAsync(
        Indexer indexer,
        IndexerQueryObservation observation,
        CancellationToken ct = default);
}
