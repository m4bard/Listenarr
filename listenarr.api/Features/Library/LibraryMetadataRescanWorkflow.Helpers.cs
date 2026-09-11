/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
namespace Listenarr.Api.Features.Library
{
    public sealed partial class LibraryMetadataRescanWorkflow
    {
        private static bool TryConsumeMetadataRescanQuota(
            IMemoryCache cache,
            HttpContext? httpContext,
            int audiobookId,
            out string message,
            out int retryAfterSeconds)
        {
            message = string.Empty;
            retryAfterSeconds = 0;

            var actorKey = BuildMetadataRescanActorKey(httpContext);
            var cacheKey = $"metadata-rescan-rate:{audiobookId}:{actorKey}";
            var now = DateTime.UtcNow;

            if (!cache.TryGetValue(cacheKey, out MetadataRescanRateLimitState? state) || state == null)
            {
                state = new MetadataRescanRateLimitState
                {
                    WindowStartUtc = now,
                    Count = 0,
                    LastAttemptUtc = null
                };
            }

            if (state.LastAttemptUtc.HasValue)
            {
                var cooldownRemaining = TimeSpan.FromSeconds(MetadataRescanCooldownSeconds) - (now - state.LastAttemptUtc.Value);
                if (cooldownRemaining > TimeSpan.Zero)
                {
                    retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(cooldownRemaining.TotalSeconds));
                    message = $"Rescan cooldown active. Please wait {retryAfterSeconds} seconds before rescanning this audiobook again.";
                    return false;
                }
            }

            if ((now - state.WindowStartUtc) >= TimeSpan.FromMinutes(MetadataRescanWindowMinutes))
            {
                state.WindowStartUtc = now;
                state.Count = 0;
            }

            if (state.Count >= MetadataRescanMaxRequestsPerWindow)
            {
                var windowEndsAt = state.WindowStartUtc.AddMinutes(MetadataRescanWindowMinutes);
                var remaining = windowEndsAt - now;
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
                message = $"Metadata rescan rate limit reached for this audiobook. Try again in {retryAfterSeconds} seconds.";
                return false;
            }

            state.Count++;
            state.LastAttemptUtc = now;

            cache.Set(
                cacheKey,
                state,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(MetadataRescanWindowMinutes + 5)
                });

            return true;
        }

        private static string BuildMetadataRescanActorKey(HttpContext? httpContext)
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

        private sealed class MetadataRescanRateLimitState
        {
            public DateTime WindowStartUtc { get; set; }
            public int Count { get; set; }
            public DateTime? LastAttemptUtc { get; set; }
        }
    }
}
