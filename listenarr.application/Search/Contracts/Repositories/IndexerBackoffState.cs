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

namespace Listenarr.Application.Search.Contracts.Repositories;

/// <summary>
/// The five failure-backoff columns of one indexer, on their own.
/// </summary>
/// <remarks>
/// A separate type from <see cref="Indexer"/> on purpose. Writing this state through the ordinary
/// update path would send a whole entity, and the ordinary update path is a whole-entity overwrite
/// (EfIndexerRepository.UpdateAsync, via CurrentValues.SetValues) fed by AsNoTracking reads. A
/// status write built on a stale read would silently revert a settings edit made in between, and an
/// operator saving settings would silently reset a cooldown. Both directions are avoided by only
/// ever writing these columns and only ever writing them all at once.
/// </remarks>
/// <param name="InitialFailure">When the current failure run began; set once, not per failure.</param>
/// <param name="MostRecentFailure">When the indexer most recently failed to answer.</param>
/// <param name="EscalationLevel">Position on the ladder; 0 is healthy.</param>
/// <param name="DisabledTill">When the current cooldown expires; null when not blocked.</param>
/// <param name="LastFailureReason">Name of the query reason behind the current block.</param>
public sealed record IndexerBackoffState(
    DateTime? InitialFailure,
    DateTime? MostRecentFailure,
    int EscalationLevel,
    DateTime? DisabledTill,
    string? LastFailureReason)
{
    /// <summary>An indexer that has never failed, or has walked all the way back down.</summary>
    public static readonly IndexerBackoffState Healthy = new(null, null, 0, null, null);

    /// <summary>Reads the current state off an indexer entity.</summary>
    public static IndexerBackoffState From(Indexer indexer)
    {
        ArgumentNullException.ThrowIfNull(indexer);
        return new IndexerBackoffState(
            indexer.InitialFailure,
            indexer.MostRecentFailure,
            indexer.EscalationLevel,
            indexer.DisabledTill,
            indexer.LastFailureReason);
    }

    /// <summary>Whether the cooldown is still running at <paramref name="asOf"/>.</summary>
    public bool IsBlockedAt(DateTime asOf) => DisabledTill is { } till && till > asOf;
}
