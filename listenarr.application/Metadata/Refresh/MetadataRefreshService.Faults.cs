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
using Listenarr.Application.Metadata.Faults;

namespace Listenarr.Application.Metadata.Refresh;

/// <summary>
/// How this service reads a provider fault. Separated from the walk that acts on one, because
/// the two decisions are different: whether a fault is pushback is a property of the exception,
/// while what to do about it is a property of the run.
/// </summary>
public sealed partial class MetadataRefreshService
{
    /// <summary>
    /// Says whether <paramref name="exception"/> is the provider asking for less, and how long it
    /// asked for if it said. Reading the signal is separate from acting on it: the run is
    /// narrowed once per book, on the first piece of pushback, not once per attempt.
    /// </summary>
    private static bool TryReadPushback(Exception exception, out TimeSpan? retryAfter)
    {
        retryAfter = null;

        // Only pushback is the provider asking for less. A DNS failure or a timeout is transient
        // in a different way, and halving the allowance for one would ratchet a whole walk down
        // to a request an hour inside a couple of books. Those still retry and still defer; they
        // just do not narrow what is left of the run.
        //
        // Two shapes are accepted. MetadataProviderThrottledException is the one a client should
        // raise, and is the only one that can carry a Retry-After. A bare HttpRequestException
        // carrying the status is what a client throwing from EnsureSuccessStatusCode produces,
        // and is honoured so the halving does not depend on which of the two arrives first.
        switch (exception)
        {
            case MetadataProviderThrottledException throttled:
                retryAfter = throttled.RetryAfter;
                return true;
            case HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests }:
                return true;
            default:
                return false;
        }
    }
}
