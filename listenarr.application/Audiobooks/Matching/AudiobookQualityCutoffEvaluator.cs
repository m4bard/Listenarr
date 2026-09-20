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

using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Matching
{
    /// <summary>
    /// Shared "is the audiobook already at/above its quality cutoff?" evaluation.
    ///
    /// Per-file/-download quality is resolved through <see cref="QualityMatcher"/> rather than the
    /// previous string-label classifier that emitted container labels (e.g. "M4B") which never
    /// equalled a codec/bitrate rung — so the cutoff was never met and the audiobook was
    /// searched/re-grabbed on every cycle.
    /// </summary>
    public static class AudiobookQualityCutoffEvaluator
    {
        public static async Task<bool> IsQualityCutoffMetAsync(
            Audiobook audiobook,
            IDownloadRepository downloadRepository,
            IAudiobookFileRepository audioFileRepository,
            ILogger? logger = null)
        {
            var profile = audiobook.QualityProfile;
            if (profile == null)
            {
                return false;
            }

            var existingDownloads = (await downloadRepository.GetByAudiobookIdAsync(audiobook.Id))
                .Where(d => d.Status == DownloadStatus.Completed ||
                            d.Status == DownloadStatus.Downloading ||
                            d.Status == DownloadStatus.ImportPending)
                .ToList();

            var existingFiles = await audioFileRepository.GetByAudiobookIdAsync(audiobook.Id);

            if (!existingDownloads.Any() && !existingFiles.Any())
            {
                return false;
            }

            // Narrowed guard: a blank profile (no rungs, or no CutoffQuality set) is always
            // satisfied, matching QualityMatcher.ResolveCutoff/MeetsCutoff/LabelMeetsCutoff and
            // AudiobookStatusEvaluator's library-view answer for the same input. A CutoffQuality
            // that names a rung the profile no longer lists, or that is present but not Allowed,
            // is a different case and still means "keep searching": that half of the original
            // guard is preserved below, delegated to QualityMatcher.AllowedQualities so this guard
            // and QualityMatcher.MeetsCutoff/LabelMeetsCutoff (used further down in this same
            // method) can no longer disagree about what Allowed means.
            var cutoffIsBlank = profile.Qualities.Count == 0 ||
                                 string.IsNullOrWhiteSpace(profile.CutoffQuality);

            if (!cutoffIsBlank)
            {
                var cutoffQuality = QualityMatcher.AllowedQualities(profile)
                    .FirstOrDefault(q => q.Quality == profile.CutoffQuality);

                if (cutoffQuality == null)
                {
                    return false;
                }
            }

            foreach (var download in existingDownloads)
            {
                if (download.Status == DownloadStatus.Completed &&
                    !string.IsNullOrEmpty(download.Metadata?.GetValueOrDefault("Quality")?.ToString()))
                {
                    var downloadQuality = download.Metadata["Quality"].ToString();
                    if (QualityMatcher.LabelMeetsCutoff(downloadQuality, profile))
                    {
                        logger?.LogDebug(
                            "Quality cutoff met for audiobook '{Title}' by completed download (Quality: {Quality})",
                            audiobook.Title,
                            downloadQuality);
                        return true;
                    }
                }
                else if (download.Status == DownloadStatus.Downloading ||
                         download.Status == DownloadStatus.ImportPending)
                {
                    logger?.LogDebug(
                        "Quality cutoff assumed met for audiobook '{Title}' due to active download/import",
                        LogRedaction.SanitizeText(audiobook.Title));
                    return true;
                }
            }

            foreach (var file in existingFiles)
            {
                if (QualityMatcher.MeetsCutoff(ToInput(file), profile))
                {
                    logger?.LogDebug(
                        "Quality cutoff met for audiobook '{Title}' by existing file (File: {FileName})",
                        audiobook.Title,
                        Path.GetFileName(file.Path));
                    return true;
                }
            }

            return false;
        }

        /// <summary>The profile rung label a stored file maps to, or null if it does not match.</summary>
        public static string? ResolveFileQualityLabel(AudiobookFile file, QualityProfile? profile)
            => QualityMatcher.MatchLabel(ToInput(file), profile);

        private static AudioQualityInput ToInput(AudiobookFile file) => new()
        {
            Codec = file.Codec,
            Container = file.Container,
            Format = file.Format,
            BitrateBitsPerSecond = file.Bitrate,
            Path = file.Path
        };
    }
}
