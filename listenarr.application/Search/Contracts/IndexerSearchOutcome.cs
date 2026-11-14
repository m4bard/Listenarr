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
    /// How one indexer's search for one query ended.
    /// </summary>
    public enum IndexerSearchStatus
    {
        /// <summary>
        /// The indexer answered its search requests. Failed enrichment requests are skipped. The results
        /// may be empty.
        /// </summary>
        Completed,

        /// <summary>
        /// A search, fallback or redirect request failed (including a timeout while the caller's token
        /// was not cancelled), or failed enrichment left no candidate results.
        /// </summary>
        Failed,

        /// <summary>
        /// The gate held a request back. The results hold whatever was gathered before it.
        /// </summary>
        Deferred,

        /// <summary>
        /// The caller's own token was cancelled.
        /// </summary>
        Cancelled
    }

    /// <summary>
    /// The context one indexer search runs in.
    /// </summary>
    /// <param name="Origin">Who caused the search.</param>
    /// <param name="CycleAllowance">The worker cycle's request allowance, or null when the caller
    /// has no per-cycle bound.</param>
    /// <param name="QueryKind">The kind the provider uses for its primary send:
    /// <see cref="IndexerRequestKind.Search"/>, or <see cref="IndexerRequestKind.FallbackQuery"/>
    /// for the title-only fallback.</param>
    public sealed record IndexerSearchContext(
        SearchOrigin Origin,
        CycleRequestAllowance? CycleAllowance,
        IndexerRequestKind QueryKind = IndexerRequestKind.Search);

    /// <summary>
    /// The typed outcome of one indexer's search. An empty result list never stands for failed,
    /// deferred or cancelled; <see cref="Status"/> says which.
    /// </summary>
    /// <param name="IndexerId">The indexer that was searched.</param>
    /// <param name="Status">How the search ended.</param>
    /// <param name="Results">Whatever was gathered before the search stopped.</param>
    /// <param name="RequestsSent">How many requests went out on the wire.</param>
    /// <param name="Deferral">The deferral, when <see cref="Status"/> is Deferred.</param>
    /// <param name="FailureReason">A short reason, when <see cref="Status"/> is Failed.</param>
    public sealed record IndexerSearchOutcome(
        int IndexerId,
        IndexerSearchStatus Status,
        IReadOnlyList<IndexerSearchResult> Results,
        int RequestsSent,
        RequestDeferral? Deferral = null,
        string? FailureReason = null);

    /// <summary>
    /// One outcome per indexer searched.
    /// </summary>
    /// <param name="Outcomes">The per-indexer outcomes.</param>
    public sealed record IndexerSearchReport(IReadOnlyList<IndexerSearchOutcome> Outcomes);
}
