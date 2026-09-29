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
    /// One indexer's search results together with whether the indexer could be asked at all.
    /// Returned by <c>GET /search/{apiId}</c> only when the caller passes <c>includeOutcome=true</c>;
    /// without it the endpoint returns the bare result list it always has.
    /// </summary>
    public sealed class IndexerSearchOutcomeResponse
    {
        /// <summary>The results, in the same shape the endpoint returns without the envelope.</summary>
        public object Results { get; set; } = Array.Empty<object>();

        /// <summary>
        /// True when the indexer gave a readable answer, including an empty one. False when it
        /// timed out, errored, sent something unreadable, is not usable as configured, or does not
        /// exist: an empty result list then says nothing about whether the release exists.
        /// </summary>
        public bool Answered { get; set; }

        /// <summary>Why the indexer could not be asked; null when <see cref="Answered"/> is true.</summary>
        public string? FailureReason { get; set; }

        /// <summary>
        /// The endpoint's payload: <paramref name="results"/> unchanged unless the caller asked
        /// for the outcome, in which case the envelope around them.
        /// </summary>
        public static object Wrap(object results, bool includeOutcome, IndexerQueryObservation? observation)
        {
            if (!includeOutcome)
            {
                return results;
            }

            var answered = observation?.Answered ?? false;
            return new IndexerSearchOutcomeResponse
            {
                Results = results,
                Answered = answered,
                // A null observation is an indexer that does not exist or is disabled.
                FailureReason = answered ? null : observation?.Reason.ToString() ?? "NotFound"
            };
        }
    }
}
