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
            // A genuine single-file book: GenerateFilePathAsync only selects FileNamingPattern
            // (rather than MultiFileNamingPattern) when metadata.DiscNumber and
            // metadata.TrackNumber are BOTH null (FileNamingService.cs: `isMultiFile =
            // metadata.DiscNumber.HasValue || metadata.TrackNumber.HasValue`). The folder and
            // single-file rows use this same single sample, matching how a real single-file
            // import renders its folder and file components from one metadata object.
            var singleFileSample = BuildPreviewMetadata(discNumber: null, trackNumber: null);

            var folderExample = string.IsNullOrWhiteSpace(folderPattern)
                ? string.Empty
                : ApplyNamingPattern(folderPattern, singleFileSample, treatAsFilename: false);

            // Not reproduced here: when the folder pattern is blank, GenerateFilePathAsync
            // switches to a legacy mode where the file pattern (or a hardcoded default) is
            // applied as the FULL relative path, folder and filename combined, with no
            // treatAsFilename flattening at all. That has no equivalent in a three-row preview
            // where folder and file are always rendered as two separate examples, so folderExample
            // above stays empty for a blank folder pattern rather than guessing at legacy mode.
            //
            // The file and multi-file patterns do NOT get that special case: GenerateFilePathAsync
            // defaults a blank file pattern to "{Title}" whenever a folder pattern is set (the
            // shipped FolderNamingPattern default is never blank), so this always renders through
            // DefaultFilePattern below rather than short-circuiting to an empty string. The
            // three-row UI hides a row while its own input is blank regardless of what this
            // returns, so defaulting here costs nothing and keeps the response honest for any
            // other caller.
            var effectiveFilePattern = DefaultFilePattern(filePattern);
            var singleFileExample = AppendExtensionIfMissing(
                ApplyNamingPattern(
                    effectiveFilePattern,
                    singleFileSample,
                    treatAsFilename: !PatternImpliesSubfolders(effectiveFilePattern)));

            var effectiveMultiFilePattern = DefaultFilePattern(multiFilePattern);
            var multiFileTreatAsFilename = !PatternImpliesSubfolders(effectiveMultiFilePattern);

            // Render the same pattern against three files of the same book: two that share a
            // disc and two that share a track. Varying disc and track together (as an earlier
            // version of this did) hides a pattern that only references one of the two tokens,
            // since two probes that differ in both dimensions never collide even when the
            // pattern itself cannot tell the files apart.
            var partOne = ApplyNamingPattern(effectiveMultiFilePattern, BuildPreviewMetadata(discNumber: 1, trackNumber: 1), multiFileTreatAsFilename);
            var partTwo = ApplyNamingPattern(effectiveMultiFilePattern, BuildPreviewMetadata(discNumber: 1, trackNumber: 2), multiFileTreatAsFilename);
            var partThree = ApplyNamingPattern(effectiveMultiFilePattern, BuildPreviewMetadata(discNumber: 2, trackNumber: 1), multiFileTreatAsFilename);

            var multiFileExamples = new List<string>
            {
                AppendExtensionIfMissing(partOne),
                AppendExtensionIfMissing(partTwo),
                AppendExtensionIfMissing(partThree)
            };

            var multiFileAmbiguous =
                string.Equals(partOne, partTwo, StringComparison.OrdinalIgnoreCase)
                || string.Equals(partOne, partThree, StringComparison.OrdinalIgnoreCase)
                || string.Equals(partTwo, partThree, StringComparison.OrdinalIgnoreCase);

            return new NamingPatternPreview
            {
                FolderExample = folderExample,
                SingleFileExample = singleFileExample,
                MultiFileExamples = multiFileExamples,
                MultiFileAmbiguous = multiFileAmbiguous
            };
        }

        private static string DefaultFilePattern(string? pattern) =>
            string.IsNullOrWhiteSpace(pattern) ? "{Title}" : pattern;

        private static string AppendExtensionIfMissing(string value) =>
            value.EndsWith(PreviewExtension, StringComparison.OrdinalIgnoreCase)
                ? value
                : value + PreviewExtension;

        /// <summary>
        /// A fixed sample fed through the real renderer for the settings preview.
        /// Series and Subtitle are deliberately left empty: every other sample field in the
        /// old frontend-only preview was non-empty, which meant the bracket/separator elision
        /// rules in ApplyNamingPattern (empty tokens strip their surrounding punctuation) were
        /// never exercised on screen. Title carries a colon so SanitizePathComponent's
        /// invalid-character handling is visible too.
        /// </summary>
        internal static AudioMetadata BuildPreviewMetadata(int? discNumber, int? trackNumber)
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
