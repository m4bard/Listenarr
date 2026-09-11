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
/// Which exceptions mean "the provider did not answer" rather than "the provider answered and
/// had nothing".
/// </summary>
/// <remarks>
/// One predicate rather than a repeated exception filter, because every layer between the HTTP
/// client and the refresh service has a catch-all that turns a fault into null, and the two have
/// to agree on what may not be turned into null. A null that reaches
/// <c>MetadataRefreshService</c> is read as the provider saying it has never heard of the book,
/// and a book read that way is stamped and not asked about again for a month.
/// </remarks>
public static class MetadataProviderFaults
{
    /// <summary>
    /// True when <paramref name="exception"/> says the request never got an answer: pushback, a
    /// connection that would not open, a name that would not resolve, a request that timed out.
    /// </summary>
    public static bool IsProviderUnavailable(Exception? exception) => exception switch
    {
        MetadataProviderThrottledException => true,
        HttpRequestException => true,
        _ => false
    };
}
