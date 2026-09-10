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

namespace Listenarr.Api.Features.Library
{
    public sealed partial class LibraryMetadataRescanWorkflow
    {
        private const int MetadataRescanCooldownSeconds = 15;
        private const int MetadataRescanWindowMinutes = 10;
        private const int MetadataRescanMaxRequestsPerWindow = 5;

        private readonly IMetadataRefreshService _refreshService;
        private readonly ILogger<LibraryMetadataRescanWorkflow> _logger;
        private readonly IMemoryCache? _memoryCache;

        public LibraryMetadataRescanWorkflow(
            IMetadataRefreshService refreshService,
            ILogger<LibraryMetadataRescanWorkflow> logger,
            IMemoryCache? memoryCache = null)
        {
            _refreshService = refreshService ?? throw new ArgumentNullException(nameof(refreshService));
            _logger = logger;
            _memoryCache = memoryCache;
        }

        public async Task<IActionResult> RescanAsync(int id, HttpContext httpContext)
        {
            var cancellationToken = httpContext.RequestAborted;
            cancellationToken.ThrowIfCancellationRequested();

            if (_memoryCache != null &&
                !TryConsumeMetadataRescanQuota(_memoryCache, httpContext, id, out var rateLimitMessage, out var retryAfterSeconds))
            {
                try
                {
                    httpContext.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    _logger.LogDebug(ex, "Failed to set Retry-After header for metadata rescan rate-limit response");
                }

                return new ObjectResult(new
                {
                    message = rateLimitMessage,
                    retryAfterSeconds
                })
                {
                    StatusCode = StatusCodes.Status429TooManyRequests
                };
            }

            MetadataRefreshResult result;
            try
            {
                result = await _refreshService.RefreshAsync(id, new UnlimitedMetadataRefreshBudget(), cancellationToken);
            }
            catch (ApplicationConflictException exception)
            {
                // A blocked filesystem mutation names its own code and detail, which the refresh
                // outcomes cannot carry. The endpoint has always reported that verbatim.
                return new ConflictObjectResult(new
                {
                    message = exception.SafeDetail,
                    code = exception.Code
                });
            }

            return result.Outcome switch
            {
                MetadataRefreshOutcome.Updated => new OkObjectResult(new
                {
                    message = "Metadata rescanned successfully",
                    audiobookId = id,
                    source = result.Source,
                    asin = result.Asin,
                    region = result.Region
                }),
                MetadataRefreshOutcome.Skipped => new BadRequestObjectResult(new
                {
                    message = "No ASIN or ISBN identifiers are available for metadata rescan."
                }),
                MetadataRefreshOutcome.NotFound => new NotFoundObjectResult(new
                {
                    message = "No metadata found using the available identifiers."
                }),
                MetadataRefreshOutcome.Conflict => new ConflictObjectResult(new
                {
                    message = "The audiobook metadata changed during the rescan. Refresh and try again.",
                    code = "audiobook_metadata_changed"
                }),
                MetadataRefreshOutcome.Deferred => new ObjectResult(new
                {
                    message = "The metadata provider is not answering. Try again shortly.",
                    code = "metadata_provider_unavailable"
                })
                {
                    StatusCode = StatusCodes.Status503ServiceUnavailable
                },
                _ => new NotFoundObjectResult(new { message = "Audiobook not found" })
            };
        }
    }

    /// <summary>
    /// The one-book endpoint is throttled per actor and per book by the cooldown above, not by the
    /// run budget, so it grants every request and counts them for the response only.
    /// </summary>
    internal sealed class UnlimitedMetadataRefreshBudget : IMetadataRefreshBudget
    {
        private int _spent;

        public int RequestsSpent => _spent;

        public Task<bool> ChargeAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _spent);
            return Task.FromResult(true);
        }

        public void ApplyThrottleSignal(TimeSpan? retryAfter)
        {
        }
    }
}
