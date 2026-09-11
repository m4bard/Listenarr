/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Application.Metadata.Refresh;

/// <summary>
/// The provider asked for less traffic. A metadata client raises this when it recognises
/// pushback, carrying the wait the provider named if it named one.
/// </summary>
/// <remarks>
/// A type of its own rather than a key in <see cref="Exception.Data"/> on some general
/// exception: nothing populates a dictionary key by accident, so a signal defined that way is
/// dead until somebody remembers it exists. Teaching a client to push back is now a
/// compile-time obligation with one obvious shape.
/// <para>
/// <c>AudibleApiClient</c> raises it on a 429 and carries the Retry-After the response named.
/// The refresh service also accepts an <see cref="HttpRequestException"/> carrying
/// <c>TooManyRequests</c>, so a client that throws from <c>EnsureSuccessStatusCode</c> instead
/// still narrows the run.
/// </para>
/// </remarks>
public sealed class MetadataProviderThrottledException : Exception
{
    public MetadataProviderThrottledException(
        string message,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// How long the provider asked the caller to wait, or null when it only said no. The budget
    /// halves either way; a named wait also holds the next grant back that long.
    /// </summary>
    public TimeSpan? RetryAfter { get; }
}
