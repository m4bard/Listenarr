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
    /// Round-4 independent-review adversarial fixtures against commit 53bee4069's narrowed
    /// BitrateTagPattern (literal "FLAC" in a bracket, or a number + literal "kbps" unit only).
    /// Deliberately NOT reusing round 1-3 fixtures (now permanent regressions). Targets the one
    /// remaining plausible collision class: nonfiction audiobooks ABOUT digital audio/music
    /// technology, whose titles legitimately reference "FLAC" or a kbps figure as subject matter
    /// rather than as a release tag -- plus the "must still catch" fixtures with the updated
    /// kbps-based tags, to confirm the catch-path survived the narrowing.
    ///
    /// All fixtures are synthetic: invented titles, authors and narrators, never a real title,
    /// author or narrator.
    /// </summary>
    [Trait("Name", "MusicReleaseClassifierReviewRound4Tests")]
    [Trait("Category", "Application")]
    public sealed class MusicReleaseClassifierReviewRound4Tests : BaseTests
    {
        private readonly ITestOutputHelper _output;

        public MusicReleaseClassifierReviewRound4Tests(ITestOutputHelper output)
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

        // 1. Dash(8) + BitrateTag(48, "(FLAC...)" as a descriptive parenthetical about the
        //    SUBJECT of a nonfiction audio-engineering memoir, not a release tag) = 56.
        [Fact]
        public void Adversarial4_DashShapedNonfictionAudioMemoir_FlacAsSubjectMatter_NotAReleaseTag()
        {
            var result = Result(
                category: null,
                title: "Jordan Pierce - The Vinyl Revival (FLAC Format Explained)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 2. Dash(8) + BitrateTag(48, "128kbps" as ordinary technical content, no brackets
        //    needed since the kbps branch doesn't require one) = 56.
        [Fact]
        public void Adversarial4_DashShapedNonfictionAudioMemoir_KbpsFigureAsSubjectMatter()
        {
            var result = Result(
                category: null,
                title: "Jordan Pierce - Engineering Sound: 128kbps and Beyond");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 3. AlbumVocab(12) + BitrateTag(48, "128kbps"), no dash shape at all.
        [Fact]
        public void Adversarial4_AlbumVocabPlusKbpsFigure_NoDashShape()
        {
            var result = Result(category: null, title: "The Remastered History of 128kbps Streaming");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 4. A contrived but regex-blind case: "FLAC" as a fictional in-universe acronym rather
        //    than an audio format, in a bracketed parenthetical, dash-shaped title.
        [Fact]
        public void Adversarial4_FlacAsFictionalAcronym_NotAnAudioFormat()
        {
            var result = Result(
                category: null,
                title: "Senator Avery Lin - The Silent Vote (FLAC Resistance)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 5. Sanity: BitrateTag alone via "[FLAC]" -- no dash, no vocab, no genre, no fields, no
        //    size -- must stay under threshold alone (48 < 50).
        [Fact]
        public void Adversarial4_Sanity_FlacTagAlone_StaysUnderThreshold()
        {
            var result = Result(category: null, title: "A Book About Sound [FLAC]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // 6. Sanity: a bare 4-digit number followed immediately by digits+kbps-like text should
        //    NOT match (confirms the \d{2,3} bound is respected) -- a page count like "1280" next
        //    to unrelated text must not accidentally combine into a kbps match.
        [Fact]
        public void Adversarial4_Sanity_FourDigitNumberNearKbpsLikeText_DoesNotFalsePositive()
        {
            var result = Result(category: null, title: "Teodor Fenwick - A Long Walk (1280 Kilometers)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // --- Must-still-catch confirmations (catch-path not gutted by the narrowing) ---

        [Fact]
        public void Confirm_MusicSingle_NotAlbum_IsCaught()
        {
            var result = Result(category: null, title: "Mira Delgado - Nightfall (Single) [320kbps]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        [Fact]
        public void Confirm_AlbumVocabularyPlusBitrateTag_IsCaught()
        {
            var result = Result(category: null, title: "Nightfall Sessions EP [320kbps]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        [Fact]
        public void Confirm_PopNearTwin_MislabeledMusicWithGenreMarker_IsCaught()
        {
            var result = Result(
                category: "3030",
                title: "Jonas Harrow - Harbor Lights (Pop) [FLAC]",
                artist: "Jonas Harrow",
                album: "Harbor Lights (Pop)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        [Fact]
        public void Confirm_ArtistAlbumFields_PlusOneWeakTitleSignal_CrossesThreshold()
        {
            var result = Result(
                category: null,
                title: "Soraya Quint - Midnight Transit [FLAC]",
                artist: "Soraya Quint",
                album: "Midnight Transit");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        [Fact]
        public void Confirm_Category3030_WithStrongContraryTitleAndFieldSignals_IsOverridden()
        {
            var result = Result(
                category: "3030",
                title: "Nadia Brecht - Low Tide (Rock) [320kbps]",
                artist: "Nadia Brecht",
                album: "Low Tide (Rock)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        // Diagnostic-only: prints the reason string for every false-positive candidate above.
        [Fact]
        public void Diagnostic4_PrintReasonsForAllRound4Fixtures()
        {
            void Probe(string label, SearchResult r)
            {
                var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(r, out var reason);
                _output.WriteLine($"{label} => LooksLikeMusicRelease={looksLikeMusic}; reason=\"{reason}\"");
            }

            Probe("DashNonfictionAudioMemoir_FlacSubject",
                Result(category: null, title: "Jordan Pierce - The Vinyl Revival (FLAC Format Explained)"));
            Probe("DashNonfictionAudioMemoir_KbpsSubject",
                Result(category: null, title: "Jordan Pierce - Engineering Sound: 128kbps and Beyond"));
            Probe("AlbumVocabPlusKbpsFigure_NoDash",
                Result(category: null, title: "The Remastered History of 128kbps Streaming"));
            Probe("FlacAsFictionalAcronym",
                Result(category: null, title: "Senator Avery Lin - The Silent Vote (FLAC Resistance)"));
        }
    }
}
