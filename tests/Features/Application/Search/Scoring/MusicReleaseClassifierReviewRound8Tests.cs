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
    /// Round-8 independent-review adversarial fixtures against commit b816c3820's two-signal
    /// convergence gate. Deliberately NOT reusing round 1-7 fixtures (now permanent regressions).
    /// Targets exactly what the implementer asked: can a real audiobook plausibly carry TWO of
    /// {album vocabulary, genre marker, bare dash shape} at once, alongside a genuine FLAC/kbps
    /// tag? Dash-shaped ("Author - Title") naming is an extremely common real audiobook
    /// convention, so combining it with either an anthology/collection word or a genre-word
    /// subtitle is not a contrived edge case -- it is how a large fraction of real audiobook
    /// titles are already shaped.
    ///
    /// All fixtures are synthetic: invented titles, authors and narrators, never a real title,
    /// author or narrator.
    /// </summary>
    [Trait("Name", "MusicReleaseClassifierReviewRound8Tests")]
    [Trait("Category", "Application")]
    public sealed class MusicReleaseClassifierReviewRound8Tests : BaseTests
    {
        private readonly ITestOutputHelper _output;

        public MusicReleaseClassifierReviewRound8Tests(ITestOutputHelper output)
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

        // 1. Dash + AlbumVocab + FLAC: an author-dash-title-shaped short-story anthology,
        //    archived in lossless FLAC. Dash-shaped audiobook titles and anthology/collection
        //    wording are both completely ordinary on their own; this combines them.
        [Fact]
        public void Adversarial8_RealAudiobook_DashShapedAnthology_LegitimateFlacRelease()
        {
            var result = Result(category: null, title: "Teodor Fenwick - Winter Tales Anthology [FLAC]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 2. Dash + GenreMarker + FLAC: an author-dash-title-shaped memoir about a music scene,
        //    archived in lossless FLAC.
        [Fact]
        public void Adversarial8_RealAudiobook_DashShapedGenreMemoir_LegitimateFlacRelease()
        {
            var result = Result(category: null, title: "Teodor Fenwick - Growing Up Loud (Punk) [FLAC]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 3. AlbumVocab + GenreMarker + FLAC, no dash: a genre-scene memoir framed as part of an
        //    anthology/collection series.
        [Fact]
        public void Adversarial8_RealAudiobook_GenreMemoirFramedAsAnthology_NoDash_LegitimateFlacRelease()
        {
            var result = Result(category: null, title: "Growing Up Loud (Punk): An Anthology [FLAC]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 4. Dash + GenreMarker + kbps: the only two components the (stricter) kbps gate counts
        //    at all, both present on an author-dash-title-shaped genre memoir whose subtitle
        //    legitimately references an archival bitrate.
        [Fact]
        public void Adversarial8_RealAudiobook_DashShapedGenreMemoir_LegitimateKbpsArchivalNote()
        {
            var result = Result(
                category: null,
                title: "Teodor Fenwick - Growing Up Loud (Punk): Archived at 56kbps");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // --- Sanity: confirm the boundary itself (exactly one signal still correctly gated off,
        // post-fix) ---

        [Fact]
        public void Sanity8_DashAlonePlusFlac_OneSignalOnly_StaysUnderThreshold()
        {
            var result = Result(category: null, title: "Teodor Fenwick - Winter Tales [FLAC]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void Sanity8_DashAlonePlusKbps_OneSignalOnly_StaysUnderThreshold()
        {
            var result = Result(category: null, title: "Teodor Fenwick - Life at 56kbps");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // Diagnostic-only: prints the reason string for the false-positive candidates above.
        [Fact]
        public void Diagnostic8_PrintReasonsForAllRound8Fixtures()
        {
            void Probe(string label, SearchResult r)
            {
                var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(r, out var reason);
                _output.WriteLine($"{label} => LooksLikeMusicRelease={looksLikeMusic}; reason=\"{reason}\"");
            }

            Probe("DashShapedAnthology_Flac",
                Result(category: null, title: "Teodor Fenwick - Winter Tales Anthology [FLAC]"));
            Probe("DashShapedGenreMemoir_Flac",
                Result(category: null, title: "Teodor Fenwick - Growing Up Loud (Punk) [FLAC]"));
            Probe("GenreMemoirAsAnthology_NoDash_Flac",
                Result(category: null, title: "Growing Up Loud (Punk): An Anthology [FLAC]"));
            Probe("DashShapedGenreMemoir_Kbps",
                Result(category: null, title: "Teodor Fenwick - Growing Up Loud (Punk): Archived at 56kbps"));
        }
    }
}
