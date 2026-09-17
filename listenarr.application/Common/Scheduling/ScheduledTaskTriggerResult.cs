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
    /// Result of asking a registered task to run out of band.
    /// </summary>
    public enum ScheduledTaskTriggerResult
    {
        /// <summary>The cycle was started on a background task.</summary>
        Accepted = 0,

        /// <summary>No worker with that name is registered.</summary>
        NotFound = 1,

        /// <summary>A cycle of that task is already in flight.</summary>
        AlreadyRunning = 2,

        /// <summary>
        /// The task is registered but is not on the manual-run allowlist. Distinct from
        /// <see cref="NotFound"/> on purpose: the caller asked for something real and was
        /// refused, and telling them it does not exist would be a lie they cannot act on.
        /// </summary>
        NotAllowed = 3
    }
}
