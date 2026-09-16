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
    /// ran and is next due, and a way to run one now.
    /// </summary>
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
        /// because a scan or a metadata rescan outlives any sensible request.
        /// </summary>
        [HttpPost("{taskName}/run")]
        [ProducesResponseType(typeof(ScheduledTaskDto), StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public ActionResult<ScheduledTaskDto> Run(string taskName)
        {
            var result = _scheduledTasks.Trigger(taskName);

            switch (result)
            {
                case ScheduledTaskTriggerResult.NotFound:
                    return NotFound(new { error = $"No scheduled task named '{taskName}' is running." });

                case ScheduledTaskTriggerResult.AlreadyRunning:
                    return Conflict(new { error = $"'{taskName}' is already running." });

                default:
                    var status = _scheduledTasks.Find(taskName);
                    return status is null
                        ? Accepted()
                        : Accepted(ScheduledTaskDto.FromStatus(status));
            }
        }
    }
}
