
namespace Listenarr.Application.Mapping
{
    public class QueueItemConverter
    {
        public static Download UpdateFromQueueItem(Download download, QueueItem item)
        {
            if (!string.IsNullOrEmpty(item.LocalPath))
            {
                download.DownloadPath = item.LocalPath;
            }

            if (!string.IsNullOrEmpty(item.ContentPath))
            {
                download.SetMetadata("ClientContentPath", item.ContentPath);
            }

            download.SetMetadata("CanBeRemoved", item.CanRemove);

            var amountLeft = item.Size - item.Downloaded;
            download = MapDownloadProgress(download, item.Progress, amountLeft, item.Status);

            // Skip finalization/progress logic for downloads that are already
            // being processed, awaiting import, or fully imported. Re-entering
            // finalization for these would cause duplicate notifications and
            // potentially import the wrong files a second time.
            if (download.Status == DownloadStatus.Moved ||
                download.Status == DownloadStatus.Processing ||
                download.Status == DownloadStatus.ImportPending)
            {
                return download;
            }

            var normalizedState = (item.Status ?? string.Empty).ToLowerInvariant();
            if (normalizedState == "error" || normalizedState == "missingfiles")
            {
                download.Failed($"qBittorrent state: {item.Status}");
                return download;
            }

            if (item.Progress >= 100)
            {
                download.Completed();
            }

            return download;
        }

        /// <summary>
        /// Used for old adapter implementation
        /// Returns a download updated with the given values
        /// </summary>
        /// <param name="download"></param>
        /// <param name="progress"></param>
        /// <param name="amountLeft"></param>
        /// <param name="clientState"></param>
        /// <returns></returns>
        private static Download MapDownloadProgress(Download download, double progress, long amountLeft, string clientState)
        {
            var normalizedState = (clientState ?? string.Empty).ToLowerInvariant();

            // Map client state to our DownloadStatus
            var mappedStatus = normalizedState switch
            {
                "downloading" => DownloadStatus.Downloading,
                "metadl" => DownloadStatus.Downloading,
                "forceddl" => DownloadStatus.Downloading,
                "stalleddl" => DownloadStatus.Downloading,
                "checkingdl" => DownloadStatus.Downloading,
                "checkingresumedata" => DownloadStatus.Downloading,
                "moving" => DownloadStatus.Downloading,
                "fetching" => DownloadStatus.Downloading,
                "scanning" => DownloadStatus.Downloading,
                "pp_queued" => DownloadStatus.Downloading,
                "pp_processing" => DownloadStatus.Downloading,
                "uploading" => DownloadStatus.Downloading,
                "stalledup" => DownloadStatus.Downloading,
                "checkingup" => DownloadStatus.Downloading,
                "forcedup" => DownloadStatus.Downloading,
                "stoppeddl" => DownloadStatus.Paused,
                "stoppedup" => DownloadStatus.Paused,
                "queueddl" => DownloadStatus.Queued,
                "queuedup" => DownloadStatus.Queued,
                "queued" => DownloadStatus.Queued,
                "paused" => DownloadStatus.Paused,
                "seeding" => DownloadStatus.Downloading,
                "success" => DownloadStatus.Completed,
                "error" => DownloadStatus.Failed,
                "failed" => DownloadStatus.Failed,
                "failure" => DownloadStatus.Failed,
                "missingfiles" => DownloadStatus.Failed,
                "missing_files" => DownloadStatus.Failed,
                _ => DownloadStatus.Queued
            };

            // Update download record
            download.Progress = (decimal)progress;
            download.DownloadedSize = download.TotalSize - amountLeft;
            download.Metadata ??= new Dictionary<string, object>();
            download.Metadata!["ClientState"] = clientState ?? "Unknown";
            download.Metadata!["AmountLeft"] = amountLeft;

            // Conservative guard: if the DB record is currently Failed, do not overwrite
            // the status to a non-failed value unless we have strong evidence (progress increased)
            // or the client reports Completed. This prevents transient client "error" states
            // from flipping the UI incorrectly.
            if (download.Status == DownloadStatus.Failed && mappedStatus != DownloadStatus.Failed)
            {
                var incomingProgress = (decimal)progress;

                // Allow transition to Completed always (finalization or client reports complete)
                if (mappedStatus == DownloadStatus.Completed)
                {
                    download.Completed();
                }
                else if (incomingProgress > download.Progress)
                {
                    download.Downloading();
                }
            }
            else if (download.Status != DownloadStatus.Completed && download.Status != DownloadStatus.Moved)
            {
                // Don't overwrite Completed/Moved status - Completed is managed by the completion
                // detection logic, and Moved means the file is already imported (we only keep
                // polling Moved downloads to update CanBeRemoved for deferred client removal).
                download.SetStatus(mappedStatus);
            }

            return download;
        }
    }
}
