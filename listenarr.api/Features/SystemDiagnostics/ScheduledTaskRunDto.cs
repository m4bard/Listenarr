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

namespace Listenarr.Api.Features.SystemDiagnostics
{
    /// <summary>
    /// The answer to a manual run request: what the request did, and the task's row as it
    /// stood when that was decided.
    /// </summary>
    /// <remarks>
    /// <see cref="Triggered"/> exists because the row alone cannot carry it. Both a
    /// started run and a request that joined one already in flight answer 202 with
    /// <c>isRunning</c> true, the same <c>lastTrigger</c>, and the same
    /// <c>lastStartedAt</c> to the tick, since the second caller is being told about the
    /// very cycle the first caller started. A UI that wants to say "started" rather than
    /// "already running" has to read it from here.
    /// </remarks>
    public sealed class ScheduledTaskRunDto
    {
        /// <summary>
        /// <c>started</c> when this request began the cycle, <c>already-running</c> when a
        /// cycle was already in flight and this request joined it rather than queuing a
        /// second one.
        /// </summary>
        public required string Triggered { get; init; }

        public required ScheduledTaskDto Task { get; init; }

        /// <summary>This request started the cycle.</summary>
        public const string Started = "started";

        /// <summary>A cycle was already in flight; nothing further was queued.</summary>
        public const string AlreadyRunning = "already-running";

        public static ScheduledTaskRunDto FromStatus(
            ScheduledTaskTriggerResult result,
            ScheduledTaskStatus status) => new()
            {
                Triggered = result == ScheduledTaskTriggerResult.AlreadyRunning
                    ? AlreadyRunning
                    : Started,
                Task = ScheduledTaskDto.FromStatus(status)
            };
    }
}
