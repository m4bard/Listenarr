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
    /// What came of asking a task to run out of band, together with the row as it stood
    /// at the moment the answer was decided.
    /// </summary>
    /// <remarks>
    /// The status travels with the result because the caller cannot read it afterwards
    /// without a race. An accepted trigger hands back the row with the manual cycle
    /// already marked started; re-reading the registry instead would usually catch the
    /// previous cycle's row, because the cycle body is dispatched onto the thread pool
    /// while the caller carries straight on.
    /// </remarks>
    public sealed record ScheduledTaskTriggerOutcome
    {
        public required ScheduledTaskTriggerResult Result { get; init; }

        /// <summary>
        /// The task as it stood when the answer was decided, or null when no task of
        /// that name is registered.
        /// </summary>
        public ScheduledTaskStatus? Status { get; init; }

        public static ScheduledTaskTriggerOutcome NotFound { get; } = new()
        {
            Result = ScheduledTaskTriggerResult.NotFound
        };

        public static ScheduledTaskTriggerOutcome For(
            ScheduledTaskTriggerResult result,
            ScheduledTaskStatus status) => new() { Result = result, Status = status };
    }
}
