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
    /// Round-7 independent-review adversarial fixtures against commit 8480d7d9d, which dropped
    /// Artist/Album consistency from the bitrate/FLAC gate. Deliberately NOT reusing round 1-6
    /// fixtures (now permanent regressions). Targets exactly the two open questions the
    /// implementer asked to re-attack: whether ordinary album vocabulary can gate FLAC the same
    /// way Artist/Album consistency falsely gated it, and the symmetric question for a genre
    /// marker gating kbps.
    ///
    /// All fixtures are synthetic: invented titles, authors and narrators, never a real title,
    /// author or narrator.
    /// </summary>
    [Trait("Name", "MusicReleaseClassifierReviewRound7Tests")]
    [Trait("Category", "Application")]
    public sealed class MusicReleaseClassifierReviewRound7Tests : BaseTests
    {
        private readonly ITestOutputHelper _output;

        public MusicReleaseClassifierReviewRound7Tests(ITestOutputHelper output)
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

        // 1. The requested disproof attempt: a real audiobook (a classic-recordings reissue,
        //    where "Remastered" is ordinary and accurate wording for an audio restoration) with
        //    an album-vocabulary word AND a genuine isolated, trailing "[FLAC]" tag -- plausible
        //    for an archival-quality lossless release of a legitimately public-domain audiobook.
        //    No dash, no Artist/Album fields, no category: isolates vocab+FLAC exactly.
        [Fact]
        public void Adversarial7_RealAudiobook_RemasteredReissue_LegitimateFlacArchivalRelease()
        {
            var result = Result(category: null, title: "The Classic Readings (Remastered Edition) [FLAC]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 2. Sanity/contrast: same shape, but with a correctly-applied 3030 category tag -- the
        //    negative category pull should be enough to keep this safe even though the gate
        //    itself doesn't look at category here (vocab alone already opens it). Confirms the
        //    bug in #1 specifically needs the ABSENCE of category info, which is common for
        //    indexers with sparse metadata.
        [Fact]
        public void Sanity7_RealAudiobook_RemasteredReissue_FlacRelease_WithCorrectCategoryTag_StaysSafe()
        {
            var result = Result(category: "3030", title: "The Classic Readings (Remastered Edition) [FLAC]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // 3. The symmetric question for kbps: a real nonfiction audiobook carrying a genre-word
        //    subtitle (round 2/3's own "(Punk)" shape -- a memoir/history about a music scene)
        //    that ALSO legitimately ends its own subtitle in a data-rate figure (an archival note
        //    about how the source recordings were digitized, not a release tag). No dash, no
        //    Artist/Album fields, no category: isolates genre+kbps exactly.
        [Fact]
        public void Adversarial7_RealAudiobook_GenreSubtitleMemoir_LegitimateKbpsArchivalNote()
        {
            var result = Result(category: null, title: "Growing Up Loud (Punk): Recorded and Archived at 56kbps");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 4. Sanity/contrast: same shape, correctly-tagged category 3030 -- confirms the bug in
        //    #3 also specifically needs the absence of category info.
        [Fact]
        public void Sanity7_RealAudiobook_GenreSubtitleMemoir_KbpsNote_WithCorrectCategoryTag_StaysSafe()
        {
            var result = Result(category: "3030", title: "Growing Up Loud (Punk): Recorded and Archived at 56kbps");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // 5. Sanity: vocab word alone with NO FLAC tag must stay under threshold (12 < 50),
        //    confirming #1 needs the FLAC tag specifically, not just the vocab word.
        [Fact]
        public void Sanity7_VocabAlone_NoFlacTag_StaysUnderThreshold()
        {
            var result = Result(category: null, title: "The Classic Readings (Remastered Edition)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // 6. Sanity: genre marker alone with NO kbps tag must stay under threshold (18 < 50),
        //    confirming #3 needs the kbps tag specifically, not just the genre marker.
        [Fact]
        public void Sanity7_GenreMarkerAlone_NoKbpsTag_StaysUnderThreshold()
        {
            var result = Result(category: null, title: "Growing Up Loud (Punk): A Memoir");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // Diagnostic-only: prints the reason string for the false-positive candidates above.
        [Fact]
        public void Diagnostic7_PrintReasonsForAllRound7Fixtures()
        {
            void Probe(string label, SearchResult r)
            {
                var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(r, out var reason);
                _output.WriteLine($"{label} => LooksLikeMusicRelease={looksLikeMusic}; reason=\"{reason}\"");
            }

            Probe("RealAudiobook_RemasteredReissue_FlacRelease",
                Result(category: null, title: "The Classic Readings (Remastered Edition) [FLAC]"));
            Probe("RealAudiobook_GenreSubtitleMemoir_KbpsArchivalNote",
                Result(category: null, title: "Growing Up Loud (Punk): Recorded and Archived at 56kbps"));
        }
    }
}
