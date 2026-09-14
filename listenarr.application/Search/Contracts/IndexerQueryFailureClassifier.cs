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
}
