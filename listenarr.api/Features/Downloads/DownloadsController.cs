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
using Microsoft.Extensions.Caching.Memory;

namespace Listenarr.Api.Features.Downloads;

[ApiController]
[Route("api/v{version:apiVersion}/downloads")]
[Tags("Downloads")]
public class DownloadsController : ControllerBase
{
    private readonly IDownloadRepository _downloadRepository;
    private readonly IDownloadService _downloadService;
    private readonly ILogger<DownloadsController> _logger;
    private readonly IConfigurationService _configurationService;
    private readonly IMemoryCache? _cache;

    public DownloadsController(IDownloadRepository downloadRepository, IDownloadService downloadService, ILogger<DownloadsController> logger, IConfigurationService configurationService, IMemoryCache? cache = null)
    {
        _downloadRepository = downloadRepository;
        _downloadService = downloadService;
        _logger = logger;
        _configurationService = configurationService;
        _cache = cache;
    }
    /// <summary>
    /// Retrieve cached torrent bytes (if cached) for a given download id (synchronous for tests)
    /// </summary>
    [NonAction]
    public IActionResult GetCachedTorrent(string downloadId)
    {
        if (_cache == null)
        {
            return NotFound(new { error = "Cached torrent not found", downloadId });
        }

        if (_cache.TryGetValue($"mam:cachedtorrent:{downloadId}:bytes", out byte[]? bytes) && bytes != null && bytes.Length > 0)
        {
            var fileName = _cache.Get<string>($"mam:cachedtorrent:{downloadId}:name") ?? "download.torrent";
            return new FileContentResult(bytes, "application/x-bittorrent") { FileDownloadName = fileName };
        }

        return NotFound(new { error = "Cached torrent not found", downloadId });
    }

    /// <summary>
    /// Retrieve cached announce URLs (sync for tests)
    /// </summary>
    [NonAction]
    public IActionResult GetCachedAnnounces(string downloadId)
    {
        if (_cache == null)
            return NotFound(new { error = "Cached announces not found", downloadId });

        if (_cache.TryGetValue($"mam:cachedtorrent:{downloadId}:announces", out List<string>? announces) && announces != null && announces.Count > 0)
        {
            return Ok(new { downloadId, announces });
        }

        return NotFound(new { error = "Cached announces not found", downloadId });
    }

