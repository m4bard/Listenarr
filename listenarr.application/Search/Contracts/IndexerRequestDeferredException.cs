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
    /// Carries a request deferral out of a fetcher that returns bytes rather than an
    /// <see cref="IndexerRequestResult"/>.
    /// </summary>
    public sealed class IndexerRequestDeferredException : Exception
    {
        /// <summary>
        /// Creates the exception for a request the gate did not send.
        /// </summary>
        /// <param name="deferral">The deferral that kept the request from being sent.</param>
        public IndexerRequestDeferredException(RequestDeferral deferral)
            : base($"Indexer request deferred until {deferral.UntilUtc:O} ({deferral.Reason}).")
        {
            Deferral = deferral;
        }

        /// <summary>
        /// The deferral that kept the request from being sent.
        /// </summary>
        public RequestDeferral Deferral { get; }
    }
}
