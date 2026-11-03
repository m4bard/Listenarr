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
using Xunit.Abstractions;

namespace Listenarr.Tests.Features.Application.Search.Scoring
{
    /// <summary>
    /// Round-3 independent-review adversarial fixtures against commit 760466f17's narrowed
    /// bitrate pattern (FLAC/V0/V1/V2/320 only) and re-tuned weights. Deliberately NOT reusing
    /// round-1 or round-2 fixtures (those are now permanent regressions, i.e. exactly what was
    /// tuned against). Targets the two tokens kept in the narrowed pattern that double as
    /// ordinary, non-audio book metadata: "V1"/"V2" as series volume abbreviations, and "320" as
    /// a page count -- both extremely common in real audiobook/ebook listings, unlike FLAC.
    ///
    /// All fixtures are synthetic: invented titles, authors and narrators, never a real title,
    /// author or narrator.
    /// </summary>
    [Trait("Name", "MusicReleaseClassifierReviewRound3Tests")]
    [Trait("Category", "Application")]
    public sealed class MusicReleaseClassifierReviewRound3Tests : BaseTests
    {
        private readonly ITestOutputHelper _output;

        public MusicReleaseClassifierReviewRound3Tests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static SearchResult Result(
            string? category = null,
            string? title = "Some Audiobook Title",
            string? artist = null,
            string? album = null,
            long size = 0)
        {
            return new SearchResult
            {
                Title = title ?? string.Empty,
                Category = category ?? string.Empty,
                Artist = artist ?? string.Empty,
                Album = album ?? string.Empty,
                Size = size
            };
        }

        // 1. BareDash(8) + BitrateTag(48, "(320" matched as a page-count parenthetical, not a
        //    bitrate) = 56. No category, no Artist/Album, no Size. "(320 Pages)" is an ordinary
        //    print-metadata note some audiobook indexers echo from the underlying book listing.
        [Fact]
        public void Adversarial3_DashShapedTitle_PageCountParenthetical_320_IsNotABitrate()
        {
            var result = Result(category: null, title: "Teodor Fenwick - A Long Walk (320 Pages)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 2. BareDash(8) + BitrateTag(48, "(V1)" matched as a scene VBR label, but far more
        //    commonly a series-volume abbreviation in book/audiobook listings) = 56.
        [Fact]
        public void Adversarial3_DashShapedTitle_SeriesVolumeAbbreviation_V1_IsNotABitrate()
        {
            var result = Result(category: null, title: "Priya Nandakumar - Chronicles of the North (V1)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 3. AlbumVocab(12) + BitrateTag(48, "(V2)" volume marker) = 60, no dash shape at all.
        [Fact]
        public void Adversarial3_AlbumVocabPlusVolumeAbbreviation_V2_NoDashShape()
        {
            var result = Result(category: null, title: "The Second Anthology (V2)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 4. The strongest real-world shape: Dash(8) + ArtistAlbum(8) + BitrateTag(48, "(V1)") =
        //    64, with Artist/Album fields populated as a narrator/series pair consistent with the
        //    title's own dash shape -- exactly how a real series audiobook's metadata would look.
        [Fact]
        public void Adversarial3_DashArtistAlbumPlusVolumeAbbreviation_V1_PlausibleRealSeriesAudiobook()
        {
            var result = Result(
                category: null,
                title: "Priya Nandakumar - Chronicles of the North (V1)",
                artist: "Priya Nandakumar",
                album: "Chronicles of the North (V1)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 5. Sanity: BitrateTag alone via "(V1)" -- no dash, no vocab, no genre, no fields, no
        //    size -- must stay under threshold alone (48 < 50), confirming #2/#4 need the dash to
        //    cross and the bug is the combination, not the pattern alone.
        [Fact]
        public void Adversarial3_Sanity_VolumeAbbreviationAlone_StaysUnderThreshold()
        {
            var result = Result(category: null, title: "Chronicles of the North (V1)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // 6. Confirms the strong-tag catch-path still works as intended: same shape as round 2's
        //    MP3 fixture, but swapped to an actual scene-specific tag (FLAC). This SHOULD be
        //    caught (True) -- it is the true positive this path exists for, not a bug.
        [Fact]
        public void Adversarial3_Confirm_DashArtistAlbumPlusFlacTag_StillCaughtAsIntended()
        {
            var result = Result(
                category: null,
                title: "Elena Voss - The Clockmaker Chronicles [FLAC]",
                artist: "Elena Voss",
                album: "The Clockmaker Chronicles");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        // 7. Sanity: a triple of the four ambiguous signals without Size (Dash+GenreMarker+
        //    AlbumVocab = 8+18+12=38) must stay safely under threshold, confirming the claimed
        //    >=12 margin.
        [Fact]
        public void Adversarial3_Sanity_TripleAmbiguousSignals_NoFields_StaysUnderThreshold()
        {
            var result = Result(category: null, title: "Jonas Harrow - Low Tide (Rock) Anthology");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // 8. Sanity: all four ambiguous signals at once (Dash+GenreMarker+AlbumVocab+ArtistAlbum
        //    = 8+18+12+8=46) without Size must stay just under threshold.
        [Fact]
        public void Adversarial3_Sanity_AllFourAmbiguousSignals_NoSize_StaysUnderThreshold()
        {
            var result = Result(
                category: null,
                title: "Jonas Harrow - Low Tide (Rock) Anthology",
                artist: "Jonas Harrow",
                album: "Low Tide (Rock) Anthology");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // 9. Confirms the one case the design intends to catch: all four ambiguous signals PLUS
        //    a Size nudge (46+8=54) -- the maximally-corroborated case. SHOULD be True (by
        //    design), not a bug -- included so the boundary is measured, not assumed.
        [Fact]
        public void Adversarial3_Confirm_AllFourAmbiguousSignalsPlusSize_CrossesAsIntended()
        {
            var result = Result(
                category: null,
                title: "Jonas Harrow - Low Tide (Rock) Anthology",
                artist: "Jonas Harrow",
                album: "Low Tide (Rock) Anthology",
                size: 40_000_000);

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        // Diagnostic-only: prints the reason string for every false-positive candidate above
        // regardless of outcome, so signal breakdowns can be quoted directly.
        [Fact]
        public void Diagnostic3_PrintReasonsForAllRound3Fixtures()
        {
            void Probe(string label, SearchResult r)
            {
                var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(r, out var reason);
                _output.WriteLine($"{label} => LooksLikeMusicRelease={looksLikeMusic}; reason=\"{reason}\"");
            }

            Probe("PageCountParenthetical_320",
                Result(category: null, title: "Teodor Fenwick - A Long Walk (320 Pages)"));
            Probe("SeriesVolumeAbbreviation_V1_Dash",
                Result(category: null, title: "Priya Nandakumar - Chronicles of the North (V1)"));
            Probe("AlbumVocabPlusVolumeAbbreviation_V2",
                Result(category: null, title: "The Second Anthology (V2)"));
            Probe("DashArtistAlbumPlusVolumeAbbreviation_V1",
                Result(
                    category: null,
                    title: "Priya Nandakumar - Chronicles of the North (V1)",
                    artist: "Priya Nandakumar",
                    album: "Chronicles of the North (V1)"));
        }
    }
}
