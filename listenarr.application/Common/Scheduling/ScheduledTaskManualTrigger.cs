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
    /// Whether a worker may be run out of band from the task surface.
    /// </summary>
    /// <remarks>
    /// The default is <see cref="Denied"/>, and that is the whole point of the type.
    /// Membership of the manual-run set is an allowlist: a worker is triggerable only
    /// because its own author said so at the point it asks to be driven. A worker added
    /// tomorrow, by somebody who has never read this file, is not triggerable, and a
    /// deny-list would have had to anticipate it.
    /// <para>
    /// This is how the rest of the family draws the same line. In Sonarr, Readarr and
    /// Prowlarr the task surface is read-only (<c>Readarr.Api.V1/System/Tasks/TaskController.cs:25-45</c>
    /// has a GET and nothing else); running something on demand is a separate endpoint,
    /// and the set it accepts is exactly the types implementing the <c>Command</c> base
    /// class (<c>Readarr.Api.V1/Commands/CommandController.cs:54-57</c>, enumerated by
    /// <c>NzbDrone.Common/Composition/KnownTypes.cs:22-29</c>). Something periodic that
    /// nobody wrote a command for has no route at all: <c>NzbDrone.Core/Jobs/Scheduler.cs</c>
    /// runs on a thirty-second timer and cannot be reached from the API. Declaring the
    /// answer beside the worker also matches how a command declares
    /// <c>RequiresDiskAccess</c>, <c>IsExclusive</c> and <c>IsLongRunning</c> on itself
    /// (<c>NzbDrone.Core/Messaging/Commands/Command.cs:25-30</c>) rather than in a central
    /// table somebody has to remember to edit.
    /// </para>
    /// </remarks>
    public enum ScheduledTaskManualTrigger
    {
        /// <summary>
        /// The worker runs on its schedule only. A manual request is refused, and said to
        /// be refused, rather than quietly dropped or reported as an unknown task.
        /// </summary>
        Denied = 0,

        /// <summary>
        /// The worker may be run on demand. Reserve this for cycles that are safe to
        /// bring forward: a refresh, a rescan, a poll, a search. A cycle that deletes
        /// or prunes stored rows is not one of those, because bringing it forward
        /// destroys state the caller was not warned about.
        /// </summary>
        Allowed = 1
    }
}
