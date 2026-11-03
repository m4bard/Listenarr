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
    /// Round-9 independent-review adversarial fixtures against commit af1b38fa7's category-only
    /// bitrate/FLAC gate. Deliberately NOT reusing round 1-8 fixtures (now permanent
    /// regressions). Targets the one remaining lever: "categoryHasMusicSignal" is true not only
    /// for a decisive category match (which already rejects on its own, weight 1000, regardless
    /// of the bitrate gate) but also for the WEAK co-occurring-id shape (e.g. "3000,3030") that
    /// #336's own original tests establish is too weak to decide alone. That weak shape is a
    /// real, if uncommon, indexer quirk (a release correctly tagged Audiobooks that an indexer
    /// also multi-labels with an unrelated music-range id) -- and it is now sufficient, on its
    /// own, to unlock the bitrate/FLAC signal's full weight for a release that otherwise carries
    /// nothing but ordinary title-side signals.
    ///
    /// All fixtures are synthetic: invented titles, authors and narrators, never a real title,
    /// author or narrator.
    /// </summary>
    [Trait("Name", "MusicReleaseClassifierReviewRound9Tests")]
    [Trait("Category", "Application")]
    public sealed class MusicReleaseClassifierReviewRound9Tests : BaseTests
    {
        private readonly ITestOutputHelper _output;

        public MusicReleaseClassifierReviewRound9Tests(ITestOutputHelper output)
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

        // 1. A real audiobook, correctly carrying the Audiobooks category id, that an indexer
        //    ALSO multi-labels with an unrelated music-range id (the exact "3000,3030" shape
        //    #336's own original tests establish as a legitimate, too-weak-alone indexer quirk).
        //    It is a plausible lossless archival release of an anthology collection. Lands
        //    exactly AT the threshold.
        [Fact]
        public void Adversarial9_RealAudiobook_WeakCoOccurringCategory_AnthologyVocab_LegitimateFlacRelease()
        {
            var result = Result(
                category: "3000,3030",
                title: "The Lighthouse Keeper's Anthology [FLAC]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 2. Same weak co-occurring category shape, genre-themed memoir subtitle, legitimate
        //    kbps archival note.
        [Fact]
        public void Adversarial9_RealAudiobook_WeakCoOccurringCategory_GenreMemoir_LegitimateKbpsArchivalNote()
        {
            var result = Result(
                category: "3000,3030",
                title: "Growing Up Loud (Punk): Archived at 56kbps");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 3. Same weak co-occurring category shape, a real dash-shaped audiobook with
        //    consistent narrator/title Artist-Album fields, legitimate FLAC release.
        [Fact]
        public void Adversarial9_RealAudiobook_WeakCoOccurringCategory_DashArtistAlbum_LegitimateFlacRelease()
        {
            var result = Result(
                category: "3000,3030",
                title: "Elena Voss - The Clockmaker Chronicles [FLAC]",
                artist: "Elena Voss",
                album: "The Clockmaker Chronicles");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // --- Sanity: confirm the weak co-occurring category shape alone, with ONLY the bitrate
        // tag and no other weak title signal, stays safe (so the bug above needs the extra
        // signal, not just the category shape by itself) ---

        [Fact]
        public void Sanity9_WeakCoOccurringCategory_FlacAlone_NoOtherTitleSignal_StaysUnderThreshold()
        {
            var result = Result(category: "3000,3030", title: "An Ordinary Audiobook Title [FLAC]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void Sanity9_WeakCoOccurringCategory_DashAlone_FlacAlone_StaysUnderThreshold()
        {
            var result = Result(category: "3000,3030", title: "Teodor Fenwick - Winter Tales [FLAC]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // Diagnostic-only: prints the reason string for the false-positive candidates above.
        [Fact]
        public void Diagnostic9_PrintReasonsForAllRound9Fixtures()
        {
            void Probe(string label, SearchResult r)
            {
                var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(r, out var reason);
                _output.WriteLine($"{label} => LooksLikeMusicRelease={looksLikeMusic}; reason=\"{reason}\"");
            }

            Probe("WeakCoOccurCategory_AnthologyVocab_Flac",
                Result(category: "3000,3030", title: "The Lighthouse Keeper's Anthology [FLAC]"));
            Probe("WeakCoOccurCategory_GenreMemoir_Kbps",
                Result(category: "3000,3030", title: "Growing Up Loud (Punk): Archived at 56kbps"));
            Probe("WeakCoOccurCategory_DashArtistAlbum_Flac",
                Result(
                    category: "3000,3030",
                    title: "Elena Voss - The Clockmaker Chronicles [FLAC]",
                    artist: "Elena Voss",
                    album: "The Clockmaker Chronicles"));
        }
    }
}
