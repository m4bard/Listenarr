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

namespace Listenarr.Infrastructure.SystemDiagnostics.Diagnostics
{
    /// <summary>
    /// Indexer health from the failure-backoff columns: which enabled indexers are in a cooldown
    /// right now, split by how long their failure run has lasted.
    /// </summary>
    /// <remarks>
    /// The split follows the *arr family's pair of indexer status checks. A run that began within
    /// the last six hours is "failing right now"; one that began earlier is the separate condition
    /// "failing for more than six hours", which means nobody has noticed. Within each check, every
    /// enabled indexer blocked is an error and a subset is a warning.
    /// </remarks>
    internal static class IndexerHealthMapper
    {
        public static readonly TimeSpan LongTermThreshold = TimeSpan.FromHours(6);

        public static IndexerHealth BuildIndexerHealth(IEnumerable<Indexer> indexers, DateTime utcNow)
        {
            var enabled = (indexers ?? Enumerable.Empty<Indexer>()).Where(i => i.IsEnabled).ToList();
            var blocked = enabled.Where(i => IndexerBackoffState.From(i).IsBlockedAt(utcNow)).ToList();

            // InitialFailure is set once at the start of a run and kept until the indexer walks all
            // the way back down, so it measures the run and not the latest failure. A blocked row
            // with no run start is reported as a current failure rather than dropped from both
            // checks: a blocked indexer missing from health is the silence this exists to end.
            var longTermCutoff = utcNow - LongTermThreshold;
            var longTerm = blocked.Where(i => i.InitialFailure is { } began && began <= longTermCutoff).ToList();
            var nearTerm = blocked.Except(longTerm).ToList();

            var checks = new List<IndexerHealthCheck>();
            AddCheck(checks, nearTerm, enabled.Count, IndexerHealthCheck.NearTermKind, string.Empty);
            AddCheck(checks, longTerm, enabled.Count, IndexerHealthCheck.LongTermKind, " for more than 6 hours");

            var available = enabled.Count - blocked.Count;

            return new IndexerHealth
            {
                // Taken from the counts, not from the checks: with one run on each side of the
                // boundary neither check covers every indexer, yet nothing is left to ask.
                Status = SystemHealthMapper.BuildChildStatus(available, enabled.Count),
                Available = available,
                Total = enabled.Count,
                Checks = checks
            };
        }

        public static IndexerHealth BuildIndexerHealthError()
        {
            return new IndexerHealth
            {
                Status = "error",
                Available = 0,
                Total = 0,
                Checks = new List<IndexerHealthCheck>()
            };
        }

        private static void AddCheck(
            List<IndexerHealthCheck> checks,
            List<Indexer> unavailable,
            int enabledCount,
            string kind,
            string durationSuffix)
        {
            if (unavailable.Count == 0)
            {
                return;
            }

            var names = unavailable.Select(i => i.Name).ToList();
            var all = unavailable.Count == enabledCount;

            checks.Add(new IndexerHealthCheck
            {
                Kind = kind,
                Status = all ? "error" : "warning",
                Message = all
                    ? $"All indexers are unavailable due to failures{durationSuffix}"
                    : $"Indexers unavailable due to failures{durationSuffix}: {string.Join(", ", names)}",
                IndexerNames = names
            });
        }
    }
}
