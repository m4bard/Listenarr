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
    /// What the gate did with one request: sent it, or deferred it.
    /// </summary>
    public abstract record IndexerRequestResult
    {
        /// <summary>
        /// The request went out on the wire. The caller owns and disposes the response.
        /// </summary>
        /// <param name="Response">The indexer's response.</param>
        public sealed record Sent(HttpResponseMessage Response) : IndexerRequestResult;

        /// <summary>
        /// The request was not sent.
        /// </summary>
        /// <param name="Deferral">Why it was held back and until when.</param>
        public sealed record Deferred(RequestDeferral Deferral) : IndexerRequestResult;
    }
}
