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
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Api.Services
{
    /// <summary>
    /// Tests for FileNamingService.PreviewNamingPatterns, the server-rendered replacement for
    /// the settings screen's old frontend-only preview (fe/src/components/settings/FileManagementSection.vue
    /// applyPattern). Each test targets one of the four divergences the frontend renderer had
    /// from ApplyNamingPattern/GenerateFilePathAsync, so a regression that reintroduces the old,
    /// independent approximation fails here.
    /// </summary>
    [Trait("Category", "FileNamingService")]
    [Trait("Name", "FileNamingService_PreviewNamingPatternsTests")]
    public class FileNamingService_PreviewNamingPatternsTests : BaseTests
    {
        private readonly FileNamingService _service;

        public FileNamingService_PreviewNamingPatternsTests()
        {
            var mockConfig = new Mock<IConfigurationService>();
            var mockLogger = new Mock<ILogger<FileNamingService>>();
            _service = new FileNamingService(mockConfig.Object, mockLogger.Object);
        }

        [Fact]
        public void PreviewNamingPatterns_LowercaseTokenName_MirrorsRendererRatherThanResolvingItLikeTheOldFrontendClaimed()
        {
            // Correction to the tracker item, measured rather than assumed: the item describes
            // rule 1 as "token matching is RegexOptions.IgnoreCase on the backend, exact-case in
            // the frontend", implying the backend resolves a lowercase {title} the way {Title}
            // would resolve. It does not. RegexOptions.IgnoreCase on the token-syntax regex
            // (FileNamingService.cs:206, `\{(\w+)(?::([^}]+))?\}`) is a no-op for this purpose,
            // because \w already matches both cases and there is no case-sensitive literal
            // letter in that pattern. The actual variable-NAME lookup is
            // `variables.TryGetValue(variableName, ...)` against a plain
            // `Dictionary<string, object>` with no case-insensitive comparer
            // (FileNamingService.Helpers.cs BuildVariables), so a lowercase {title} is an
            // unrecognized variable, not a case-insensitive hit.
            //
            // Measured here: {title} in a folder pattern renders as "" (the empty-token
            // sentinel has nothing adjacent to preserve). In a file/multi-file pattern
            // (treatAsFilename=true) the same empty result falls through
            // SanitizePathComponent's "Unknown" fallback, producing "Unknown.m4b". This is
            // exactly the defect tracked upstream as #976. Per this item's sequencing caveat,
            // this preview mirrors that behavior rather than opinion its way around it; fixing
            // #976 is out of scope for this branch, and this test is what would need to change
            // (deliberately) once #976 lands.
            var preview = _service.PreviewNamingPatterns(
                folderPattern: "{title}",
                filePattern: "{title}",
                multiFilePattern: "{title}");

            Assert.Equal(string.Empty, preview.FolderExample);
            Assert.Equal("Unknown.m4b", preview.SingleFileExample);
            Assert.All(preview.MultiFileExamples, example => Assert.Equal("Unknown.m4b", example));
            Assert.True(preview.MultiFileAmbiguous);
        }

        [Fact]
        public void PreviewNamingPatterns_SanitizesInvalidCharactersInValues()
        {
            // Rule 2: ApplyNamingPattern sanitizes every rendered value through
            // SanitizePathComponent, which converts ':' to " - ". The sample Title carries a
            // colon specifically to exercise this. The old frontend preview never sanitized.
            var preview = _service.PreviewNamingPatterns(folderPattern: "{Title}", filePattern: null, multiFilePattern: null);

            Assert.DoesNotContain(":", preview.FolderExample);
            Assert.Contains("Clockmaker's Apprentice - A Novel", preview.FolderExample);
        }

        [Fact]
        public void PreviewNamingPatterns_ElidesSeparatorsAroundEmptyTokens()
        {
            // Rule 3: an empty token emits a sentinel that strips the adjacent separator or
            // slash. The sample deliberately leaves Series empty so the default folder pattern
            // "{Author}/{Series}/{Title}" collapses the empty segment instead of producing a
            // literal empty directory component. The old frontend preview's sample values were
            // all non-empty, so this branch was never reachable on screen.
            var preview = _service.PreviewNamingPatterns(
                folderPattern: "{Author}/{Series}/{Title}",
                filePattern: null,
                multiFilePattern: null);

            Assert.DoesNotContain("//", preview.FolderExample);
            var segments = preview.FolderExample.Split('/', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, segments.Length); // Author/Title, with the empty Series segment gone
        }

        [Fact]
        public void PreviewNamingPatterns_FilePatternWithDiskNumberToken_KeepsIntendedSubfolders()
        {
            // Rule 4 (fidelity check): GenerateFilePathAsync only flattens a file pattern to its
            // last path segment (treatAsFilename=true) when the pattern does not itself imply
            // subfolders. A pattern referencing DiskNumber is judged to imply subfolders
            // (PatternImpliesSubfolders), so the literal '/' in the pattern must survive here
            // exactly as it would through GenerateFilePathAsync. The sample's DiscNumber is 3
            // for a single-file preview, so a naive preview that always treats the file pattern
            // as a flat filename would collapse this to "Clockmaker's Apprentice - A Novel.m4b"
            // and lose the "03" segment.
            var preview = _service.PreviewNamingPatterns(
                folderPattern: null,
                filePattern: "{DiskNumber:00}/{Title}",
                multiFilePattern: null);

            Assert.StartsWith("03/", preview.SingleFileExample);
        }

        [Fact]
        public void PreviewNamingPatterns_MultiFilePatternWithoutDifferentiator_IsAmbiguous()
        {
            var preview = _service.PreviewNamingPatterns(
                folderPattern: null,
                filePattern: null,
                multiFilePattern: "{Title}");

            Assert.True(preview.MultiFileAmbiguous);
            Assert.Equal(2, preview.MultiFileExamples.Count);
            Assert.Equal(preview.MultiFileExamples[0], preview.MultiFileExamples[1]);
        }

        [Fact]
        public void PreviewNamingPatterns_MultiFilePatternWithDiskNumber_IsNotAmbiguous()
        {
            var preview = _service.PreviewNamingPatterns(
                folderPattern: null,
                filePattern: null,
                multiFilePattern: "{Title}-{DiskNumber:00}");

            Assert.False(preview.MultiFileAmbiguous);
            Assert.NotEqual(preview.MultiFileExamples[0], preview.MultiFileExamples[1]);
        }

        [Fact]
        public void PreviewNamingPatterns_EmptyPatterns_ReturnEmptyExamples()
        {
            var preview = _service.PreviewNamingPatterns(folderPattern: "", filePattern: "", multiFilePattern: "");

            Assert.Equal(string.Empty, preview.FolderExample);
            Assert.Equal(string.Empty, preview.SingleFileExample);
            Assert.Empty(preview.MultiFileExamples);
        }
    }
}
