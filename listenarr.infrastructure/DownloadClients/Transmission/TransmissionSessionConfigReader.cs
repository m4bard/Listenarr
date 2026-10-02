/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.DownloadClients.Transmission
{
    /// <summary>
    /// Reads Transmission's session config (global seed-ratio/idle-seeding limits), used to
    /// resolve a per-torrent seed limit that is set to "inherit session". Shared between the
    /// item-fetch and queue-poll workflows so both can reach the same seed-limit-aware
    /// <c>CanBeRemoved</c>/<c>CanRemove</c> computation.
    /// </summary>
    internal static class TransmissionSessionConfigReader
    {
        public static async Task<(bool SeedRatioLimited, double SeedRatioLimit, bool IdleSeedingLimitEnabled, int IdleSeedingLimit)> ReadAsync(
            TransmissionRpcClient rpcClient,
            DownloadClientConfiguration client,
            ILogger logger,
            CancellationToken ct)
        {
            var sessionSeedRatioLimited = false;
            var sessionSeedRatioLimit = 0.0;
            var sessionIdleSeedingLimitEnabled = false;
            var sessionIdleSeedingLimit = 0;
            try
            {
                var sessionPayload = new { method = "session-get", arguments = new { }, tag = 99 };
                var sessionResp = await rpcClient.InvokeAsync(client, sessionPayload, ct);
                if (sessionResp.TryGetProperty("arguments", out var sessionArgs))
                {
                    sessionSeedRatioLimited = (sessionArgs.TryGetProperty("seedRatioLimited", out var srl) || sessionArgs.TryGetProperty("seed_ratio_limited", out srl)) && srl.GetBoolean();
                    sessionSeedRatioLimit = (sessionArgs.TryGetProperty("seedRatioLimit", out var srlv) || sessionArgs.TryGetProperty("seed_ratio_limit", out srlv)) ? srlv.GetDouble() : 0;
                    sessionIdleSeedingLimitEnabled = (sessionArgs.TryGetProperty("idle-seeding-limit-enabled", out var isle) || sessionArgs.TryGetProperty("idle_seeding_limit_enabled", out isle)) && isle.GetBoolean();
                    sessionIdleSeedingLimit = (sessionArgs.TryGetProperty("idle-seeding-limit", out var isl) || sessionArgs.TryGetProperty("idle_seeding_limit", out isl)) ? isl.GetInt32() : 0;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogDebug(ex, "Failed to fetch Transmission session config for seed limit evaluation, will use conservative defaults");
            }

            return (sessionSeedRatioLimited, sessionSeedRatioLimit, sessionIdleSeedingLimitEnabled, sessionIdleSeedingLimit);
        }
    }
}
