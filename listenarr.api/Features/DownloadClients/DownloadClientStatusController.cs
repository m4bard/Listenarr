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

namespace Listenarr.Api.Features.DownloadClients
{
    /// <summary>
    /// One client's failure status as the settings page reads it. Times are UTC and carry the
    /// designator on the wire.
    /// </summary>
    public sealed record DownloadClientStatusResponse(
        string ClientId,
        int EscalationLevel,
        DateTime? InitialFailure,
        DateTime? MostRecentFailure,
        DateTime? DisabledTill,
        bool IsBlocked);

    /// <summary>
    /// Download client failure status. A separate route from the client list so the list keeps its
    /// shape, and so a status that changes on every failed poll is not mixed into the
    /// configuration the settings form edits and posts back.
    /// </summary>
    [ApiController]
    [Route("api/v{version:apiVersion}/download-clients/status")]
    [RequireAdminOrApiKey]
    [Tags("Download Clients")]
    public class DownloadClientStatusController(IDownloadClientStatusService statusService) : ControllerBase
    {
        /// <summary>
        /// Every client that has failed recently. A client with no entry has never failed or has
        /// fully recovered.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(List<DownloadClientStatusResponse>), 200)]
        public async Task<ActionResult<List<DownloadClientStatusResponse>>> GetStatuses(CancellationToken ct)
        {
            var statuses = await statusService.GetStatusesAsync(ct);
            return Ok(statuses
                .Select(s => new DownloadClientStatusResponse(
                    s.ClientId,
                    s.EscalationLevel,
                    AsUtc(s.InitialFailure),
                    AsUtc(s.MostRecentFailure),
                    AsUtc(s.DisabledTill),
                    s.IsBlocked))
                .ToList());
        }

        private static DateTime? AsUtc(DateTime? value) =>
            value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
    }
}
