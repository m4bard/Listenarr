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

using System.Net.Http.Headers;

namespace Listenarr.Application.Search.Indexers.Common
{
    /// <summary>
    /// Turns a Retry-After header into the cooldown an indexer asked for.
    /// </summary>
    public static class RetryAfterParser
    {
        /// <summary>
        /// Reads delta-seconds or an HTTP-date. A period that is not positive, or a date that is
        /// not in the future, gives <see cref="IndexerRequestPolicy.RetryAfterPastDate"/>; the
        /// result never exceeds <see cref="IndexerRequestPolicy.RetryAfterMaximum"/>.
        /// </summary>
        /// <param name="retryAfter">The response's Retry-After header, or null.</param>
        /// <param name="timeProvider">The clock a date is measured against.</param>
        /// <returns>The requested delay, or null when the header is missing or unusable.</returns>
        public static TimeSpan? Parse(RetryConditionHeaderValue? retryAfter, TimeProvider timeProvider)
        {
            var delay = retryAfter?.Delta;
            if (delay == null && retryAfter?.Date is DateTimeOffset retryDate)
            {
                delay = retryDate - timeProvider.GetUtcNow();
            }

            if (delay is not { } requested)
            {
                return null;
            }

            if (requested <= TimeSpan.Zero)
            {
                return IndexerRequestPolicy.RetryAfterPastDate;
            }

            return requested > IndexerRequestPolicy.RetryAfterMaximum
                ? IndexerRequestPolicy.RetryAfterMaximum
                : requested;
        }
    }
}
