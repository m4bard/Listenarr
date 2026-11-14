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

namespace Listenarr.Domain.Search
{
    /// <summary>
    /// Why an indexer request was not sent. The scheduler's response to a deferral does not
    /// depend on the reason; the reason is for logs and for callers that report it.
    /// </summary>
    public enum RequestDeferralReason
    {
        /// <summary>
        /// The worker's per-cycle outbound request bound is spent.
        /// </summary>
        CycleRequestBound
    }

    /// <summary>
    /// An indexer request that was held back, and the earliest time it may be tried again.
    /// </summary>
    /// <param name="UntilUtc">The earliest time, in UTC, at which the request may be sent.</param>
    /// <param name="Reason">Why the request was held back.</param>
    public readonly record struct RequestDeferral(DateTime UntilUtc, RequestDeferralReason Reason);
}
