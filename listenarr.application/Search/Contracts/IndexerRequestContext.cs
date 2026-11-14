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
    /// What one indexer request is for. Every request on the wire carries exactly one kind.
    /// </summary>
    public enum IndexerRequestKind
    {
        /// <summary>
        /// The primary query to an indexer, including each Internet Archive query variant.
        /// </summary>
        Search,

        /// <summary>
        /// The title-only fallback query, sent to an indexer whose primary query completed with
        /// no results.
        /// </summary>
        FallbackQuery,

        /// <summary>
        /// A per-item request that adds detail to results already found, such as item metadata,
        /// a detail page or a comments page.
        /// </summary>
        Enrichment,

        /// <summary>
        /// A redirect hop that the application follows itself, after the first send.
        /// </summary>
        Redirect,

        /// <summary>
        /// Any wire request on the grab path: a torrent fetch, an NZB fetch or MyAnonamouse
        /// torrent preparation, each hop and each retry included.
        /// </summary>
        DownloadFetch,

        /// <summary>
        /// A request that tests an indexer's connection or credentials.
        /// </summary>
        ConnectionTest
    }

    /// <summary>
    /// Identifies one indexer request to the gate.
    /// </summary>
    /// <param name="IndexerId">The indexer the request goes to; the key for per-indexer state.
    /// Values of 0 or less mean the request is unattributed: only the global cap and the cycle
    /// allowance apply.</param>
    /// <param name="Origin">Who caused the request.</param>
    /// <param name="Kind">What the request is for.</param>
    /// <param name="CycleAllowance">The worker cycle's request allowance, or null when the caller
    /// has no per-cycle bound (interactive callers).</param>
    public sealed record IndexerRequestContext(
        int IndexerId,
        SearchOrigin Origin,
        IndexerRequestKind Kind,
        CycleRequestAllowance? CycleAllowance = null);
}
