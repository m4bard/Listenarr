/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Application.Common.Exceptions;

namespace Listenarr.Infrastructure.Downloads.Processing
{
    public partial class DownloadProcessingJobProcessor
    {
        // Conflicts that resolve without operator action: a move that is running finishes, and a committed
        // file import completes its publication. Every other conflict code waits for a repair.
        private static readonly HashSet<string> TemporaryConflictCodes = new(StringComparer.Ordinal)
        {
            "move_already_active",
            "move_active_options_conflict",
            "registration_recovery_pending"
        };

        /// <summary>
        /// The import refused because an earlier file operation on this audiobook is unresolved. A temporary
        /// conflict uses the job's bounded retries. Any other conflict fails the job and keeps the reason on the
        /// download; Retry Import makes the failed job runnable again once the cause is repaired.
        /// </summary>
        private static async Task HandleImportConflictAsync(
            ApplicationConflictException exception,
            DownloadProcessingJob job,
            IDownloadProcessingJobService jobService,
            IHistoryRepository historyRepository,
            IDownloadService downloadService,
            Download download,
            Audiobook audiobook,
            string correlationId,
            CancellationToken ct)
        {
            if (TemporaryConflictCodes.Contains(exception.Code))
            {
                await ScheduleRetryAsync(job, jobService, historyRepository, download, audiobook,
                    correlationId, exception.Message, ct);
                return;
            }

            download.AddBlockMessage(exception.Message);
            await downloadService.UpdateAsync(download);
            await FailImportAsync(job, jobService, historyRepository, download, audiobook,
                correlationId, exception.Message, ct);
        }
    }
}
