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
    /// The registry's side of one registered worker. The cycle runner holds it for
    /// the life of the worker loop and routes every cycle through it, which is what
    /// makes last execution, duration and outcome observable without a worker
    /// having to report anything itself. Disposing it deregisters the worker.
    /// </summary>
    public interface IScheduledTaskHandle : IDisposable
    {
        /// <summary>
        /// Runs the registered cycle under the task's exclusion gate, recording the
        /// start, the end and the outcome. Waits for a manual run already in flight
        /// rather than dropping the scheduled cycle. Exceptions are recorded and
        /// rethrown so the caller keeps its own error handling.
        /// </summary>
        Task RunCycleAsync(ScheduledTaskTrigger trigger, CancellationToken cancellationToken);

        /// <summary>
        /// Publishes when the next scheduled cycle is due. Called by the runner
        /// immediately before it waits out the interval.
        /// </summary>
        void RecordNextExecution(DateTimeOffset nextExecution);
    }
}
