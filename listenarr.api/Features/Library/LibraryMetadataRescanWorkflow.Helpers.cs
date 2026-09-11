/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
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

            var actorKey = LibraryRequestActorKey.Build(httpContext);
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

        private sealed class MetadataRescanRateLimitState
        {
            public DateTime WindowStartUtc { get; set; }
            public int Count { get; set; }
            public DateTime? LastAttemptUtc { get; set; }
        }
    }
}
