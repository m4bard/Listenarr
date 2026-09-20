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

namespace Listenarr.Application.Metadata.Core
{
    public static class AudiobookStatusEvaluator
    {
        public const string Downloading = "downloading";
        public const string NoFile = "no-file";
        public const string QualityMismatch = "quality-mismatch";
        public const string QualityMatch = "quality-match";

        public static string ComputeStatus(
            bool isDownloading,
            bool hasAnyFile,
            string? audiobookQuality,
            QualityProfile? qualityProfile,
            IReadOnlyList<AudiobookFormatSummary>? files)
        {
            if (isDownloading)
            {
                return Downloading;
            }

            if (!hasAnyFile)
            {
                return NoFile;
            }

            if (qualityProfile == null)
            {
                return QualityMatch;
            }

            // PreferredFormats is not consulted here. It says which container the user would rather
            // grab, not whether what is already on disk is good enough, and this status answers the
            // second question only. Filtering the file list by it made a 320 kbps MP3 above an
            // MP3 128kbps cutoff report QualityMismatch, which the library view renders as the words
            // "Below Cutoff". It also put this evaluator at odds with AudiobookQualityCutoffEvaluator,
            // which drives automatic search off the same stored files and applies no format filter.
            var candidateFiles = files ?? Array.Empty<AudiobookFormatSummary>();

            if (candidateFiles.Count == 0)
            {
                return QualityMatch;
            }

            // A profile that is not upgrading is handled below rather than here: every cutoff
            // comparison past this point runs through QualityMatcher, which already treats
            // UpgradeAllowed = false the same way it treats a blank cutoff.
            if (string.IsNullOrWhiteSpace(qualityProfile.CutoffQuality)
                || qualityProfile.Qualities == null
                || qualityProfile.Qualities.Count == 0)
            {
                return QualityMatch;
            }

            // A pinned audiobook-level quality short-circuits per-file derivation.
            if (!string.IsNullOrWhiteSpace(audiobookQuality))
            {
                return QualityMatcher.LabelMeetsCutoff(audiobookQuality, qualityProfile)
                    ? QualityMatch
                    : QualityMismatch;
            }

            foreach (var file in candidateFiles)
            {
                var input = new AudioQualityInput
                {
                    Codec = file.Codec,
                    Container = file.Container,
                    Format = file.Format,
                    BitrateBitsPerSecond = file.Bitrate,
                    // Path is the only quality signal when metadata processing is disabled,
                    // ffprobe is unavailable, or extraction failed. Mirrors AudiobookQualityCutoffEvaluator
                    // so automatic-search and library status agree for path-only files.
                    Path = file.Path
                };

                if (QualityMatcher.MeetsCutoff(input, qualityProfile))
                {
                    return QualityMatch;
                }
            }

            return QualityMismatch;
        }
    }
}