    /// <summary>
    /// List all download records, optionally filtered by status.
    /// </summary>
    /// <param name="status">Optional status filter (e.g., Queued, Downloading, Completed, Failed).</param>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Download>>> GetDownloads([FromQuery] string? status = null)
    {
        try
        {
            var downloadClients = await _configurationService.GetDownloadClientConfigurationsAsync();
            var enabledClientIds = downloadClients
                .Where(c => c.IsEnabled && !string.IsNullOrWhiteSpace(c.Id))
                .Select(c => c.Id)
                .ToList();

            var all = await _downloadRepository.GetAllAsync();
            var filtered = all.Where(d =>
                d.DownloadClientId == "DDL" ||
                (!string.IsNullOrEmpty(d.DownloadClientId) && enabledClientIds.Contains(d.DownloadClientId)));

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<DownloadStatus>(status, true, out var parsedStatus))
            {
                filtered = filtered.Where(d => d.Status == parsedStatus);
            }

            var downloads = filtered
                .OrderByDescending(d => d.StartedAt)
                .ToList();

            var enhancedDownloads = await EnhanceDownloadsWithClientNames(downloads);

            _logger.LogInformation("Retrieved {Count} downloads", downloads.Count);
            return Ok(enhancedDownloads);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger.LogError(ex, "Error retrieving downloads");
            return StatusCode(500, new { error = "Failed to retrieve downloads", message = ex.Message });
        }
    }

    /// <summary>
    /// Get a specific download record by ID.
    /// </summary>
    /// <param name="id">Download record ID.</param>
    [HttpGet("{id}")]
    public async Task<ActionResult<Download>> GetDownload(string id)
    {
        try
        {
            var download = await _downloadRepository.FindAsync(id);

            if (download == null)
            {
                return NotFound(new { error = "Download not found", id });
            }

            // Remove downloadPath before returning to client
            var downloadObj = new
            {
                id = download.Id,
                audiobookId = download.AudiobookId,
                title = download.Title,
                artist = download.Artist,
                album = download.Album,
                originalUrl = download.OriginalUrl,
                status = download.Status.ToString(),
                progress = download.Progress,
                totalSize = download.TotalSize,
                downloadedSize = download.DownloadedSize,
                finalPath = download.FinalPath,
                startedAt = download.StartedAt,
                completedAt = download.CompletedAt,
                errorMessage = download.ErrorMessage,
                downloadClientId = download.DownloadClientId,
                metadata = download.Metadata,
                importBlockReason = download.ImportBlockReason,
                importBlockMessages = download.ImportBlockMessages,
                importAttempts = download.ImportAttempts
            };

            return Ok(downloadObj);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger.LogError(ex, "Error retrieving download {DownloadId}", LogRedaction.SanitizeText(id));
            return StatusCode(500, new { error = "Failed to retrieve download", message = ex.Message });
        }
    }

    /// <summary>
    /// Retry importing a download that was blocked due to import issues. Resets status to ImportPending.
    /// </summary>
    /// <param name="id">Download record ID.</param>
    [HttpPost("{id}/retry-import")]
    public async Task<ActionResult> RetryBlockedImport(string id)
    {
        try
        {
            var download = await _downloadRepository.FindAsync(id);
            if (download == null)
            {
                return NotFound(new { error = "Download not found", id });
            }

            if (download.Status != DownloadStatus.ImportBlocked)
            {
                return BadRequest(new
                {
                    error = "Download is not import blocked",
                    id,
                    status = download.Status.ToString()
                });
            }

            download.Unblock();

            await _downloadService.UpdateAsync(download);

            _logger.LogInformation("Reset blocked import {DownloadId} back to ImportPending", LogRedaction.SanitizeText(id));
            return Ok(new
            {
                message = "Import retry queued",
                id,
                status = download.Status.ToString()
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger.LogError(ex, "Error retrying blocked import {DownloadId}", id);
            return StatusCode(500, new { error = "Failed to retry blocked import", message = ex.Message });
        }
    }

    /// <summary>
    /// Get all active downloads (Queued, Downloading, Processing, or ImportPending status).
    /// </summary>
    [HttpGet("active")]
    public async Task<ActionResult<IEnumerable<Download>>> GetActiveDownloads()
    {
        try
        {
            var downloadClients = await _configurationService.GetDownloadClientConfigurationsAsync();
            var enabledClientIds = downloadClients
                .Where(c => c.IsEnabled && !string.IsNullOrWhiteSpace(c.Id))
                .Select(c => c.Id)
                .ToList();

            var allDownloads = await _downloadRepository.GetAllAsync();
            var activeDownloads = allDownloads
                .Where(d => d.Status == DownloadStatus.Queued ||
                           d.Status == DownloadStatus.Downloading ||
                           d.Status == DownloadStatus.Processing ||
                           d.Status == DownloadStatus.ImportPending)
                .Where(d =>
                    d.DownloadClientId == "DDL" ||
                    (!string.IsNullOrEmpty(d.DownloadClientId) && enabledClientIds.Contains(d.DownloadClientId)))
                .OrderByDescending(d => d.StartedAt)
                .ToList();

            var enhancedActiveDownloads = await EnhanceDownloadsWithClientNames(activeDownloads);

            _logger.LogInformation("Retrieved {Count} active downloads", activeDownloads.Count);
            return Ok(enhancedActiveDownloads);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger.LogError(ex, "Error retrieving active downloads");
            return StatusCode(500, new { error = "Failed to retrieve active downloads", message = ex.Message });
        }
    }


    /// <summary>
    /// Delete a download record and, by default, remove the download from its download client.
    /// Pass removeFromClient=false to delete only the database record and leave the client alone.
    /// </summary>
    /// <param name="id">Download record ID.</param>
    /// <param name="removeFromClient">
    /// When true, the default, the item is also removed from the download client and the record is
    /// kept if the client does not confirm the removal. When false only the database record goes.
    /// </param>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteDownload(string id, [FromQuery] bool removeFromClient = true)
    {
        try
        {
            var download = await _downloadRepository.FindAsync(id);

            if (download == null)
            {
                return NotFound(new { error = "Download not found", id });
            }

            var removed = await RemoveDownloadAsync(download, removeFromClient);

            if (!removed)
            {
                _logger.LogWarning("Kept download record {DownloadId} because the download client did not confirm removal", LogRedaction.SanitizeText(id));
                return Conflict(new
                {
                    error = "Download client removal failed",
                    message = "The download client did not confirm removal, so the record was kept. Retry with removeFromClient=false to delete the record only.",
                    id
                });
            }

            _logger.LogInformation("Deleted download record {DownloadId} (removeFromClient: {RemoveFromClient})", LogRedaction.SanitizeText(id), removeFromClient);
            return Ok(new { message = "Download deleted successfully", id, removeFromClient });
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger.LogError(ex, "Error deleting download {DownloadId}", LogRedaction.SanitizeText(id));
            return StatusCode(500, new { error = "Failed to delete download", message = ex.Message });
        }
    }

    /// <summary>
    /// Delete all download records with Completed status, by default removing each one from its
    /// download client as well.
    /// </summary>
    /// <param name="removeFromClient">
    /// When true, the default, each item is also removed from its download client. Records the
    /// client would not confirm are kept and listed in the response.
    /// </param>
    [HttpDelete("completed")]
    public async Task<ActionResult> ClearCompletedDownloads([FromQuery] bool removeFromClient = true)
    {
        try
        {
            var all = await _downloadRepository.GetAllAsync();
            var completedDownloads = all.Where(d => d.Status == DownloadStatus.Completed).ToList();
            var (removedIds, keptIds) = await RemoveDownloadsAsync(completedDownloads, removeFromClient);

            _logger.LogInformation("Cleared {Count} completed downloads, kept {KeptCount} the download client would not confirm", removedIds.Count, keptIds.Count);
            return Ok(new
            {
                message = "Completed downloads cleared",
                count = removedIds.Count,
                kept = keptIds.Count,
                keptIds,
                removeFromClient
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger.LogError(ex, "Error clearing completed downloads");
            return StatusCode(500, new { error = "Failed to clear completed downloads", message = ex.Message });
        }
    }

    /// <summary>
    /// Delete all download records with Failed or ImportBlocked status, by default removing each one
    /// from its download client as well.
    /// </summary>
    /// <param name="removeFromClient">
    /// When true, the default, each item is also removed from its download client. Records the
    /// client would not confirm are kept and listed in the response.
    /// </param>
    [HttpDelete("failed")]
    public async Task<ActionResult> ClearFailedDownloads([FromQuery] bool removeFromClient = true)
    {
        try
        {
            var all = await _downloadRepository.GetAllAsync();
            var failedDownloads = all.Where(d => d.Status == DownloadStatus.Failed || d.Status == DownloadStatus.ImportBlocked).ToList();
            var (removedIds, keptIds) = await RemoveDownloadsAsync(failedDownloads, removeFromClient);

            _logger.LogInformation("Cleared {Count} failed downloads, kept {KeptCount} the download client would not confirm", removedIds.Count, keptIds.Count);
            return Ok(new
            {
                message = "Failed downloads cleared",
                count = removedIds.Count,
                kept = keptIds.Count,
                keptIds,
                removeFromClient
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger.LogError(ex, "Error clearing failed downloads");
            return StatusCode(500, new { error = "Failed to clear failed downloads", message = ex.Message });
        }
    }

    /// <summary>
    /// Remove one download through the shared removal workflow, which contacts the download client
    /// and only drops the database record once the client side is settled. The workflow's force flag
    /// is the record-only path, so it is exactly the opt-out that removeFromClient=false asks for.
    /// </summary>
    private async Task<bool> RemoveDownloadAsync(Download download, bool removeFromClient)
    {
        // An empty client id means the record never recorded which client holds the item. Passing
        // null rather than the empty string lets the workflow sweep every enabled client for it.
        var downloadClientId = string.IsNullOrWhiteSpace(download.DownloadClientId)
            ? null
            : download.DownloadClientId;

        return await _downloadService.RemoveFromQueueAsync(download.Id, downloadClientId, force: !removeFromClient);
    }

    /// <summary>
    /// Remove a set of downloads one at a time, reporting per item rather than per sweep. One client
    /// refusing must not abort the rest of the clear, and must not take the record with it either.
    /// </summary>
    private async Task<(List<string> RemovedIds, List<string> KeptIds)> RemoveDownloadsAsync(IEnumerable<Download> downloads, bool removeFromClient)
    {
        var removedIds = new List<string>();
        var keptIds = new List<string>();

        foreach (var download in downloads)
        {
            bool removed;

            try
            {
                removed = await RemoveDownloadAsync(download, removeFromClient);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Error removing download {DownloadId} during bulk clear", LogRedaction.SanitizeText(download.Id));
                removed = false;
            }

            if (removed)
            {
                removedIds.Add(download.Id);
            }
            else
            {
                _logger.LogWarning("Kept download record {DownloadId} because the download client did not confirm removal", LogRedaction.SanitizeText(download.Id));
                keptIds.Add(download.Id);
            }
        }

        return (removedIds, keptIds);
    }

    /// <summary>
    /// Enhance downloads with resolved client names
    /// </summary>
    private async Task<List<object>> EnhanceDownloadsWithClientNames(List<Download> downloads)
    {
        var downloadClients = await _configurationService.GetDownloadClientConfigurationsAsync();
        var clientLookup = downloadClients.ToDictionary(c => c.Id, c => c.Name);

        return downloads.Select(d =>
        {
            // Remove any client-local content path information before returning to the frontend.
            // Server keeps `DownloadPath`/metadata internally for mapping/monitoring, but must not transmit
            // client-local paths (for example ClientContentPath) to user browsers.
            object? sanitizedMetadata = null;
            if (d.Metadata != null)
            {
                var dict = new Dictionary<string, object>();
                foreach (var kvp in d.Metadata.Where(kvp => !string.Equals(kvp.Key, "ClientContentPath", StringComparison.OrdinalIgnoreCase)))
                {
                    dict[kvp.Key] = kvp.Value!;
                }
                sanitizedMetadata = dict;
            }

            return new
            {
                id = d.Id,
                audiobookId = d.AudiobookId,
                title = d.Title,
                artist = d.Artist,
                album = d.Album,
                originalUrl = d.OriginalUrl,
                status = d.Status.ToString(),
                progress = d.Progress,
                totalSize = d.TotalSize,
                downloadedSize = d.DownloadedSize,
                finalPath = d.FinalPath,
                startedAt = d.StartedAt,
                completedAt = d.CompletedAt,
                errorMessage = d.ErrorMessage,
                downloadClientId = d.DownloadClientId,
                downloadClientName = d.DownloadClientId == "DDL" ? "Direct Download" :
                                   clientLookup.TryGetValue(d.DownloadClientId, out var clientName) ? clientName : "Unknown Client",
                metadata = sanitizedMetadata,
                // Sprint 2: Error handling and import blocking fields
                importBlockReason = d.ImportBlockReason,
                importBlockMessages = d.ImportBlockMessages,
                importAttempts = d.ImportAttempts
            };
        }).Cast<object>().ToList();
    }
}
