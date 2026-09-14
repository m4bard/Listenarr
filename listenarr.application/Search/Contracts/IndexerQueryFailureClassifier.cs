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

namespace Listenarr.Application.Search.Contracts;

/// <summary>
/// Maps a transport exception to the reason an indexer query failed, so every provider reports the
/// same distinction rather than inventing its own.
/// </summary>
public static class IndexerQueryFailureClassifier
{
    /// <summary>
    /// Classifies a transport exception. HttpClient surfaces its own request timeout as a
    /// <see cref="TaskCanceledException"/> whose inner exception is a <see cref="TimeoutException"/>,
    /// which is what separates a timeout from a caller-initiated cancellation.
    /// </summary>
    public static IndexerQueryReason Classify(Exception exception)
    {
        return exception switch
        {
            TimeoutException => IndexerQueryReason.Timeout,
            OperationCanceledException canceled when canceled.InnerException is TimeoutException => IndexerQueryReason.Timeout,
            OperationCanceledException => IndexerQueryReason.Cancelled,
            HttpRequestException => IndexerQueryReason.NetworkError,
            _ => IndexerQueryReason.NetworkError
        };
    }

    /// <summary>
    /// A short, non-identifying description of a failure, safe to put in a log line.
    /// </summary>
    public static string Describe(Exception exception)
    {
        return LogRedaction.SanitizeText(exception.GetType().Name);
    }

    /// <summary>
    /// A short description of a non-2xx response, safe to put in a log line.
    /// </summary>
    public static string Describe(System.Net.HttpStatusCode statusCode)
    {
        return LogRedaction.SanitizeText(((int)statusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Classifies a non-2xx response. A 429 and a 403 are not the same event as a 500: one is the
    /// remote naming a rate, one is a credential that will never recover on its own, and collapsing
    /// them into a single "non-2xx" leaves a caller unable to react differently to any of them.
    /// </summary>
    public static IndexerQueryReason Classify(System.Net.HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            System.Net.HttpStatusCode.TooManyRequests => IndexerQueryReason.RateLimited,
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => IndexerQueryReason.AuthFailure,
            _ => IndexerQueryReason.HttpStatus
        };
    }

    /// <summary>
    /// Reads a <c>Retry-After</c> header in either wire form: a delta in seconds, or an HTTP date.
    /// Mirrors TorrentFileDownloader.GetRetryDelay, which is the in-repo precedent for this header,
    /// but deliberately applies no upper clamp: the caller wants the number the indexer actually
    /// stated, not a retry delay capped to something a single request is willing to wait.
    /// </summary>
    public static TimeSpan? ReadRetryAfter(System.Net.Http.Headers.RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter == null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta > TimeSpan.Zero ? delta : null;
        }

        if (retryAfter.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : null;
        }

        return null;
    }
}
