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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Downloads.Monitoring
{
    /// <summary>
    /// Failure handling for downloads the client reported Failed: blocklists the release (unless
    /// failed-download handling is off), removes the item from the client where appropriate, and
    /// runs an auto-search for the book if configured. Reached from TriggerCallbacks in the main
    /// file, and also how a stalled download (see DownloadMonitorService.StallDetection.cs) is
    /// retried once StallDetection has failed it.
    ///
    /// Kept beside the monitor rather than inside it because the monitor file is already at the
    /// size the architecture tests cap production files at.
    /// </summary>
    public partial class DownloadMonitorProcessor
    {
        private async Task OnDownloadFailed(
            Download download,
            DownloadClientConfiguration client,
            string errorMessage,
            CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var downloadClientGateway = scope.ServiceProvider.GetRequiredService<IDownloadClientGateway>();
            var audiobookRepository = scope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
            var downloadHistoryService = scope.ServiceProvider.GetRequiredService<IDownloadHistoryService>();
            var configurationService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var settings = await configurationService.GetApplicationSettingsAsync();
            var downloadService = scope.ServiceProvider.GetRequiredService<IDownloadService>();

            await downloadHistoryService.RecordDownloadFailedAsync(
                download.Id,
                download.DownloadClientId,
                download.Title ?? "Unknown",
                errorMessage);

            if (!settings.FailedDownloadHandlingEnabled)
            {
                return;
            }

            // Block the release before the auto-search below, so the search that follows a
            // failure cannot pick the same broken release straight back up.
            //
            // Below the FailedDownloadHandlingEnabled gate rather than above it. An operator who
            // has turned failed-download handling off has said they want failures left alone, and
            // a blocklist entry is durable state with no expiry: writing one anyway would
            // accumulate permanent bans that the setting gives no hint exist.
            //
            // Only downloads the client accepted and then failed reach this method. A
            // release the client refused at submission never gets here, which is what keeps
            // a qBittorrent 409 out of the blocklist: that answer means the client already
            // holds the release, so blocking it would ban something the user is currently
            // downloading. The carve-out is structural rather than a condition to remember.
            if (download.AudiobookId.HasValue)
            {
                var blocklistService = scope.ServiceProvider.GetRequiredService<IBlocklistService>();
                // Read back the identity stamped on the download when it was grabbed. This method
                // must not work one out for itself: by the time a download fails, its TotalSize
                // has been overwritten from the client's queue snapshot and its OriginalUrl may be
                // a spent per-fetch link, so anything derived here disagrees with what the search
                // side derives from the indexer's listing and the row never matches. A live
                // install wrote one correctly formatted row after the first failure and then
                // grabbed the identical release more than a hundred times over the next eleven
                // hours.
                var identifier = ReleaseIdentity.ForGrabbed(download);
                if (identifier is not null)
                {
                    // BlocklistService.BlockAsync only swallows the one expected race (see its own
                    // comment). Anything else it throws must not take the removal and auto-search
                    // below down with it; History for this failure is already written.
                    try
                    {
                        await blocklistService.BlockAsync(
                            download.AudiobookId.Value,
                            identifier,
                            download.Title ?? "Unknown",
                            download.ExpectedFileSize ?? (download.TotalSize > 0 ? download.TotalSize : null),
                            errorMessage);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException && exception is not OutOfMemoryException && exception is not StackOverflowException)
                    {
                        logger.LogError(exception, "Failed to blocklist release for failed download {DownloadId}; continuing with removal and auto-search", download.Id);
                    }
                }
            }

            var clientItemId = download.GetExternalId();
            // NZBGet history is part of failure diagnostics and final-path recovery.
            // Do not remove failed NZBGet history here; successful imports remove client
            // history through the post-import cleanup path.
            if (!string.IsNullOrWhiteSpace(clientItemId) &&
                ShouldRemoveFailedClientItem(client))
            {
                // Isolated for the same reason as the blocklist call above.
                try
                {
                    await downloadClientGateway.RemoveAsync(client, clientItemId, deleteFiles: false, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException && exception is not OutOfMemoryException && exception is not StackOverflowException)
                {
                    logger.LogError(exception, "Failed to remove failed download {DownloadId} from the download client; continuing with auto-search", download.Id);
                }
            }

            if (settings.FailedDownloadAutoSearch && download.AudiobookId.HasValue)
            {
                if (ShouldSuppressFailedDownloadAutoSearch(client, download, errorMessage))
                {
                    logger.LogInformation(
                        "Skipping immediate auto-search for failed NZBGet download {DownloadId}; client failure requires user action or manual retry",
                        download.Id);
                    return;
                }

                try
                {
                    var audiobook = await audiobookRepository.GetByIdAsync(download.AudiobookId!.Value);
                    if (audiobook != null && audiobook.Monitored)
                    {
                        await downloadService.SearchAndDownloadAsync(download.AudiobookId.Value);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    logger.LogWarning(ex, "Failed to auto-search after failed download {DownloadId}", download.Id);
                }
            }
        }

        internal static bool ShouldRemoveFailedClientItem(DownloadClientConfiguration client)
        {
            return !string.Equals(client.Type, "nzbget", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool ShouldSuppressFailedDownloadAutoSearch(
            DownloadClientConfiguration client,
            Download download,
            string errorMessage)
        {
            if (!string.Equals(client.Type, "nzbget", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var clientFailureReason = download.GetMetadataString("ClientFailureReason") ?? errorMessage;
            return NzbgetFailureMessageMapper.IsMoveOrPostProcessingFailure(clientFailureReason);
        }
    }
}
