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

namespace Listenarr.Application.Search.Contracts
{
    /// <summary>
    /// A budget the gate consults before dispatch. The gate takes every registered reservation
    /// and ships with none. Neither method is ever called for an unattributed request (indexer
    /// id 0 or less).
    /// </summary>
    public interface IIndexerRequestReservation
    {
        /// <summary>
        /// Atomically checks and charges one request. Called by the gate after the slot is held
        /// and the cooldown re-check passed, immediately before dispatch. A charge is not
        /// refunded if the request is then cancelled or fails: the ledger may over-count and
        /// must never under-count.
        /// </summary>
        /// <param name="context">The request about to be sent, kind included.</param>
        /// <param name="cancellationToken">The caller's own token.</param>
        /// <returns>Null when the request is admitted or there is nothing to charge, otherwise
        /// the deferral that keeps it from being sent.</returns>
        Task<RequestDeferral?> TryReserveAsync(
            IndexerRequestContext context,
            CancellationToken cancellationToken);

        /// <summary>
        /// Would a request for this indexer and origin be deferred right now? Never charges and
        /// never waits. It carries no <see cref="IndexerRequestKind"/>, so it is treated as a
        /// search request.
        /// </summary>
        /// <param name="indexerId">The indexer to check.</param>
        /// <param name="origin">Who would cause the request.</param>
        /// <param name="cancellationToken">The caller's own token.</param>
        /// <returns>The deferral that would apply, or null. The default admits everything.</returns>
        Task<RequestDeferral?> PeekAsync(
            int indexerId,
            SearchOrigin origin,
            CancellationToken cancellationToken) => Task.FromResult<RequestDeferral?>(null);
    }
}
