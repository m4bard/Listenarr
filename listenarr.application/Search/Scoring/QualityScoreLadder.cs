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
namespace Listenarr.Application.Search.Scoring
{
    /// <summary>
    /// The hardcoded codec/bitrate ladder that turns a release's quality string into a 0..100
    /// score. Accept/reject scoring (<see cref="SearchResultScorer"/>), the Smart sort
    /// (<see cref="CompositeScorer"/>) and the Quality column sort
    /// (<see cref="SearchResultSortingService"/>) all rank by it, so it lives here once.
    /// </summary>
    internal static class QualityScoreLadder
    {
        /// <summary>
        /// Scores a quality string. Matching is a substring test on the input lowered with the
        /// invariant culture; an empty or unrecognised string scores 0.
        /// </summary>
        public static int Score(string? quality)
        {
            if (string.IsNullOrEmpty(quality))
                return 0;

            var lowerQuality = quality.ToLowerInvariant();

            // Highest quality
            if (lowerQuality.Contains("flac"))
                return 100;

            // Audible format (AAX) - high quality
            if (lowerQuality.Contains("aax"))
                return 95;

            // Container formats
            if (lowerQuality.Contains("m4b"))
                return 90;

            // Modern efficient codecs
            if (lowerQuality.Contains("opus"))
                return 85;

            // VBR quality presets (LAME VBR presets like V0/V1/V2)
            if (ContainsVbrPreset(lowerQuality, "v0"))
                return 82;
            if (ContainsVbrPreset(lowerQuality, "v1"))
                return 76;
            if (ContainsVbrPreset(lowerQuality, "v2"))
                return 70;

            // AAC / M4A (check before numeric bitrates to prefer codec score for e.g. "AAC 256")
            if (lowerQuality.Contains("aac") || lowerQuality.Contains("m4a"))
                return 78;

            // Explicit numeric bitrates
            if (lowerQuality.Contains("320"))
                return 80;
            if (lowerQuality.Contains("256"))
                return 74;
            if (lowerQuality.Contains("192"))
                return 60;

            // VBR / CBR generic tokens (treat as mid-range if no numeric bitrate provided).
            // If there's an explicit numeric bitrate elsewhere, that will have matched above.
            if (lowerQuality.Contains("vbr") || lowerQuality.Contains("cbr"))
                return 65;

            // Generic MP3 mention without explicit bitrate -> mid-range
            if (lowerQuality.Contains("mp3") && !ContainsAnyBitrate(lowerQuality, "64", "128", "192", "256", "320"))
                return 65;

            if (lowerQuality.Contains("128"))
                return 50;
            if (lowerQuality.Contains("64"))
                return 40;

            return 0;
        }

        /// <summary>
        /// Checks if a quality string contains VBR preset indicators (v0, v1, v2).
        /// </summary>
        private static bool ContainsVbrPreset(string qualityLower, string preset)
        {
            return qualityLower.Contains(preset) ||
                   qualityLower.Contains($"-{preset}") ||
                   qualityLower.Contains($" {preset}");
        }

        /// <summary>
        /// Checks if a quality string contains any of the specified bitrate indicators.
        /// </summary>
        private static bool ContainsAnyBitrate(string qualityLower, params string[] bitrates)
        {
            return bitrates.Any(b => qualityLower.Contains(b));
        }
    }
}
