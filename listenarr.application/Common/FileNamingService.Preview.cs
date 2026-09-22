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
namespace Listenarr.Application.Common
{
    public partial class FileNamingService
    {
        private const string PreviewExtension = ".m4b";

        public NamingPatternPreview PreviewNamingPatterns(string? folderPattern, string? filePattern, string? multiFilePattern)
        {
            var folderExample = string.IsNullOrWhiteSpace(folderPattern)
                ? string.Empty
                : ApplyNamingPattern(folderPattern, BuildPreviewMetadata(), treatAsFilename: false);

            var singleFileExample = string.IsNullOrWhiteSpace(filePattern)
                ? string.Empty
                : ApplyNamingPattern(
                    filePattern,
                    BuildPreviewMetadata(),
                    treatAsFilename: !PatternImpliesSubfolders(filePattern)) + PreviewExtension;

            var multiFileExamples = new List<string>();
            if (!string.IsNullOrWhiteSpace(multiFilePattern))
            {
                var treatAsFilename = !PatternImpliesSubfolders(multiFilePattern);

                // Render the same pattern against two different parts of the same book, so the
                // preview shows whether the pattern actually produces distinct filenames instead
                // of asserting that it does.
                multiFileExamples.Add(
                    ApplyNamingPattern(multiFilePattern, BuildPreviewMetadata(discNumber: 1, trackNumber: 1), treatAsFilename) + PreviewExtension);
                multiFileExamples.Add(
                    ApplyNamingPattern(multiFilePattern, BuildPreviewMetadata(discNumber: 2, trackNumber: 2), treatAsFilename) + PreviewExtension);
            }

            return new NamingPatternPreview
            {
                FolderExample = folderExample,
                SingleFileExample = singleFileExample,
                MultiFileExamples = multiFileExamples,
                MultiFileAmbiguous = multiFileExamples.Count == 2
                    && string.Equals(multiFileExamples[0], multiFileExamples[1], StringComparison.OrdinalIgnoreCase)
            };
        }

        /// <summary>
        /// A fixed sample fed through the real renderer for the settings preview.
        /// Series and Subtitle are deliberately left empty: every other sample field in the
        /// old frontend-only preview was non-empty, which meant the bracket/separator elision
        /// rules in ApplyNamingPattern (empty tokens strip their surrounding punctuation) were
        /// never exercised on screen. Title carries a colon so SanitizePathComponent's
        /// invalid-character handling is visible too.
        /// </summary>
        private static AudioMetadata BuildPreviewMetadata(int? discNumber = 3, int? trackNumber = 3)
        {
            return new AudioMetadata
            {
                Title = "The Clockmaker's Apprentice: A Novel",
                Artist = "M. R. Castellane",
                Narrator = "Priya Okonkwo",
                Series = string.Empty,
                Subtitle = string.Empty,
                Edition = "Anniversary Edition",
                Publisher = "Harborlight Audio",
                Language = "English",
                Asin = "B0PREVIEW01",
                SeriesPosition = 2,
                Year = 2019,
                BitRate = 128,
                Format = "M4B",
                DiscNumber = discNumber,
                TrackNumber = trackNumber
            };
        }
    }
}
