/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Common
{
    /// <summary>
    /// The one 503 every metadata and search endpoint answers when the provider did not answer.
    /// </summary>
    /// <remarks>
    /// A ProblemDetails, because ServerErrorProblemDetailsFilter rewrites any result of 500 or
    /// above that is not already one into a generic internal_error and, outside Development,
    /// drops the detail. A string or an anonymous object here reaches the caller as "Internal
    /// server error", which hides the one thing this status exists to say. One builder rather
    /// than one per endpoint, so the title and code a client keys on cannot drift apart.
    /// </remarks>
    public static class MetadataProviderUnavailableProblem
    {
        public const string Code = "metadata_provider_unavailable";
        public const string Title = "Metadata provider unavailable";

        private const string NotAnsweringDetail = "The metadata provider is not answering. Try again shortly.";
        private const string ThrottledDetail = "The metadata provider asked for less traffic. Try again shortly.";

        /// <summary>
        /// Builds the 503. When <paramref name="cause"/> is pushback that named a wait, the wait
        /// goes out as Retry-After on <paramref name="response"/> and as retryAfterSeconds in the
        /// body; otherwise neither is set, because nothing on this side knows how long to wait.
        /// </summary>
        /// <param name="response">The response to set Retry-After on; null when there is none.</param>
        /// <param name="cause">The provider fault, or null when the caller only knows the outcome.</param>
        public static ObjectResult Create(HttpResponse? response, Exception? cause = null)
        {
            var throttled = cause as MetadataProviderThrottledException;
            var detail = throttled != null ? ThrottledDetail : NotAnsweringDetail;

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = Title,
                Detail = detail
            };
            problem.Extensions["code"] = Code;
            problem.Extensions["message"] = detail;

            if (throttled?.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero)
            {
                var retryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
                problem.Extensions["retryAfterSeconds"] = retryAfterSeconds;
                if (response != null)
                {
                    response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            return new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable,
                ContentTypes = { "application/problem+json" }
            };
        }
    }
}
