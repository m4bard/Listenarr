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

namespace Listenarr.Application.Common.Scheduling
{
    /// <summary>
    /// A point-in-time view of one registered periodic worker. Everything here is
    /// observed by <c>IWorkerCycleRunner</c> as it drives the worker, so a worker
    /// does not have to declare anything about itself to appear on this surface.
    /// </summary>
    public sealed record ScheduledTaskStatus
    {
        /// <summary>The worker name the cycle runner was called with.</summary>
        public required string TaskName { get; init; }

        /// <summary>The gap the runner will wait after the current cycle finishes.</summary>
        public required TimeSpan Interval { get; init; }

        /// <summary>When the worker first announced itself to the registry.</summary>
        public required DateTimeOffset RegisteredAt { get; init; }

        /// <summary>
        /// Whether the worker's loop is still running. A worker that has stopped, or
        /// whose host has torn it down, keeps its row with this false rather than
        /// vanishing: for a monitoring surface, "it stopped" is the state most worth
        /// seeing, and removing the row reported it as a task that never existed.
        /// </summary>
        public required bool IsRegistered { get; init; }

        /// <summary>True while a cycle is in flight, whether scheduled or manual.</summary>
        public required bool IsRunning { get; init; }

        /// <summary>
        /// Whether this worker may be run out of band. Required rather than defaulted so
        /// that anything constructing a status has to state the answer, and reported on
        /// the API row so the whole allowlist is auditable in one GET rather than by
        /// reading ten registration sites.
        /// </summary>
        public required ScheduledTaskManualTrigger ManualTrigger { get; init; }

        public DateTimeOffset? LastStartedAt { get; init; }

        public DateTimeOffset? LastEndedAt { get; init; }

        public TimeSpan? LastDuration { get; init; }

        public ScheduledTaskOutcome LastOutcome { get; init; }

        public ScheduledTaskTrigger? LastTrigger { get; init; }

        /// <summary>
        /// The deadline the runner is currently waiting out. Null while a scheduled
        /// cycle is in flight, because the next one is not due until it ends. A
        /// manual run happens beside that wait and leaves the deadline standing.
        /// </summary>
        public DateTimeOffset? NextExecution { get; init; }
    }
}
