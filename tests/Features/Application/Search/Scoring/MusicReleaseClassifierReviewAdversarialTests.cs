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
    /// Independent-reviewer adversarial negative controls for tracker #361's strengthened
    /// MusicReleaseClassifier. These fixtures were NOT written by the implementer and target
    /// combinations the implementer's own MusicReleaseClassifierStrengtheningTests negative
    /// controls do not cover -- specifically, title-only combinations of two "individually weak"
    /// new/widened signals that were never tested together without category or Artist/Album help.
    ///
    /// All fixtures are synthetic: invented titles, authors and narrators, never a real title,
    /// author or narrator.
    /// </summary>
    [Trait("Name", "MusicReleaseClassifierReviewAdversarialTests")]
    [Trait("Category", "Application")]
    public sealed class MusicReleaseClassifierReviewAdversarialTests : BaseTests
    {
        private readonly ITestOutputHelper _output;

        public MusicReleaseClassifierReviewAdversarialTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // REVIEW-DIAGNOSTIC ONLY: prints the reason string for every confirmed false-positive
        // fixture below, so the signal breakdown can be quoted in the review report. Not a
        // behavioral assertion; safe to delete once the report is written.
        [Fact]
        public void Diagnostic_PrintReasonsForConfirmedFalsePositives()
        {
            void Probe(string label, SearchResult r)
            {
                var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(r, out var reason);
                _output.WriteLine($"{label} => LooksLikeMusicRelease={looksLikeMusic}; reason=\"{reason}\"");
            }

            Probe("DashShapedMemoirTitle_Soundtrack",
                Result(category: null, title: "Teodor Fenwick - Soundtrack of My Years"));
            Probe("DashShapedSelfHelpTitle_Single",
                Result(category: null, title: "Dr. Marguerite Olin - Single and Unapologetic"));
            Probe("DashShapedAnthologyTitle",
                Result(category: null, title: "Rosalind Pike - Winter Tales Anthology"));
            Probe("ArtistAlbumDashShape_UnrelatedNonMusicCategory",
                Result(
                    category: "7020",
                    title: "Jonas Whitfield - The River Anthology",
                    artist: "Jonas Whitfield",
                    album: "The River Anthology"));
            Probe("MultiDashSeriesTitle_RemasteredVocabWord",
                Result(
                    category: null,
                    title: "Preston Ivy - The Lantern Chronicles - Remastered Edition Book Three"));
            Probe("BareGenreMarkerAsNovelSubtitle_Punk",
                Result(category: null, title: "Static and Silence (Punk)"));
        }

        [Fact]
        public void Adversarial_DecisiveMusicCategory_TitleAudiobookOverrideDoesNotRescue()
        {
            // "Vice versa" half of the domain-scoping claim: a decisive numeric music category id
            // (3010, weight 1000) must still reject even though the title carries an explicit
            // audiobook signal word ("narrated by") that protects the TITLE domain only.
            var result = Result(category: "3010", title: "Some Invented Title, narrated by Someone");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
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

        // 1. Dash-shaped "Author - Title" memoir whose title legitimately contains "Soundtrack"
        //    as an ordinary English word (a radio-host memoir), no category, no Artist/Album.
        //    TitleBareArtistAlbumDashWeight(20) + TitleAlbumVocabWeight(30) = 50, title-only.
        [Fact]
        public void Adversarial_DashShapedMemoirTitle_WithSoundtrackAsOrdinaryWord_NoOtherSignal()
        {
            var result = Result(category: null, title: "Teodor Fenwick - Soundtrack of My Years");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 2. Dash-shaped self-help book title containing "Single" as an ordinary English word.
        //    Same arithmetic as above, different vocab word, no brackets, no category.
        [Fact]
        public void Adversarial_DashShapedSelfHelpTitle_WithSingleAsOrdinaryWord_NoOtherSignal()
        {
            var result = Result(category: null, title: "Dr. Marguerite Olin - Single and Unapologetic");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 3. Dash-shaped short-story collection title containing "Anthology" (a word audiobooks use
        //    constantly), no brackets, no category, no Artist/Album fields populated at all.
        [Fact]
        public void Adversarial_DashShapedAnthologyTitle_NoCategoryNoFields()
        {
            var result = Result(category: null, title: "Rosalind Pike - Winter Tales Anthology");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 4. Bracketed non-bitrate tag: a bracket containing a word that happens to share
        //    characters with a bitrate token but is not one ("CD" as in "CD-ROM companion", or a
        //    plain "[Abridged]" edition note is already covered by the override -- use a
        //    legitimate "[Book Club Edition]" bracket that must not be mistaken for a bitrate tag).
        [Fact]
        public void Adversarial_BracketedNonBitrateEditionTag_IsNotMistakenForBitrateTag()
        {
            var result = Result(category: null, title: "The Long Road Home [Book Club Edition]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 5. Artist/Album populated AND a dash-shaped title AND a non-3030, non-empty, non-music
        //    numeric category (so neither the audiobook-category-token override nor the 3030
        //    counterweight protects it). This is the case neither override domain reaches.
        [Fact]
        public void Adversarial_ArtistAlbumDashShape_WithUnrelatedNonMusicNumericCategory()
        {
            // 7020 is an invented stand-in for a generic, non-audio, non-music numeric category id
            // outside the 3000-3040 audio range (e.g. an "Ebooks" or "Other" bucket some indexers
            // use for cross-listed audiobook releases) -- it carries no audiobook token text and no
            // music-range id, so neither absolute override nor the 3030 counterweight can apply.
            var result = Result(
                category: "7020",
                title: "Jonas Whitfield - The River Anthology",
                artist: "Jonas Whitfield",
                album: "The River Anthology");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 6. Long narrator/series credit string with multiple dashes -- the kind of unwieldy title
        //    some indexers produce for audiobooks: "Narrator - Series - Book Title".
        [Fact]
        public void Adversarial_MultiDashNarratorSeriesCreditString_NoOtherSignal()
        {
            var result = Result(
                category: null,
                title: "Narrated by Preston Ivy - The Lantern Chronicles - Book Three Anthology");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 7. Multi-dash credit string without any audiobook signal word at all (no "narrated by"),
        //    just author - series - title shape, with one widened vocab word riding along.
        [Fact]
        public void Adversarial_MultiDashSeriesTitle_WithoutAudiobookSignalWord_WithVocabWord()
        {
            var result = Result(
                category: null,
                title: "Preston Ivy - The Lantern Chronicles - Remastered Edition Book Three");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 8. A bare genre-marker parenthetical used as an evocative subtitle in a real-sounding
        //    novel title (not the implementer's own near-twin fixture): no category, no brackets,
        //    nothing else. Tests whether GenreMarkerPattern alone (110) over-fires on ordinary
        //    fiction/memoir subtitling.
        [Fact]
        public void Adversarial_BareGenreMarkerAsNovelSubtitle_NoOtherSignal()
        {
            var result = Result(category: null, title: "Static and Silence (Punk)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 9. Gibberish / junk category string: no digits, no recognizable words, not null -- must
        //    contribute nothing and the release must still fail open with no other signal. Title
        //    is deliberately neutral (no dash shape, no vocab word, no audiobook signal word) so
        //    the category's own ambiguity is what is actually under test, not the title override.
        [Fact]
        public void Adversarial_GibberishCategoryString_NoOtherSignal_FailsOpen()
        {
            var result = Result(category: "xqzw-mmbl-ghjk", title: "A Story of Two Rivers");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 10. Size sitting exactly at the small-threshold boundary (300 MiB), alone, nothing else.
        //     Title is neutral (no dash shape, no vocab word, no audiobook signal word) so the
        //     boundary behavior of the size signal itself is what is under test.
        [Fact]
        public void Adversarial_SizeExactlyAtSmallThresholdBoundary_Alone_FailsOpen()
        {
            var result = Result(category: null, title: "A Story of Two Rivers", size: 300L * 1024 * 1024);

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        // 11. One weak signal and nothing else: a single widened vocab word with no dash shape, no
        //     category, no Artist/Album, no size signal.
        [Fact]
        public void Adversarial_SingleWeakVocabWordAlone_NoDashShape_FailsOpen()
        {
            var result = Result(category: null, title: "The Remastered Edition of an Old Favorite Tale");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }
    }
}
