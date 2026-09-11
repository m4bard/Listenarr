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
using Microsoft.Extensions.Caching.Memory;

namespace Listenarr.Api.Features.Library;

/// <summary>
/// Transport adapter over the refresh coordinator. Holds no state: the run registry is the
/// coordinator's, and a restart legitimately loses it.
/// </summary>
public sealed class LibraryMetadataRefreshWorkflow
{
    /// <summary>
    /// The same fifteen seconds the per-book rescan uses, for the same reason and in the same
    /// shape.
    /// </summary>
    /// <remarks>
    /// The 409 the coordinator returns for a run already in flight is not a substitute. A run
    /// that finds nothing due finishes in milliseconds, so a held button, a double click or a
    /// browser retry starts run after run, each one asking the database for the due set and each
    /// one admitted because the last had already ended.
    /// </remarks>
    private const int MetadataRefreshTriggerCooldownSeconds = 15;

    private readonly IMetadataRefreshCoordinator _coordinator;
    private readonly IConfigurationService _configuration;
    private readonly IMemoryCache? _memoryCache;

    public LibraryMetadataRefreshWorkflow(
        IMetadataRefreshCoordinator coordinator,
        IConfigurationService configuration,
        IMemoryCache? memoryCache = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _memoryCache = memoryCache;
    }

    public async Task<IActionResult> StartAsync(
        MetadataRefreshRequest? request,
        HttpContext? httpContext,
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

        // Per actor and per scope, so one person hammering the library button does not lock
        // another out, and an author refresh is not blocked by a library one a moment earlier.
        if (_memoryCache != null &&
            !TryConsumeTriggerCooldown(_memoryCache, httpContext, effective.AuthorId, out var retryAfterSeconds))
        {
            SetRetryAfter(httpContext, retryAfterSeconds);
            return new ObjectResult(new
            {
                message = $"Metadata refresh cooldown active. Please wait {retryAfterSeconds} seconds before starting another refresh.",
                retryAfterSeconds
            })
            {
                StatusCode = StatusCodes.Status429TooManyRequests
            };
        }

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

    /// <summary>
    /// Takes this actor's slot for this scope, or reports how long is left on it. Shaped like
    /// the per-book rescan's quota: a memory-cache entry per actor, a fixed cooldown, and a
    /// whole number of seconds for Retry-After.
    /// </summary>
    private static bool TryConsumeTriggerCooldown(
        IMemoryCache cache,
        HttpContext? httpContext,
        int? authorId,
        out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;

        var actorKey = LibraryRequestActorKey.Build(httpContext);
        var scopeKey = authorId.HasValue ? $"author:{authorId.Value}" : "library";
        var cacheKey = $"metadata-refresh-trigger-rate:{scopeKey}:{actorKey}";
        var now = DateTime.UtcNow;
        var cooldown = TimeSpan.FromSeconds(MetadataRefreshTriggerCooldownSeconds);

        if (cache.TryGetValue(cacheKey, out DateTime lastAttemptUtc))
        {
            var remaining = cooldown - (now - lastAttemptUtc);
            if (remaining > TimeSpan.Zero)
            {
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
                return false;
            }
        }

        cache.Set(
            cacheKey,
            now,
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = cooldown });

        return true;
    }

    private static void SetRetryAfter(HttpContext? httpContext, int retryAfterSeconds)
    {
        try
        {
            if (httpContext != null)
            {
                httpContext.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            // The body carries the same number. A header that could not be set is not worth
            // turning a 429 into a 500 over.
            _ = ex;
        }
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
