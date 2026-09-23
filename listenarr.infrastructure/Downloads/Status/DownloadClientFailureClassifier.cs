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
using System.Net.Sockets;
using Polly.CircuitBreaker;

namespace Listenarr.Infrastructure.Downloads.Status
{
    /// <summary>
    /// Decides whether a failed submission counts against the client or only against the release.
    /// </summary>
    /// <remarks>
    /// Readarr draws this line by exception type: its DownloadService rethrows
    /// DownloadClientRejectedReleaseException without touching the client's status. The adapters
    /// here do not yet throw a type of their own for a refusal, and each one reports "the client
    /// answered no" as a plain submission exception with no transport failure inside it. So the
    /// test is the other way round: a submission counts against the client only when there is
    /// evidence the client could not be reached or is broken for every release, and everything
    /// else, a 409 for an info-hash it already holds included, is taken to be about the release.
    /// A client that is broken in a way this misses is still caught by the monitor poll, which
    /// counts every failure.
    /// </remarks>
    public static class DownloadClientFailureClassifier
    {
        /// <summary>
        /// Whether <paramref name="exception"/>, thrown by a submission, says the client itself is
        /// unavailable: no connection, no answer in time, the circuit breaker open, or an HTTP
        /// status that every release would get (a server error or an authentication failure).
        /// </summary>
        /// <param name="exception">What the submission threw.</param>
        /// <param name="callerToken">The caller's token. A cancellation the caller asked for says
        /// nothing about the client.</param>
        public static bool IsClientUnavailable(Exception exception, CancellationToken callerToken)
        {
            ArgumentNullException.ThrowIfNull(exception);

            if (callerToken.IsCancellationRequested)
            {
                return false;
            }

            for (var current = exception; current != null; current = current.InnerException)
            {
                switch (current)
                {
                    case BrokenCircuitException:
                    case SocketException:
                    case TimeoutException:
                    // HttpClient reports its own timeout as a cancellation nobody asked for.
                    case OperationCanceledException:
                        return true;
                    case HttpRequestException http when http.StatusCode is { } status:
                        return IsClientWideStatus(status);
                    case HttpRequestException:
                        // No status code: the request never got an answer.
                        return true;
                }
            }

            return false;
        }

        private static bool IsClientWideStatus(HttpStatusCode status) =>
            (int)status >= 500
            || status == HttpStatusCode.Unauthorized
            || status == HttpStatusCode.Forbidden;
    }
}
