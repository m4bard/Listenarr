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
    /// Indexer health from the request gate: which enabled indexers a search would be held back
    /// from right now, until when and why.
    /// </summary>
    /// <remarks>
    /// Follows the *arr family's indexer status check: every enabled indexer in cooldown is an
    /// error, a subset is a warning. The gate keeps no start time for a run of failures, so the
    /// family's separate "failing for more than six hours" check has nothing to read and is not
    /// reported.
    /// </remarks>
    internal static class IndexerHealthMapper
    {
        public static IndexerHealth BuildIndexerHealth(
            IEnumerable<Indexer> indexers,
            IReadOnlyDictionary<int, RequestDeferral> deferrals)
        {
            var enabled = (indexers ?? Enumerable.Empty<Indexer>()).Where(i => i.IsEnabled).ToList();
            var cooldowns = enabled
                .Where(i => deferrals.ContainsKey(i.Id))
                .Select(i => new IndexerCooldownHealth
                {
                    IndexerId = i.Id,
                    Name = i.Name,
                    UntilUtc = deferrals[i.Id].UntilUtc,
                    Reason = deferrals[i.Id].Reason.ToString()
                })
                .ToList();

            var checks = new List<IndexerHealthCheck>();
            if (cooldowns.Count > 0)
            {
                var names = cooldowns.Select(c => c.Name).ToList();
                var all = cooldowns.Count == enabled.Count;
                checks.Add(new IndexerHealthCheck
                {
                    Kind = IndexerHealthCheck.StatusKind,
                    Status = all ? "error" : "warning",
                    Message = all
                        ? "All indexers are unavailable: each is in a request cooldown"
                        : $"Indexers unavailable while in a request cooldown: {string.Join(", ", names)}",
                    IndexerNames = names
                });
            }

            var available = enabled.Count - cooldowns.Count;

            return new IndexerHealth
            {
                Status = SystemHealthMapper.BuildChildStatus(available, enabled.Count),
                Available = available,
                Total = enabled.Count,
                Checks = checks,
                Cooldowns = cooldowns
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
    }
}
