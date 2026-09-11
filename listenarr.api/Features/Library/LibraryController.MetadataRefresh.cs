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

using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library;

public partial class LibraryController
{
    /// <summary>
    /// Start a throttled provider-metadata refresh. Omit the author id for the whole library.
    /// </summary>
    /// <param name="request">Author id and force flag.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>
    /// Accepted with a run id, Conflict naming the run already in flight, or TooManyRequests
    /// while this caller's cooldown is still running.
    /// </returns>
    [HttpPost("refresh-metadata")]
    public async Task<IActionResult> StartMetadataRefresh(
        [FromBody] MetadataRefreshRequest? request,
        CancellationToken cancellationToken = default)
    {
        return await _metadataRefreshWorkflow.StartAsync(request, HttpContext, cancellationToken);
    }

    /// <summary>Get the progress of a metadata refresh run.</summary>
    /// <param name="runId">The run id returned when the refresh was accepted.</param>
    [HttpGet("refresh-metadata/{runId:guid}")]
    public IActionResult GetMetadataRefreshRun(Guid runId)
    {
        return _metadataRefreshWorkflow.GetStatus(runId);
    }

    /// <summary>Get the active metadata refresh run, or the most recent one.</summary>
    [HttpGet("refresh-metadata")]
    public IActionResult GetActiveMetadataRefreshRun()
    {
        return _metadataRefreshWorkflow.GetLatest();
    }

    /// <summary>Cancel a metadata refresh run.</summary>
    /// <param name="runId">The run id returned when the refresh was accepted.</param>
    [HttpDelete("refresh-metadata/{runId:guid}")]
    public IActionResult CancelMetadataRefreshRun(Guid runId)
    {
        return _metadataRefreshWorkflow.Cancel(runId);
    }
}
