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

namespace Listenarr.Api.Features.Downloads;

public partial class DownloadsController
{
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
