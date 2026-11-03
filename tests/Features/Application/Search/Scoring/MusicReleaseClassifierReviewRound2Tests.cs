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
    /// Round-2 independent-review adversarial fixtures against commit cc6200554's retuned
    /// weights. Deliberately NOT the 6 fixtures from round 1 (those are now the exact set the
    /// implementer tuned against, per the reviewer's own stated methodology). Targets
    /// combinations the retune's own commit message did not rule out: GenreMarker+AlbumVocab
    /// co-occurring without a bitrate tag or cross-domain corroboration, Size nudging a
    /// two-weak-title-signal case, and a bitrate-tag-based corroboration case built around the
    /// fact that MP3 is a legitimate, common audiobook distribution format, not just a
    /// scene-release convention.
    ///
    /// All fixtures are synthetic: invented titles, authors and narrators, never a real title,
    /// author or narrator.
    /// </summary>
    [Trait("Name", "MusicReleaseClassifierReviewRound2Tests")]
    [Trait("Category", "Application")]
    public sealed class MusicReleaseClassifierReviewRound2Tests : BaseTests
    {
        private readonly ITestOutputHelper _output;

        public MusicReleaseClassifierReviewRound2Tests(ITestOutputHelper output)
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

        // 1. GenreMarker(35) + AlbumVocab(18) = 53, both drawn from title text alone, no bitrate
        //    tag, no dash, no category, no Artist/Album, no Size. The retune's own commit message
        //    states "no two of them may cross the threshold together without either a
        //    bitrate/format tag ... or genuine cross-domain corroboration" -- this combination has
        //    neither, and still crosses.
        [Fact]
        public void Adversarial2_GenreMarkerPlusAlbumVocab_TitleOnly_NoBitrateNoCrossDomain()
        {
            var result = Result(category: null, title: "Life After the Scene (Punk): An Anthology of Essays");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 2. Same bug surfaced with an explicit non-music, non-3030 numeric category riding
        //    along, to directly test whether an unrelated category masks or rescues it (it
        //    should contribute 0 and the title-only bug should persist unchanged).
        [Fact]
        public void Adversarial2_GenreMarkerPlusAlbumVocab_WithUnrelatedNonMusicCategory()
        {
            var result = Result(category: "7020", title: "Life After the Scene (Punk): An Anthology of Essays");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 3. AlbumVocab(18) + BitrateTag(35) = 53, title-only, no dash shape at all. The bitrate
        //    tag is "[CD Companion Edition]" -- a plausible real edition note, not a scene-release
        //    bitrate tag, but it still matches BitrateTagPattern because the regex only inspects
        //    the bracket's opening token.
        [Fact]
        public void Adversarial2_AlbumVocabPlusNonSceneBracketedEditionNote_NoDashShape()
        {
            var result = Result(category: null, title: "The Remastered Letters [CD Companion Edition]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 4. Size nudging a two-weak-title-signal case: Dash(10) + GenreMarker(35) = 45, safely
        //    under threshold alone -- but a small, music-typical size (SizeWeakWeight=10) nudges
        //    it to 55. No category, no Artist/Album, no bitrate tag.
        [Fact]
        public void Adversarial2_SmallSize_NudgesDashPlusGenreMarkerAcrossThreshold()
        {
            var withoutSize = Result(category: null, title: "Jonas Harrow - Low Tide (Rock)", size: 0);
            var withSize = Result(category: null, title: "Jonas Harrow - Low Tide (Rock)", size: 40_000_000);

            var withoutSizeResult = MusicReleaseClassifier.LooksLikeMusicRelease(withoutSize, out _);
            var withSizeResult = MusicReleaseClassifier.LooksLikeMusicRelease(withSize, out var reason);

            // Sanity: the two-signal case alone (45) must stay under threshold.
            Assert.False(withoutSizeResult);
            // The regression under test: Size alone should never be enough to tip an otherwise
            // sub-threshold case into rejection without a genuinely decisive signal present.
            Assert.False(withSizeResult);
        }

        // 5. The MP3-is-a-legitimate-audiobook-format case. Dash(10) + ArtistAlbum
        //    corroboration(10) + BitrateTag(35, "[MP3]") = 55, with NO category at all (many
        //    indexers provide none). The retune's commit message justifies keeping BitrateTag
        //    strong because "a real FLAC/MP3/320kbps-style tag is a scene-release convention with
        //    essentially no legitimate audiobook use" -- but MP3 specifically is the single most
        //    common real-world audiobook distribution format, and indexers that DO carry
        //    Artist/Album ID3 fields for audiobooks (narrator as Artist, title as Album) commonly
        //    also surface the file's own codec tag in the release title.
        [Fact]
        public void Adversarial2_DashArtistAlbumPlusMp3Tag_NoCategory_PlausibleRealAudiobookUpload()
        {
            var result = Result(
                category: null,
                title: "Elena Voss - The Clockmaker Chronicles [MP3]",
                artist: "Elena Voss",
                album: "The Clockmaker Chronicles");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 6. Contrast case for #5: same shape, but correctly tagged category 3030. Must be
        //    accepted -- this isolates whether the category signal is doing the protective work
        //    the operator is relying on for the common case (and confirms #5 is not just flaky).
        [Fact]
        public void Adversarial2_DashArtistAlbumPlusMp3Tag_WithCorrectCategoryTag_IsAccepted()
        {
            var result = Result(
                category: "3030",
                title: "Elena Voss - The Clockmaker Chronicles [MP3]",
                artist: "Elena Voss",
                album: "The Clockmaker Chronicles");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // 7. Three weak title-only signals without a bitrate tag (Dash+AlbumVocab+GenreMarker =
        //    10+18+35=63) -- confirms the magnitude of the bug in #1/#2 rather than finding a new
        //    one, but exercises a dash-shaped title specifically (the shape most audiobook
        //    releases with a narrator/title or author/title convention actually use).
        [Fact]
        public void Adversarial2_DashPlusAlbumVocabPlusGenreMarker_ThreeTitleOnlySignals()
        {
            var result = Result(category: null, title: "Priya Nandakumar - Echoes (Jazz): An Anthology");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 8. Sanity true-negative: GenreMarker + Dash alone, no vocab, no size, no bitrate, no
        //    fields (35+10=45). Confirms the boundary below the bug in #4 actually holds without
        //    the Size nudge.
        [Fact]
        public void Adversarial2_Sanity_DashPlusGenreMarker_Alone_StaysUnderThreshold()
        {
            var result = Result(category: null, title: "Jonas Harrow - Low Tide (Rock)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // 9. Sanity true-negative: four weak signals landing exactly one unit under threshold
        //    (Dash 10 + AlbumVocab 18 + ArtistAlbum 10 + SmallSize 10 = 48). Confirms the retune's
        //    stated four-signal-safe claim at its tightest margin.
        [Fact]
        public void Adversarial2_Sanity_FourWeakSignals_FortyEight_StaysUnderThreshold()
        {
            var result = Result(
                category: null,
                title: "Soraya Quint - Midnight Transit Anthology",
                artist: "Soraya Quint",
                album: "Midnight Transit Anthology",
                size: 40_000_000);

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // Diagnostic-only: prints the reason string for every case above regardless of its
        // assertion outcome, so signal breakdowns can be quoted directly. Not a behavioral
        // assertion; safe to delete after the report is written.
        [Fact]
        public void Diagnostic2_PrintReasonsForAllRound2Fixtures()
        {
            void Probe(string label, SearchResult r)
            {
                var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(r, out var reason);
                _output.WriteLine($"{label} => LooksLikeMusicRelease={looksLikeMusic}; reason=\"{reason}\"");
            }

            Probe("GenreMarkerPlusAlbumVocab_TitleOnly",
                Result(category: null, title: "Life After the Scene (Punk): An Anthology of Essays"));
            Probe("GenreMarkerPlusAlbumVocab_WithUnrelatedCategory",
                Result(category: "7020", title: "Life After the Scene (Punk): An Anthology of Essays"));
            Probe("AlbumVocabPlusNonSceneBracketedEditionNote",
                Result(category: null, title: "The Remastered Letters [CD Companion Edition]"));
            Probe("DashPlusGenreMarker_WithSmallSize",
                Result(category: null, title: "Jonas Harrow - Low Tide (Rock)", size: 40_000_000));
            Probe("DashArtistAlbumPlusMp3Tag_NoCategory",
                Result(
                    category: null,
                    title: "Elena Voss - The Clockmaker Chronicles [MP3]",
                    artist: "Elena Voss",
                    album: "The Clockmaker Chronicles"));
            Probe("DashPlusAlbumVocabPlusGenreMarker",
                Result(category: null, title: "Priya Nandakumar - Echoes (Jazz): An Anthology"));
        }
    }
}
