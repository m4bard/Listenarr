/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Application.Metadata.Faults;

/// <summary>
/// Which exceptions mean "the provider did not answer" rather than "the provider answered and
/// had nothing".
/// </summary>
/// <remarks>
/// One predicate rather than a repeated exception filter, because every layer between the HTTP
/// client and the callers has a catch-all that turns a fault into null, and the two have to
/// agree on what may not be turned into null. A null coming out of a metadata lookup is read
/// as the provider saying it has never heard of the book, and callers act on that: they stop
/// asking, they report the book as missing, they write the absence down.
/// <para>
/// Asked rather than restated at each site, so a client taught to raise a new kind of "did not
/// answer" cannot quietly start escaping somewhere that used to degrade gracefully.
/// </para>
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
