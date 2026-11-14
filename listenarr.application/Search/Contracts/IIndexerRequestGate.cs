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
    /// Sends one indexer request, or defers it. Each call is exactly one request on the wire, so a
    /// per-cycle request bound counts real requests.
    /// </summary>
    public interface IIndexerRequestGate
    {
        /// <summary>
        /// Sends one request, or defers it. <paramref name="dispatch"/> performs exactly one
        /// request on the wire and is called at most once. Transport exceptions propagate after
        /// the gate has recorded them as failures. An <see cref="OperationCanceledException"/>
        /// propagates; the gate records nothing only when
        /// <paramref name="cancellationToken"/> is cancelled, otherwise it records a transport
        /// failure.
        /// </summary>
        /// <param name="context">The indexer, origin, kind and optional cycle allowance.</param>
        /// <param name="dispatch">Sends the request and returns its response.</param>
        /// <param name="cancellationToken">The caller's own token.</param>
        /// <returns>The response, or the deferral that kept the request from being sent.</returns>
        Task<IndexerRequestResult> SendAsync(
            IndexerRequestContext context,
            Func<CancellationToken, Task<HttpResponseMessage>> dispatch,
            CancellationToken cancellationToken);

        /// <summary>
        /// Would a request for this indexer and origin be deferred right now? Never charges,
        /// never waits.
        /// </summary>
        /// <param name="indexerId">The indexer to check.</param>
        /// <param name="origin">Who would cause the request.</param>
        /// <param name="cancellationToken">The caller's own token.</param>
        /// <returns>The deferral that would apply, or null when a request would be sent.</returns>
        Task<RequestDeferral?> GetDeferralAsync(
            int indexerId,
            SearchOrigin origin,
            CancellationToken cancellationToken);
    }
}
