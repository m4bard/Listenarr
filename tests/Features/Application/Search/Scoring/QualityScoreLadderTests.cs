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
using System.Text.RegularExpressions;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.Scoring
{
    /// <summary>
    /// The bitrate/codec quality ladder (quality string to 0..100) used to be hand-copied into
    /// four files: accept/reject scoring, the "Smart" sort, the "Quality" column sort, and a
    /// fourth copy with no caller. These tests pin the one shared implementation,
    /// <see cref="QualityScoreLadder"/>, and stop the ladder being copied out again.
    /// </summary>
    [Trait("Name", "QualityScoreLadderTests")]
    [Trait("Category", "Scoring")]
    public sealed class QualityScoreLadderTests : BaseTests
    {
        private static readonly string[] ProductionRoots =
        [
            "listenarr.domain",
            "listenarr.application",
            "listenarr.infrastructure",
            "listenarr.api",
        ];

        // A rung only the ladder writes: "flac" scoring exactly 100, in any layout. The format
        // scorers that also look for "flac" award other values, so they do not match.
        private static readonly Regex LadderFingerprint = new(
            @"Contains\(\s*""flac""\s*\)\s*\)\s*return\s+100\s*;",
            RegexOptions.Compiled);

        // The name every copy carried. Any mention in production code is a copy coming back, or a
        // caller that skipped the shared ladder.
        private static readonly Regex RetiredCopyName = new(@"\bGetQualityScore\s*\(", RegexOptions.Compiled);

        // ToLower( or ToUpper( with or without a culture argument. ToLowerInvariant( does not match.
        private static readonly Regex CultureSensitiveCasing = new(@"\.To(?:Lower|Upper)\s*\(", RegexOptions.Compiled);

        [Fact]
        public void Score_VariousTokens_ReturnsExpected()
        {
            // MP3 VBR should be the mid-range score (65)
            Assert.Equal(65, QualityScoreLadder.Score("MP3 VBR"));

            // V0/V1/V2 presets
            var v0 = QualityScoreLadder.Score("MP3 V0");
            var v1 = QualityScoreLadder.Score("MP3 V1");
            var v2 = QualityScoreLadder.Score("MP3 V2");
            Assert.True(v0 > v1 && v1 > v2);

            // Numeric bitrates
            Assert.Equal(80, QualityScoreLadder.Score("MP3 320kbps"));
            Assert.Equal(74, QualityScoreLadder.Score("MP3 256kbps"));

            // Opus/AAC/AAX
            Assert.Equal(85, QualityScoreLadder.Score("Opus VBR"));
            Assert.Equal(78, QualityScoreLadder.Score("AAC 256"));
            Assert.Equal(95, QualityScoreLadder.Score("AAX"));
        }

        /// <summary>
        /// Every rung of the ladder, plus the casing and separator variants the VBR-preset and
        /// bitrate checks are sensitive to. Before consolidation the four copies were pinned to
        /// agree on all of these, so the expected values are the behaviour all three live call
        /// sites already had.
        /// </summary>
        [Theory]
        [InlineData(null, 0)]
        [InlineData("", 0)]
        [InlineData("   ", 0)]
        [InlineData("Unknown", 0)]
        [InlineData("FLAC", 100)]
        [InlineData("flac", 100)]
        [InlineData("FLAC 24bit", 100)]
        [InlineData("FLAC HI-RES", 100)]
        [InlineData("AAX", 95)]
        [InlineData("aax", 95)]
        [InlineData("AAX 128", 95)]
        [InlineData("AUDIBLE AAX", 95)]
        [InlineData("M4B", 90)]
        [InlineData("m4b", 90)]
        [InlineData("M4B 64kbps", 90)]
        [InlineData("AUDIOBOOK M4B", 90)]
        [InlineData("OPUS", 85)]
        [InlineData("Opus VBR", 85)]
        [InlineData("OPUS DIGITAL", 85)]
        [InlineData("MP3 V0", 82)]
        [InlineData("MP3-V0", 82)]
        [InlineData("V0", 82)]
        [InlineData("MP3 V1", 76)]
        [InlineData("MP3-V1", 76)]
        [InlineData("MP3 V2", 70)]
        [InlineData("MP3-V2", 70)]
        [InlineData("AAC", 78)]
        [InlineData("AAC 256", 78)]
        [InlineData("M4A", 78)]
        [InlineData("m4a 192", 78)]
        [InlineData("MP3 320kbps", 80)]
        [InlineData("MP3 320", 80)]
        [InlineData("320", 80)]
        [InlineData("MP3 256kbps", 74)]
        [InlineData("256", 74)]
        [InlineData("MP3 192kbps", 60)]
        [InlineData("192", 60)]
        [InlineData("MP3 VBR", 65)]
        [InlineData("MP3 VBR (ISO)", 65)]
        [InlineData("MP3 CBR", 65)]
        [InlineData("MP3", 65)]
        [InlineData("MP3 128kbps", 50)]
        [InlineData("128", 50)]
        [InlineData("MP3 64kbps", 40)]
        [InlineData("64", 40)]
        // Rows below pin the relative order of adjacent rungs that no single input above
        // happens to hit together. Without these, swapping the if-order of two adjacent
        // rungs (e.g. flac/aax, or 128/64) leaves every other row green, because no other
        // row's input matches both rungs at once. Each value is the earlier-checked rung's
        // score, worked out by hand from the current order in QualityScoreLadder.Score.
        [InlineData("FLAC AAX", 100)] // flac checked before aax
        [InlineData("AAX M4B", 95)] // aax checked before m4b
        [InlineData("M4B OPUS", 90)] // m4b checked before opus
        [InlineData("OPUS V0", 85)] // opus checked before the v0 preset
        [InlineData("V0 V1", 82)] // v0 preset checked before v1
        [InlineData("V1 V2", 76)] // v1 preset checked before v2
        [InlineData("AAC V2", 70)] // v2 preset checked before aac/m4a
        [InlineData("AAC 320", 78)] // aac/m4a checked before the numeric 320 rung
        [InlineData("320 256", 80)] // 320 checked before 256
        [InlineData("256 192", 74)] // 256 checked before 192
        [InlineData("MP3 VBR 192", 60)] // 192 checked before the vbr/cbr generic rung
        [InlineData("MP3 128 64", 50)] // 128 checked before 64
        // Two adjacent pairs are deliberately not pinned here: vbr/cbr and the generic mp3
        // rung both score 65, so swapping them changes nothing observable; and the generic
        // mp3 rung explicitly excludes any input containing "128", so it and the 128 rung
        // can never both match the same input for their order to matter.
        public void Score_TokenCorpus_LandsOnExpectedRung(string? quality, int expected)
        {
            Assert.Equal(expected, QualityScoreLadder.Score(quality));
        }

        /// <summary>
        /// The ladder lowers its input with the invariant culture. A behavioural test under tr-TR
        /// cannot catch a regression to culture-sensitive ToLower today: the two only part company
        /// on the dotted and dotless I, and no rung of the ladder contains an "i", so every input
        /// scores the same either way. The casing choice is pinned on the source instead, and
        /// <see cref="LadderRungs_ContainNoLetterI"/> keeps that premise honest.
        /// </summary>
        [Fact]
        public void Ladder_LowersItsInputWithTheInvariantCulture()
        {
            var source = File.ReadAllText(LadderSourcePath());

            Assert.Contains(".ToLowerInvariant()", source, StringComparison.Ordinal);

            var offending = CultureSensitiveCasing.Matches(source).Select(match => match.Value).ToArray();
            Assert.True(
                offending.Length == 0,
                "QualityScoreLadder must lower its input with ToLowerInvariant; found culture-sensitive casing: "
                + string.Join(", ", offending));
        }

        /// <summary>
        /// The premise behind pinning the casing on the source rather than behaviourally. If a rung
        /// containing an "i" is ever added, culture-sensitive lowering would change scores under a
        /// Turkish or Azeri culture, and this fails so that a tr-TR behavioural test is written
        /// alongside the new rung.
        /// </summary>
        [Fact]
        public void LadderRungs_ContainNoLetterI()
        {
            var source = File.ReadAllText(LadderSourcePath());

            // Scoped to the Score method onward (which also covers the two helpers it calls,
            // declared below it), with line/doc comments stripped first. The class summary
            // above Score has <see cref="..."/> references (e.g. "CompositeScorer", which does
            // contain the letter this test checks for) and Score itself has a worked example in
            // a comment ("AAC 256"); neither is a rung, and a blind literal scan over the whole
            // file would wrongly flag both.
            var scoreIndex = source.IndexOf("public static int Score", StringComparison.Ordinal);
            Assert.True(scoreIndex >= 0, "Could not find the Score method to scope the literal scan to");
            var withoutComments = Regex.Replace(source[scoreIndex..], "//.*", string.Empty);

            // Every quoted literal from here on, not just the ones wrapped in a direct
            // Contains("..."): a rung passed through ContainsVbrPreset(lowerQuality, "v2") or
            // ContainsAnyBitrate(lowerQuality, "64", ...) is just as much a rung as a direct one.
            var rungs = Regex.Matches(withoutComments, "\"([^\"]*)\"")
                .Select(match => match.Groups[1].Value)
                .ToArray();

            // Control: the extraction has to find rungs reached only through a helper, or a
            // list missing them would pass by omission. "v0" only ever appears as a
            // ContainsVbrPreset argument; "64" appears both directly and inside
            // ContainsAnyBitrate's params array.
            Assert.Contains("flac", rungs);
            Assert.Contains("mp3", rungs);
            Assert.Contains("v0", rungs);
            Assert.Contains("64", rungs);

            var withI = rungs
                .Where(rung => rung.IndexOfAny(['i', 'I', 'ı', 'İ']) >= 0)
                .ToArray();
            Assert.True(
                withI.Length == 0,
                "A ladder rung now contains an \"i\", so culture can change a score. Add a tr-TR "
                + "behavioural test for it: " + string.Join(", ", withI));
        }

        [Fact]
        public void QualityLadder_IsDefinedExactlyOnce()
        {
            var root = RepositoryRootPath();
            var sources = ProductionRoots
                .SelectMany(project => Directory.EnumerateFiles(Path.Join(root, project), "*.cs", SearchOption.AllDirectories))
                .Where(file => !IsBuildArtifact(file))
                .Select(file => (Path: Path.GetRelativePath(root, file).Replace('\\', '/'), Text: File.ReadAllText(file)))
                .ToArray();

            var definitions = sources
                .Where(source => LadderFingerprint.IsMatch(source.Text))
                .Select(source => source.Path)
                .ToArray();
            Assert.Equal(["listenarr.application/Search/Scoring/QualityScoreLadder.cs"], definitions);

            var retiredName = sources
                .Where(source => RetiredCopyName.IsMatch(source.Text))
                .Select(source => source.Path)
                .ToArray();
            Assert.True(
                retiredName.Length == 0,
                "GetQualityScore was the name of the hand-copied ladders; use QualityScoreLadder.Score: "
                + string.Join(", ", retiredName));
        }

        [Theory]
        [InlineData("SearchResultScorer.cs")]
        [InlineData("CompositeScorer.cs")]
        [InlineData("SearchResultSortingService.cs")]
        public void LiveCallSites_RouteThroughTheSharedLadder(string fileName)
        {
            // Accept/reject scoring, the Smart sort and the Quality column sort respectively. Each
            // used to carry its own copy; with the copy gone, each must call the shared one.
            var path = Path.Join(RepositoryRootPath(), "listenarr.application", "Search", "Scoring", fileName);
            Assert.True(File.Exists(path), $"Expected to find {path}");

            Assert.Contains("QualityScoreLadder.Score(", File.ReadAllText(path), StringComparison.Ordinal);
        }

        private static string LadderSourcePath()
        {
            var path = Path.Join(
                RepositoryRootPath(),
                "listenarr.application",
                "Search",
                "Scoring",
                "QualityScoreLadder.cs");
            Assert.True(File.Exists(path), $"Expected to find {path}");
            return path;
        }

        private static bool IsBuildArtifact(string file) =>
            file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

        private static string RepositoryRootPath()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Join(directory.FullName, "listenarr.slnx")))
            {
                directory = directory.Parent;
            }

            Assert.True(directory != null, "Could not locate the repository root from the test output directory");
            return directory!.FullName;
        }
    }
}
