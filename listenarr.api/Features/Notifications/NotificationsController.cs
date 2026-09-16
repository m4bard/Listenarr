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

using Listenarr.Api.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Notifications
{
    [ApiController]
    [Route("api/v{version:apiVersion}/notifications")]
    [RequireAdminOrApiKey]
    [Tags("Notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly IEnumerable<INotificationSubscriber> _subscribers;

        public NotificationsController(IEnumerable<INotificationSubscriber> subscribers)
        {
            _subscribers = subscribers;
        }

        /// <summary>
        /// Exercise one configured subscriber instance and report whether it works.
        /// </summary>
        /// <remarks>
        /// This runs the real target: a custom script is executed, with the event type set to Test.
        /// A script is expected to handle that without side effects.
        /// </remarks>
        [HttpPost("subscribers/{subscriberName}/test/{configurationId}")]
        public async Task<ActionResult<object>> TestSubscriber(
            string subscriberName,
            string configurationId,
            CancellationToken cancellationToken)
        {
            var subscriber = _subscribers.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, subscriberName, StringComparison.OrdinalIgnoreCase));

            if (subscriber == null)
            {
                return NotFound(new { success = false, message = $"No notification subscriber named '{subscriberName}'" });
            }

            var result = await subscriber.TestAsync(configurationId, cancellationToken);

            return result.IsValid
                ? Ok(new { success = true, message = $"{subscriber.Name} test succeeded" })
                : BadRequest(new { success = false, message = $"{subscriber.Name} test failed", failures = result.Failures });
        }
    }
}
