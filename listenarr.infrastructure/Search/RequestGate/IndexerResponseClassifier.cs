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

using System.Net;

namespace Listenarr.Infrastructure.Search.RequestGate;

/// <summary>
/// What an answered request, or a transport failure, means for its indexer's state.
/// </summary>
public enum IndexerRequestOutcome
{
    /// <summary>The indexer answered without an error; clears the consecutive-failure count.</summary>
    Succeeded,

    /// <summary>A 5xx, a 408, a transport error or a timeout; counts toward the failure ladder.</summary>
    Failed,

    /// <summary>A 429, or a 503 with Retry-After; sets a rate-limit cooldown.</summary>
    RateLimited
}

/// <summary>
/// One state change the gate asks the cache to apply for an indexer.
/// </summary>
/// <param name="IndexerId">The indexer the request went to.</param>
/// <param name="Outcome">What the request's result means for the indexer.</param>
/// <param name="DispatchStartUtc">When the request was dispatched.</param>
/// <param name="ObservedUtc">When the result was seen; cooldowns are measured from it.</param>
/// <param name="RetryAfter">For <see cref="IndexerRequestOutcome.RateLimited"/>, the delay the
/// indexer asked for, or null when it gave none that could be used.</param>
public sealed record IndexerRequestTransition(
    int IndexerId,
    IndexerRequestOutcome Outcome,
    DateTime DispatchStartUtc,
    DateTime ObservedUtc,
    TimeSpan? RetryAfter = null);

/// <summary>
/// Sorts an indexer's HTTP response into the outcome the gate records.
/// </summary>
public static class IndexerResponseClassifier
{
    /// <summary>
    /// A 429, or a 503 carrying Retry-After, is a rate limit. Any other 5xx, and 408, is a
    /// failure. Any other 4xx is a request the indexer refused, usually a configuration error,
    /// and returns null: it neither counts as a failure nor clears the count. Everything else
    /// succeeded.
    /// </summary>
    public static IndexerRequestOutcome? Classify(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var status = response.StatusCode;
        if (status == HttpStatusCode.TooManyRequests
            || (status == HttpStatusCode.ServiceUnavailable && response.Headers.RetryAfter is not null))
        {
            return IndexerRequestOutcome.RateLimited;
        }

        var code = (int)status;
        if (code >= 500 || status == HttpStatusCode.RequestTimeout)
        {
            return IndexerRequestOutcome.Failed;
        }

        return code >= 400 ? null : IndexerRequestOutcome.Succeeded;
    }
}
