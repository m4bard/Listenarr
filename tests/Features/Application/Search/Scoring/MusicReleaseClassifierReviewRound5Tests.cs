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
    /// Round-5 independent-review adversarial fixtures against commit e6fdf9872's end-anchored,
    /// shape-based BitrateTagPattern. Deliberately NOT reusing round 1-4 fixtures (now permanent
    /// regressions). Targets exactly the question the implementer raised: a title that legitimately
    /// ends in a kbps figure as its last words, as ordinary prose rather than a release tag -- and
    /// specifically widens it past "books about audio/music technology" (round 4's finding) to any
    /// nonfiction mentioning a data-transfer rate, since "kbps" is a general digital-data-rate unit,
    /// not an audio- or music-specific one (modems, early internet, telecom history all use it).
    /// Also separately checks the FLAC-bracket-must-contain-nothing-else behavior the implementer
    /// flagged as an open question, as a confirm-not-assert probe rather than a pass/fail bug.
    ///
    /// All fixtures are synthetic: invented titles, authors and narrators, never a real title,
    /// author or narrator.
    /// </summary>
    [Trait("Name", "MusicReleaseClassifierReviewRound5Tests")]
    [Trait("Category", "Application")]
    public sealed class MusicReleaseClassifierReviewRound5Tests : BaseTests
    {
        private readonly ITestOutputHelper _output;

        public MusicReleaseClassifierReviewRound5Tests(ITestOutputHelper output)
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

        // 1. Dash(8) + BitrateTag(48, title ends in "...128kbps" as ordinary prose about audio
        //    technology, not a tag). The implementer specifically asked whether this shape is
        //    constructible -- it is.
        [Fact]
        public void Adversarial5_DashShapedTitle_EndsInKbpsFigure_AsOrdinaryProseSentenceEnding()
        {
            var result = Result(
                category: null,
                title: "Jordan Pierce - The Streaming Wars: Why Everyone Settled on 128kbps");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 2. Wider than round 4's finding: "kbps" is a general digital-data-rate unit, not
        //    audio-or-music-specific -- a nonfiction book about early internet/telecom history can
        //    legitimately end a sentence in a kbps figure with no connection to music or even audio
        //    production at all.
        [Fact]
        public void Adversarial5_DashShapedTelecomHistoryTitle_EndsInKbpsFigure_NotAboutAudioAtAll()
        {
            var result = Result(
                category: null,
                title: "Jordan Pierce - The Dial-Up Years: Life at 56kbps");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 3. Same telecom-history shape, no dash, corroborated by AlbumVocab instead.
        [Fact]
        public void Adversarial5_AlbumVocabPlusKbpsSentenceEnding_NoDashShape()
        {
            var result = Result(category: null, title: "The Remastered Story of Life at 56kbps");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 4. Sanity: a title ending in the bare word "FLAC" with NO brackets at all must NOT match
        //    (the FLAC alternative requires a bracket; only the kbps alternative is bracket-optional).
        [Fact]
        public void Adversarial5_Sanity_BareFlacWordAtEndNoReackets_DoesNotMatch()
        {
            var result = Result(
                category: null,
                title: "Jordan Pierce - And That Is How We All Learned to Love FLAC");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // 5. Sanity: a kbps figure that is NOT the last thing in the title (prose continues after
        //    it) must not match -- confirms the end-anchor actually does its job.
        [Fact]
        public void Adversarial5_Sanity_KbpsFigureNotAtEnd_DoesNotMatch()
        {
            var result = Result(
                category: null,
                title: "Jordan Pierce - Life at 56kbps Changed Everything About How We Connect");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // 6. Open question from the implementer, confirmed rather than assumed: a bracket
        //    containing MORE than just the bitrate word ("[FLAC Rip]") does not match under the
        //    current pattern. This is a confirm-the-boundary probe, not a claimed bug -- recorded
        //    so the trade-off (some under-catching in exchange for zero over-catching) is visible
        //    and explicit rather than silently discovered later.
        [Fact]
        public void Confirm5_BracketWithExtraWordBesideFlac_DoesNotMatch_OpenQuestionFromImplementer()
        {
            var result = Result(category: null, title: "Some Artist - Some Album [FLAC Rip]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            // Recorded as False to confirm current behavior, per the implementer's own
            // description -- not asserting this is the ideal behavior either way. See the
            // writeup for my reasoning on whether this undercatches real releases.
            Assert.False(looksLikeMusic);
        }

        // --- Must-still-catch confirmations (re-checked independently, not just trusting the
        // implementer's own pre-push verification) ---

        [Fact]
        public void Confirm_MusicSingle_NotAlbum_IsCaught()
        {
            var result = Result(category: null, title: "Mira Delgado - Nightfall (Single) [320kbps]");

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

        // Diagnostic-only: prints the reason string for every false-positive candidate above.
        [Fact]
        public void Diagnostic5_PrintReasonsForAllRound5Fixtures()
        {
            void Probe(string label, SearchResult r)
            {
                var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(r, out var reason);
                _output.WriteLine($"{label} => LooksLikeMusicRelease={looksLikeMusic}; reason=\"{reason}\"");
            }

            Probe("DashTitle_EndsInKbps_AudioTechProse",
                Result(category: null, title: "Jordan Pierce - The Streaming Wars: Why Everyone Settled on 128kbps"));
            Probe("DashTitle_EndsInKbps_TelecomHistory_NotAboutAudio",
                Result(category: null, title: "Jordan Pierce - The Dial-Up Years: Life at 56kbps"));
            Probe("AlbumVocabPlusKbpsEnding_TelecomHistory_NoDash",
                Result(category: null, title: "The Remastered Story of Life at 56kbps"));
        }
    }
}
