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
namespace Listenarr.Application.Downloads.Import
{
    public partial class DownloadImportService
    {
        // Summarizes the best quality already on disk for this audiobook, so a candidate
        // import can be compared against it before it is copied or moved.
        private static string? DetermineBestExistingQuality(Audiobook audiobook)
        {
            string? bestExisting = null;
            var abProfile = audiobook.QualityProfile;
            if (audiobook.Files == null || audiobook.Files.Count == 0)
            {
                return bestExisting;
            }

            foreach (var f in audiobook.Files)
            {
                string q = string.Empty;
                if (!string.IsNullOrEmpty(f.Format)) q = f.Format;
                if (f.Bitrate.HasValue)
                {
                    var kb = f.Bitrate.Value / 1000;
                    if (kb >= 320) q = "MP3 320kbps";
                    else if (kb >= 256) q = "MP3 256kbps";
                    else if (kb >= 192) q = "MP3 192kbps";
                    else if (kb >= 128) q = "MP3 128kbps";
                }
                if (string.IsNullOrEmpty(q) && !string.IsNullOrEmpty(f.Path)) q = ImportQualityEvaluator.Determine(null, f.Path);
                if (string.IsNullOrEmpty(bestExisting)) bestExisting = q;
                else if (!string.IsNullOrEmpty(q) && !string.IsNullOrEmpty(bestExisting) && abProfile != null && ImportQualityEvaluator.IsAcceptable(q, bestExisting, abProfile)) bestExisting = q;
            }

            return bestExisting;
        }
    }
}
