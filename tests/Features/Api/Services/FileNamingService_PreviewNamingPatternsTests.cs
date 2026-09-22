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

            var separator = Path.DirectorySeparatorChar;
            Assert.DoesNotContain($"{separator}{separator}", preview.FolderExample);
            var segments = preview.FolderExample.Split(separator, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, segments.Length); // Author/Title, with the empty Series segment gone
        }

        [Fact]
        public void PreviewNamingPatterns_FilePatternWithLiteralSeparator_KeepsBothSegmentsInsteadOfFlatteningToTheLast()
        {
            // Rule 4 (fidelity check, measured): GenerateFilePathAsync only flattens a file
            // pattern to its last path segment (treatAsFilename=true) when the pattern does not
            // itself imply subfolders (PatternImpliesSubfolders). A literal '/' between two
            // tokens is judged to imply subfolders, so both segments must survive here. A prior
            // version of this test used {DiskNumber:00}/{Title} and asserted a leading separator,
            // which was wrong: for a genuinely single-file sample DiscNumber is empty, and an
            // empty leading segment is dropped by ordinary path-component joining regardless of
            // treatAsFilename, so that pattern does not actually discriminate the two code paths.
            // {Author}/{Title} does: both tokens render non-empty values, so treatAsFilename=true
            // would silently drop "M. R. Castellane" and keep only the title.
            var preview = _service.PreviewNamingPatterns(
                folderPattern: null,
                filePattern: "{Author}/{Title}",
                multiFilePattern: null);

            Assert.Equal(
                "M. R. Castellane" + Path.DirectorySeparatorChar + "The Clockmaker's Apprentice - A Novel.m4b",
                preview.SingleFileExample);
        }

        [Fact]
        public async Task PreviewNamingPatterns_SingleFileRow_MatchesGenerateFilePathAsyncForATrueSingleFileBook()
        {
            // Differential check against the real render path, not a hand-derived expected
            // string: a prior version of this file asserted a value worked out by reasoning
            // about ApplyNamingPattern by hand, and that reasoning was wrong (it assumed a
            // single-file sample would carry a DiscNumber, which GenerateFilePathAsync never
            // gives a genuinely single-file book). This test instead calls GenerateFilePathAsync
            // itself and checks the preview's single-file row is exactly its tail.
            const string folderPattern = "{Author}";
            const string filePattern = "{DiskNumber:00}/{Title}";
            var settings = new ApplicationSettings
            {
                FolderNamingPattern = folderPattern,
                FileNamingPattern = filePattern,
                OutputPath = string.Empty
            };
            var mockConfig = new Mock<IConfigurationService>();
            mockConfig.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(settings);
            var service = new FileNamingService(mockConfig.Object, new Mock<ILogger<FileNamingService>>().Object);
            var singleFileSample = FileNamingService.BuildPreviewMetadata(discNumber: null, trackNumber: null);

            var actual = await service.GenerateFilePathAsync(singleFileSample, outputPath: string.Empty, ".m4b");
            var preview = service.PreviewNamingPatterns(folderPattern, filePattern, multiFilePattern: null);

            Assert.EndsWith(preview.SingleFileExample, actual, StringComparison.Ordinal);
            Assert.StartsWith(preview.FolderExample, actual, StringComparison.Ordinal);
        }

        [Fact]
        public void PreviewNamingPatterns_MultiFilePatternWithoutDifferentiator_IsAmbiguous()
        {
            var preview = _service.PreviewNamingPatterns(
                folderPattern: null,
                filePattern: null,
                multiFilePattern: "{Title}");

            Assert.True(preview.MultiFileAmbiguous);
            Assert.Equal(3, preview.MultiFileExamples.Count);
            Assert.Single(preview.MultiFileExamples.Distinct());
        }

        [Fact]
        public void PreviewNamingPatterns_MultiFilePatternWithOnlyDiskNumber_IsAmbiguousForFilesSharingADisc()
        {
            // A pattern that varies only by DiskNumber cannot tell apart two files that share a
            // disc (a book with 10 chapters all under "Disc 1", for example), so this must be
            // flagged ambiguous even though the pattern does differentiate SOME files. An
            // earlier version of this check probed (disc 1, track 1) against (disc 2, track 2),
            // which varies both fields at once and never surfaces this collision.
            var preview = _service.PreviewNamingPatterns(
                folderPattern: null,
                filePattern: null,
                multiFilePattern: "{Title}-{DiskNumber:00}");

            Assert.True(preview.MultiFileAmbiguous);
        }

        [Fact]
        public void PreviewNamingPatterns_MultiFilePatternWithDiskAndChapterNumber_IsNotAmbiguous()
        {
            // The shipped MultiFileNamingPattern default (ApplicationSettings.cs). Every probed
            // combination of disc and track must render a distinct name.
            var preview = _service.PreviewNamingPatterns(
                folderPattern: null,
                filePattern: null,
                multiFilePattern: "{Title}-{DiskNumber:00}-{ChapterNumber:00}");

            Assert.False(preview.MultiFileAmbiguous);
            Assert.Equal(3, preview.MultiFileExamples.Distinct().Count());
        }

        [Fact]
        public void PreviewNamingPatterns_BlankFolderPattern_ReturnsEmptyFolderExample()
        {
            // Not reproduced: GenerateFilePathAsync's legacy full-path mode for a blank folder
            // pattern (see the comment in FileNamingService.Preview.cs). The row is also never
            // shown in the UI while its own input is blank, so an empty example here is honest
            // about what this preview does and does not know, rather than guessing.
            var preview = _service.PreviewNamingPatterns(folderPattern: "", filePattern: null, multiFilePattern: null);

            Assert.Equal(string.Empty, preview.FolderExample);
        }

        [Fact]
        public void PreviewNamingPatterns_BlankFileAndMultiFilePatterns_DefaultToTitleLikeGenerateFilePathAsyncDoes()
        {
            // GenerateFilePathAsync defaults a blank file pattern to "{Title}" whenever a folder
            // pattern is set (FileNamingService.cs: `effectiveFilePattern = ... ? "{Title}" :
            // filePattern`), which is the common case since the shipped FolderNamingPattern
            // default is never blank. The preview matches that rather than returning an empty
            // string for a blank single-file/multi-file pattern.
            var preview = _service.PreviewNamingPatterns(folderPattern: null, filePattern: "", multiFilePattern: "");

            Assert.Equal("The Clockmaker's Apprentice - A Novel.m4b", preview.SingleFileExample);
            Assert.Equal(3, preview.MultiFileExamples.Count);
            Assert.All(preview.MultiFileExamples, example => Assert.Contains("Clockmaker's Apprentice", example));
        }

        [Fact]
        public void PreviewNamingPatterns_NeverDoublesTheExtension()
        {
            var preview = _service.PreviewNamingPatterns(folderPattern: null, filePattern: "{Title}.m4b", multiFilePattern: null);

            Assert.EndsWith(".m4b", preview.SingleFileExample);
            Assert.DoesNotContain(".m4b.m4b", preview.SingleFileExample);
        }
    }
}
