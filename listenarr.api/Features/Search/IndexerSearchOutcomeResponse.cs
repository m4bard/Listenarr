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

namespace Listenarr.Api.Features.Search
{
    /// <summary>
    /// One indexer's search results together with whether the indexer was searched at all.
    /// Returned by <c>GET /search/{apiId}</c> only when the caller passes <c>includeOutcome=true</c>;
    /// without it the endpoint returns the bare result list it always has.
    /// </summary>
    public sealed class IndexerSearchOutcomeResponse
    {
        /// <summary>The results, in the same shape the endpoint returns without the envelope.</summary>
        public object Results { get; set; } = Array.Empty<object>();

        /// <summary>
        /// True when the indexer answered its search, including with nothing. False when it failed,
        /// the request gate held the search back, or the indexer does not exist: an empty result
        /// list then says nothing about whether the release exists.
        /// </summary>
        public bool Answered { get; set; }

        /// <summary>
        /// Why the indexer was not searched: the deferral reason (eg. <c>RateLimited</c>), a failure
        /// reason (eg. <c>timeout</c>), <c>NotConfigured</c> for an indexer with no provider, or
        /// <c>NotFound</c> for one that does not exist or is disabled. Null, and so absent from the
        /// JSON under the controllers' ignore-when-null setting, when <see cref="Answered"/> is true.
        /// </summary>
        public string? FailureReason { get; set; }

        /// <summary>
        /// When a cooldown is behind <see cref="FailureReason"/>, the earliest time, in UTC, the
        /// indexer may be asked again. Null, and so absent from the JSON, otherwise.
        /// </summary>
        public DateTime? RetryAfterUtc { get; set; }

        /// <summary>
        /// The endpoint's payload: <paramref name="results"/> unchanged unless the caller asked
        /// for the outcome, in which case the envelope around them.
        /// </summary>
        public static object Wrap(object results, bool includeOutcome, IndexerSearchOutcome? outcome)
        {
            if (!includeOutcome)
            {
                return results;
            }

            var answered = outcome?.Status == IndexerSearchStatus.Completed;
            return new IndexerSearchOutcomeResponse
            {
                Results = results,
                Answered = answered,
                FailureReason = answered ? null : DescribeFailure(outcome),
                RetryAfterUtc = answered ? null : outcome?.Deferral?.UntilUtc
            };
        }

        private static string? DescribeFailure(IndexerSearchOutcome? outcome) => outcome switch
        {
            // A null outcome is an indexer that does not exist or is disabled.
            null => "NotFound",
            { Status: IndexerSearchStatus.Deferred, Deferral: { } deferral } => deferral.Reason.ToString(),
            // Never sent; the provider-lookup detail behind it is not what the operator acts on.
            { FailureReason: "no provider" } => "NotConfigured",
            { Status: IndexerSearchStatus.Failed } => outcome.FailureReason,
            _ => outcome.Status.ToString()
        };
    }
}
