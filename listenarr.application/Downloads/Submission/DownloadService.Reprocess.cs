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

using Listenarr.Application.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Downloads.Submission
{
    /// <summary>
    /// Reprocessing: putting a completed download back through the processing pipeline.
    ///
    /// Split out of DownloadService.cs to keep that file under the size the architecture tests
    /// enforce, and because these three methods share a shape that the rest of the service does not.
    /// </summary>
    public partial class DownloadService
    {
        /// <summary>
        /// Statuses a download can be reprocessed from: it finished downloading, so there are files
        /// to work with. Anything still in flight is excluded, because enqueuing a second job for it
        /// would race the one already running.
        /// </summary>
        private static bool IsReprocessable(DownloadStatus status) =>
            status is DownloadStatus.Completed or DownloadStatus.Moved;

        public async Task<string?> ReprocessDownloadAsync(string downloadId)
        {
            logger.LogInformation("ReprocessDownloadAsync called for {DownloadId}", LogRedaction.SanitizeText(downloadId));

            var download = await downloadRepository.FindAsync(downloadId);
            if (download == null)
            {
                logger.LogWarning("Reprocess requested for unknown download {DownloadId}", LogRedaction.SanitizeText(downloadId));
                return null;
            }

            // EnqueueAsync has its own precondition and throws InvalidOperationException for a
            // download that is not Completed. Without this check that exception escapes to the
            // controller as an unhandled 500, and the request is retried, so an ineligible download
            // produced a crash rather than a refusal. The batch path already checked; this one did
            // not, which is the whole of the defect.
            if (!IsReprocessable(download.Status))
            {
                logger.LogInformation(
                    "Reprocess refused for {DownloadId}: status is {Status}, not Completed or Moved",
                    LogRedaction.SanitizeText(downloadId),
                    download.Status);
                return null;
            }

            return await downloadProcessingJobService.EnqueueAsync(download);
        }

        public async Task<List<ReprocessResult>> ReprocessDownloadsAsync(List<string> downloadIds)
        {
            logger.LogInformation("ReprocessDownloadsAsync called for {Count} downloads", downloadIds?.Count ?? 0);

            var results = new List<ReprocessResult>();
            foreach (var downloadId in downloadIds ?? new List<string>())
            {
                var download = await downloadRepository.FindAsync(downloadId);
                if (download == null)
                {
                    results.Add(ReprocessResult.FromFailure(downloadId, "not-found"));
                    continue;
                }

                if (!IsReprocessable(download.Status))
                {
                    results.Add(ReprocessResult.FromFailure(downloadId, "not-completed", download.Status.ToString()));
                    continue;
                }

                try
                {
                    // One failure does not abandon the rest of the batch, and it is reported against
                    // the download it belongs to rather than as a failure of the whole call.
                    var jobId = await downloadProcessingJobService.EnqueueAsync(download);
                    results.Add(ReprocessResult.FromSuccess(downloadId, jobId));
                }
                catch (Exception exception) when (exception is not OperationCanceledException
                    && exception is not OutOfMemoryException
                    && exception is not StackOverflowException)
                {
                    logger.LogError(exception, "Failed to enqueue reprocess job for {DownloadId}", LogRedaction.SanitizeText(downloadId));
                    results.Add(ReprocessResult.FromFailure(downloadId, "enqueue-failed", exception.Message));
                }
            }

            return results;
        }

        public async Task<List<ReprocessResult>> ReprocessAllCompletedDownloadsAsync(bool includeProcessed = false, TimeSpan? maxAge = null)
        {
            logger.LogInformation("ReprocessAllCompletedDownloadsAsync called includeProcessed={IncludeProcessed}, maxAge={MaxAge}", includeProcessed, maxAge);

            // Age is measured from when the download finished, falling back to when it started for
            // rows old enough to predate CompletedAt being set.
            var cutoff = DateTime.UtcNow - (maxAge ?? TimeSpan.FromDays(30));

            var all = await downloadRepository.GetAllAsync();
            var eligible = all
                .Where(download => IsReprocessable(download.Status))
                .Where(download => (download.CompletedAt ?? download.StartedAt) >= cutoff)
                // LastImportedAt is the record that an import already ran for this download, so it is
                // what "already processed" means here.
                .Where(download => includeProcessed || !download.LastImportedAt.HasValue)
                .Select(download => download.Id)
                .ToList();

            logger.LogInformation("Reprocessing {Eligible} of {Total} downloads", eligible.Count, all.Count);

            return await ReprocessDownloadsAsync(eligible);
        }
    }
}
