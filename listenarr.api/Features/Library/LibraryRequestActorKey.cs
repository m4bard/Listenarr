/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using System.Security.Cryptography;
using System.Text;

namespace Listenarr.Api.Features.Library
{
    /// <summary>
    /// Who is asking, as a short opaque key a rate-limit entry can hang off.
    /// </summary>
    /// <remarks>
    /// Signed-in identity where there is one, and the remote address either way, so two people
    /// behind one address are still told apart and one person is not let through twice by
    /// signing out. Hashed because it ends up in cache keys and in log lines, and neither is a
    /// place for a username or an address.
    /// <para>
    /// Shared by the per-book rescan and the refresh trigger. Two throttles keyed by two
    /// slightly different notions of "the same caller" would be a bug nobody could see: the
    /// looser of the two would quietly become the real limit.
    /// </para>
    /// </remarks>
    internal static class LibraryRequestActorKey
    {
        public static string Build(HttpContext? httpContext)
        {
            var user = httpContext?.User;
            var userId =
                user?.FindFirst("sub")?.Value ??
                user?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ??
                user?.Identity?.Name;

            var remoteIp = httpContext?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";

            var actorDescriptor = !string.IsNullOrWhiteSpace(userId)
                ? $"user:{userId}|ip:{remoteIp}"
                : $"ip:{remoteIp}";

            return ComputeShortHash(actorDescriptor);
        }

        private static string ComputeShortHash(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return Guid.NewGuid().ToString("N").Substring(0, 12);

            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = SHA1.HashData(bytes);
            return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16).ToLowerInvariant();
        }
    }
}
