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
    /// The read surface over the periodic workers that are actually running, plus a
    /// manual trigger for them. Membership is discovered: a worker appears because
    /// it asked the cycle runner to drive it, not because it was added to a list.
    /// </summary>
    public interface IScheduledTaskRegistry
    {
        /// <summary>
        /// Announces a worker for the life of its loop. Called by the cycle runner,
        /// not by workers directly. <paramref name="manualTrigger"/> is the worker's
        /// own answer to whether it may be run out of band; the cycle runner passes
        /// through whatever the worker said, and the default is to refuse.
        /// </summary>
        IScheduledTaskHandle Register(
            string taskName,
            Func<TimeSpan> intervalProvider,
            Func<CancellationToken, Task> runCycle,
            ScheduledTaskManualTrigger manualTrigger,
            CancellationToken workerCancellation);

        /// <summary>All currently registered tasks, ordered by name.</summary>
        IReadOnlyList<ScheduledTaskStatus> GetAll();

        /// <summary>One task by name, or null when nothing is registered under it.</summary>
        ScheduledTaskStatus? Find(string taskName);

        /// <summary>
        /// Starts one cycle out of band on a background task and returns at once.
        /// The run is bound to the worker's own cancellation, so shutdown stops it.
        /// </summary>
        /// <remarks>
        /// The row travels back with the answer rather than being left for the caller to
        /// fetch. An accepted trigger has already marked the manual cycle started, and a
        /// caller that re-read the registry instead would usually get the previous
        /// cycle's row, because the cycle body is dispatched onto the thread pool.
        /// </remarks>
        ScheduledTaskTriggerOutcome Trigger(string taskName);
    }
}
