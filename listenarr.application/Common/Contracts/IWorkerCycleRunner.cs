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

using Listenarr.Application.Common.Scheduling;

namespace Listenarr.Application.Common.Contracts
{
    public interface IWorkerCycleRunner
    {
        /// <summary>
        /// Drives a worker's cycle on an interval, and puts it on the task surface for
        /// the life of that loop.
        /// </summary>
        /// <remarks>
        /// <paramref name="manualTrigger"/> is whether this worker may also be run on
        /// demand from the task surface. It sits after the cancellation token because it
        /// carries a default, and the default is
        /// <see cref="ScheduledTaskManualTrigger.Denied"/>: a worker joins the manual-run
        /// allowlist only by saying so here. Anything added later, by anyone who has not
        /// read this, is scheduled-only until its author decides otherwise.
        /// </remarks>
        Task RunPeriodicAsync(
            string workerName,
            TimeSpan? initialDelay,
            Func<TimeSpan> intervalProvider,
            Func<CancellationToken, Task> runCycle,
            CancellationToken cancellationToken,
            ScheduledTaskManualTrigger manualTrigger = ScheduledTaskManualTrigger.Denied);
    }
}
