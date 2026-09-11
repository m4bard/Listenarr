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

using Listenarr.Application.Common.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library;

/// <summary>
/// Transport adapter over the refresh coordinator. Holds no state: the run registry is the
/// coordinator's, and a restart legitimately loses it.
/// </summary>
public sealed class LibraryMetadataRefreshWorkflow
{
    private readonly IMetadataRefreshCoordinator _coordinator;
    private readonly IConfigurationService _configuration;

    public LibraryMetadataRefreshWorkflow(
        IMetadataRefreshCoordinator coordinator,
        IConfigurationService configuration)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public async Task<IActionResult> StartAsync(
        MetadataRefreshRequest? request,
        CancellationToken cancellationToken)
    {
        // The setting gated the scheduled walk and nothing else, so an operator who turned the
        // feature off still had a button that started a library-wide run. Refused here rather
        // than inside the coordinator so the caller is told which of the two 409s it got.
        var settings = await _configuration.GetApplicationSettingsAsync();
        if (!settings.MetadataRefreshEnabled)
        {
            return new ConflictObjectResult(new
            {
                message = "Metadata refresh is turned off in settings.",
                code = "metadata_refresh_disabled"
            });
        }

        var effective = request ?? new MetadataRefreshRequest();
        var scope = effective.AuthorId.HasValue
            ? MetadataRefreshRunScope.Author
            : MetadataRefreshRunScope.Library;

        MetadataRefreshStartResult started;
        try
        {
            started = await _coordinator.StartAsync(
                new MetadataRefreshScopeRequest(scope, effective.AuthorId, effective.Force),
                cancellationToken);
        }
        catch (ApplicationNotFoundException exception)
        {
            return new NotFoundObjectResult(new
            {
                message = exception.SafeDetail,
                code = exception.Code
            });
        }

        var body = new MetadataRefreshRunResponse(
            started.Run.RunId,
            started.Run.Scope,
            started.Run.TotalBooks,
            started.Run.Status);

        return started.Started
            ? new AcceptedResult(string.Empty, body)
            : new ConflictObjectResult(body);
    }

    public IActionResult GetStatus(Guid runId)
    {
        var run = _coordinator.Find(runId);
        return run == null
            ? new NotFoundObjectResult(new { message = "No metadata refresh run with that id." })
            : new OkObjectResult(Project(run));
    }

    /// <summary>The active run, or the most recent one this process ran.</summary>
    public IActionResult GetLatest()
    {
        var run = _coordinator.Current();
        return run == null
            ? new NotFoundObjectResult(new { message = "No metadata refresh has run since startup." })
            : new OkObjectResult(Project(run));
    }

    /// <summary>
    /// Asks a run to stop. Cancellation is cooperative and the book boundary is the cancellation
    /// point, so a book already inside its exclusive section finishes.
    /// </summary>
    public IActionResult Cancel(Guid runId)
    {
        return _coordinator.Cancel(runId)
            ? new AcceptedResult(string.Empty, new { message = "Metadata refresh cancelling", runId })
            : new NotFoundObjectResult(new { message = "No active metadata refresh run with that id." });
    }

    internal static MetadataRefreshRunStatusResponse Project(MetadataRefreshRunSnapshot run) => new(
        run.RunId,
        run.Scope,
        run.Status,
        run.TotalBooks,
        run.Processed,
        run.Updated,
        run.Skipped,
        run.Deferred,
        run.Failed,
        run.RequestsSpent,
        run.StartedAt,
        run.CompletedAt);
}
