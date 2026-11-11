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

using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.HostedServices.Search
{
    public partial class AutomaticSearchProcessor
    {
        private async Task<(bool skipSearch, string? bestExistingQuality, List<Download> allDownloads)> ShouldSkipSearchAsync(
            Audiobook audiobook,
            IDownloadRepository downloadRepository,
            IAudiobookFileRepository fileRepository,
            CancellationToken stoppingToken)
        {
            // Check if there's already an active download for this audiobook
            var allDownloads = await downloadRepository.GetByAudiobookIdAsync(audiobook.Id, stoppingToken);
            var activeDownload = allDownloads.FirstOrDefault(d =>
                d.Status == DownloadStatus.Queued ||
                d.Status == DownloadStatus.Downloading ||
                d.Status == DownloadStatus.Paused ||
                d.Status == DownloadStatus.Processing ||
                d.Status == DownloadStatus.Ready ||
                d.Status == DownloadStatus.ImportPending);

            if (activeDownload != null)
            {
                _logger.LogInformation("Audiobook '{Title}' already has an active download (ID: {DownloadId}, Status: {Status}), skipping automatic search",
                    audiobook.Title, activeDownload.Id, activeDownload.Status);
                return (true, null, allDownloads);
            }

            // Check existing quality and decide whether to search
            var (cutoffMet, bestExistingQuality) = await _qualityEvaluator.GetExistingQualityAsync(audiobook, downloadRepository, fileRepository, stoppingToken);
            _logger.LogInformation("Audiobook '{Title}': cutoff met={CutoffMet}, best existing quality={BestQuality}",
                audiobook.Title, cutoffMet, bestExistingQuality ?? "none");

            // Skip automatic search if quality cutoff is already met
            if (cutoffMet)
            {
                _logger.LogInformation("Quality cutoff already met for audiobook '{Title}', skipping automatic search", audiobook.Title);
                return (true, bestExistingQuality, allDownloads);
            }

            return (false, bestExistingQuality, allDownloads);
        }
    }
}
