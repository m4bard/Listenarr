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
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.SystemDiagnostics
{
    /// <summary>
    /// The System, Tasks surface: which periodic workers are running, when each last
    /// ran and is next due, and a way to run one on demand.
    /// </summary>
    /// <remarks>
    /// The list is every worker driven by <c>IWorkerCycleRunner</c>, and only those.
    /// Work that runs off a queue rather than an interval is not on it, which currently
    /// means the library scan itself (the channel reader at
    /// <c>Library/Scanning/ScanBackgroundService.cs:47</c>; only its 30-second move
    /// handoff recovery poll registers) and the two hand-rolled polling loops in
    /// <c>QueueMonitorService</c> and <c>DownloadProcessingJobProcessor</c>. Bringing
    /// queue-driven work onto this surface needs a shape a periodic cycle runner does
    /// not have, so it is deliberately out of scope here rather than overlooked.
    /// </remarks>
    [ApiController]
    [Route("api/v{version:apiVersion}/system/tasks")]
    [Tags("System")]
    public class ScheduledTasksController : ControllerBase
    {
        private readonly IScheduledTaskRegistry _scheduledTasks;

        public ScheduledTasksController(IScheduledTaskRegistry scheduledTasks)
        {
            _scheduledTasks = scheduledTasks;
        }

        /// <summary>
        /// Every periodic worker the host currently has running, ordered by name.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<ScheduledTaskDto>), StatusCodes.Status200OK)]
        public ActionResult<IReadOnlyList<ScheduledTaskDto>> GetAll()
        {
            var tasks = _scheduledTasks.GetAll()
                .Select(ScheduledTaskDto.FromStatus)
                .ToList();

            return Ok(tasks);
        }

        /// <summary>
        /// One task by name.
        /// </summary>
        [HttpGet("{taskName}")]
        [ProducesResponseType(typeof(ScheduledTaskDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<ScheduledTaskDto> GetByName(string taskName)
        {
            var status = _scheduledTasks.Find(taskName);

            return status is null
                ? NotFound(new { error = $"No scheduled task named '{taskName}' is running." })
                : Ok(ScheduledTaskDto.FromStatus(status));
        }

        /// <summary>
        /// Runs one cycle of a task now. Returns as soon as the cycle has started,
        /// because a scan or a metadata rescan outlives any sensible request. The body is
        /// the task's row with the manual cycle already marked started.
        /// </summary>
        /// <remarks>
        /// Only tasks on the manual-run allowlist can be reached here. A registered task
        /// that is not on it answers 403 rather than 404: it exists, the caller is simply
        /// not allowed to bring its cycle forward, and saying "no such task" would send
        /// them looking for a spelling mistake that is not there.
        /// <para>
        /// A task already running answers 202 with the cycle that is in flight rather
        /// than an error, which is how the family answers the same request. Sonarr's
        /// <c>CommandQueueManager.Push</c> returns the command already queued or started
        /// (<c>NzbDrone.Core/Messaging/Commands/CommandQueueManager.cs:111-121</c>) and
        /// its controller hands that straight back as a success
        /// (<c>Sonarr.Api.V3/Commands/CommandController.cs:75-77</c>), so a user clicking
        /// twice gets the running command to watch instead of a failure. The row says
        /// <c>isRunning</c> and carries <c>lastStartedAt</c>, so a caller that needs to
        /// tell the two apart still can.
        /// </para>
        /// </remarks>
        [HttpPost("{taskName}/run")]
        [ProducesResponseType(typeof(ScheduledTaskDto), StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<ScheduledTaskDto> Run(string taskName)
        {
            var outcome = _scheduledTasks.Trigger(taskName);

            switch (outcome.Result)
            {
                case ScheduledTaskTriggerResult.NotFound:
                    return NotFound(new { error = $"No scheduled task named '{taskName}' is running." });

                case ScheduledTaskTriggerResult.NotAllowed:
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            error = $"'{taskName}' runs on its schedule only and cannot be started on demand."
                        });

                default:
                    // Accepted and AlreadyRunning both answer with the cycle that is in
                    // flight. The status travels with the trigger result rather than being
                    // re-read here, because the cycle body runs on a pool thread and a
                    // second read would usually describe the previous cycle instead.
                    return outcome.Status is null
                        ? Accepted()
                        : Accepted(ScheduledTaskDto.FromStatus(outcome.Status));
            }
        }
    }
}
