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

namespace Listenarr.Application.Search.Indexers.Common
{
    /// <summary>
    /// Every tunable of the indexer request gate in one place: pacing, concurrency caps, the
    /// consecutive-failure threshold and the cooldown escalation ladder.
    /// </summary>
    public static class IndexerRequestPolicy
    {
        /// <summary>Minimum time between two dispatch starts to the same indexer.</summary>
        public static readonly TimeSpan Pacing = TimeSpan.FromSeconds(2);

        /// <summary>Requests that may be in flight to one indexer at once.</summary>
        public const int MaxConcurrentPerIndexer = 2;

        /// <summary>Requests that may be in flight to all indexers together.</summary>
        public const int MaxConcurrentGlobal = 8;

        /// <summary>Consecutive failures before the first cooldown is set.</summary>
        public const int FailureThreshold = 3;

        /// <summary>The longest cooldown a Retry-After header may set.</summary>
        public static readonly TimeSpan RetryAfterMaximum = TimeSpan.FromHours(24);

        /// <summary>The cooldown for a Retry-After date that is not in the future.</summary>
        public static readonly TimeSpan RetryAfterPastDate = TimeSpan.FromSeconds(60);

        /// <summary>How long to wait before asking again after a reservation or state failure.</summary>
        public static readonly TimeSpan UnavailableRetry = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Cooldown periods by escalation step. A 429 without a usable Retry-After starts at
        /// <see cref="RateLimitStartStep"/>.
        /// </summary>
        public static readonly IReadOnlyList<TimeSpan> EscalationLadder =
        [
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(3),
            TimeSpan.FromHours(6),
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(24)
        ];

        /// <summary>The ladder index of the 1 hour step, where 429 escalation starts.</summary>
        public const int RateLimitStartStep = 4;

        /// <summary>
        /// The cooldown after a failure, or null while the consecutive count is below the
        /// threshold. The count at the threshold gets the first step.
        /// </summary>
        /// <param name="consecutiveFailures">Consecutive failures, this one included.</param>
        public static TimeSpan? FailureCooldown(int consecutiveFailures)
        {
            if (consecutiveFailures < FailureThreshold)
            {
                return null;
            }

            return LadderStep(consecutiveFailures - FailureThreshold);
        }

        /// <summary>
        /// The cooldown for a 429 without a usable Retry-After.
        /// </summary>
        /// <param name="priorRateLimits">Consecutive rate limits already recorded.</param>
        public static TimeSpan RateLimitCooldown(int priorRateLimits)
        {
            return LadderStep(RateLimitStartStep + priorRateLimits);
        }

        /// <summary>
        /// The ladder period at a step; steps past the end stay on the last period.
        /// </summary>
        /// <param name="step">Zero-based step, clamped into the ladder.</param>
        public static TimeSpan LadderStep(int step)
        {
            return EscalationLadder[Math.Clamp(step, 0, EscalationLadder.Count - 1)];
        }
    }
}
