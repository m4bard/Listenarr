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
using Listenarr.Application.Search.Discovery;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Discovery
{
    [ApiController]
    [Route("api/v{version:apiVersion}/discovery")]
    [Tags("Discovery")]
    public class DiscoveryController(IDiscoveryStatusService statusService) : ControllerBase
    {
        /// <summary>Recent-release discovery status for every indexer with RSS enabled.</summary>
        [HttpGet("status")]
        public async Task<ActionResult<IReadOnlyList<DiscoveryIndexerStatusReport>>> GetStatus(CancellationToken cancellationToken) =>
            Ok(await statusService.GetStatusAsync(cancellationToken));

        /// <summary>Acknowledges a coverage gap; acknowledging it again changes nothing.</summary>
        [HttpPost("gaps/{id:int}/acknowledge")]
        public async Task<IActionResult> AcknowledgeGap(int id, CancellationToken cancellationToken) =>
            await statusService.AcknowledgeGapAsync(id, cancellationToken) ? NoContent() : NotFound();
    }
}
