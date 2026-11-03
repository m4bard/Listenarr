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
    /// Round-6 independent-review adversarial fixtures against commit 80f15813e's gated
    /// bitrate/FLAC signal. Deliberately NOT reusing round 1-5 fixtures (now permanent
    /// regressions). Targets exactly what the implementer asked: whether the "music-specific
    /// corroborator" gates can be unlocked by a corroborator that fires for a genuinely real
    /// audiobook, not a mislabeled music release.
    ///
    /// Central finding: ArtistAlbumCorroborationWeight's underlying check
    /// (artistAlbumCorroborates) was built from round 1 onward specifically to tolerate a real
    /// audiobook's narrator/title metadata sitting in the Artist/Album fields -- it checks field
    /// CONSISTENCY with the title's dash shape, not whether the content is music. Reusing that
    /// same boolean as a gate for "genuinely music-specific" directly contradicts its own
    /// original design intent: it fires identically for a correctly-tagged real audiobook as for
    /// a mislabeled music release, because it was deliberately built to.
    ///
    /// All fixtures are synthetic: invented titles, authors and narrators, never a real title,
    /// author or narrator.
    /// </summary>
    [Trait("Name", "MusicReleaseClassifierReviewRound6Tests")]
    [Trait("Category", "Application")]
    public sealed class MusicReleaseClassifierReviewRound6Tests : BaseTests
    {
        private readonly ITestOutputHelper _output;

        public MusicReleaseClassifierReviewRound6Tests(ITestOutputHelper output)
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

        // 1. A real, correctly-tagged audiobook (narrator as Artist, title as Album, consistent
        //    with the title's own dash shape -- exactly round 1's "hardest negative control"
        //    shape) that happens to be a legitimate lossless (FLAC) release. No category.
        //    ArtistAlbumCorroboration fires (as it was always meant to, for a real audiobook) and
        //    that firing is now exactly what unlocks the FLAC gate.
        [Fact]
        public void Adversarial6_RealAudiobook_NarratorSeriesFieldsConsistentWithDash_LegitimateFlacRelease()
        {
            var result = Result(
                category: null,
                title: "Elena Voss - The Clockmaker Chronicles [FLAC]",
                artist: "Elena Voss",
                album: "The Clockmaker Chronicles");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 2. The same real-audiobook field shape, but now combined with round 5's exact finding
        //    (a telecom-history nonfiction subtitle ending in a kbps figure) -- the kbps path is
        //    supposed to need Artist/Album corroboration specifically BECAUSE vocab/genre can't
        //    tell it apart from a real release; this shows the one thing that CAN gate it open
        //    also fires for an ordinary, correctly-tagged audiobook.
        [Fact]
        public void Adversarial6_RealAudiobook_NarratorSeriesFieldsConsistentWithDash_TelecomHistoryKbpsEnding()
        {
            var result = Result(
                category: null,
                title: "Jordan Pierce - The Dial-Up Years: Life at 56kbps",
                artist: "Jordan Pierce",
                album: "The Dial-Up Years: Life at 56kbps");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 3. Sanity: category gate specifically probed per the implementer's own question -- a
        //    real, unrelated, non-music, non-3030 numeric category (e.g. "7020") riding alongside
        //    a kbps-ending title. CollectCategorySignals should NOT report this as a music
        //    signal, so the kbps gate should stay closed. Expected to stay SAFE (confirms the
        //    category gate itself is sound, unlike the Artist/Album one above).
        [Fact]
        public void Sanity6_UnrelatedNonMusicCategory_DoesNotFalselyGateKbps()
        {
            var result = Result(
                category: "7020",
                title: "Jordan Pierce - The Dial-Up Years: Life at 56kbps");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // 4. Sanity: Artist/Album populated with narrator/series data that does NOT match the
        //    title's dash shape (the gate's own non-firing case) alongside a FLAC tag -- must
        //    stay safe, confirming the gate only opens on genuine consistency, not mere presence.
        [Fact]
        public void Sanity6_ArtistAlbumPresentButInconsistentWithDash_DoesNotGateFlac()
        {
            var result = Result(
                category: null,
                title: "The Clockmaker's Apprentice [FLAC]",
                artist: "Elena Voss",
                album: "The Clockmaker Chronicles");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // 5. The genre-marker-as-FLAC-corroborator combination the implementer asked to probe
        //    fresh. A genre-word parenthetical AND a FLAC tag together on an otherwise plain
        //    title is a materially stronger, more specific combination than either alone, and I
        //    expect (not assert as a bug) this to correctly catch -- recorded as a confirm, not
        //    an adversarial claim.
        [Fact]
        public void Confirm6_GenreMarkerPlusFlacTag_CorrectlyCatches_NotFlaggingAsABug()
        {
            var result = Result(category: null, title: "Static and Silence (Punk) [FLAC]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        // 6. Sanity: genre marker alone (no FLAC, no vocab, no dash, no fields) must still stay
        //    under threshold by itself (18 < 50) -- confirms #5 needs the FLAC tag, not just the
        //    genre marker, to catch.
        [Fact]
        public void Sanity6_GenreMarkerAlone_NoFlacTag_StaysUnderThreshold()
        {
            var result = Result(category: null, title: "Static and Silence (Punk)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // Diagnostic-only: prints the reason string for the false-positive candidates above.
        [Fact]
        public void Diagnostic6_PrintReasonsForAllRound6Fixtures()
        {
            void Probe(string label, SearchResult r)
            {
                var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(r, out var reason);
                _output.WriteLine($"{label} => LooksLikeMusicRelease={looksLikeMusic}; reason=\"{reason}\"");
            }

            Probe("RealAudiobook_NarratorFields_LegitimateFlac",
                Result(
                    category: null,
                    title: "Elena Voss - The Clockmaker Chronicles [FLAC]",
                    artist: "Elena Voss",
                    album: "The Clockmaker Chronicles"));
            Probe("RealAudiobook_NarratorFields_TelecomHistoryKbps",
                Result(
                    category: null,
                    title: "Jordan Pierce - The Dial-Up Years: Life at 56kbps",
                    artist: "Jordan Pierce",
                    album: "The Dial-Up Years: Life at 56kbps"));
        }
    }
}
