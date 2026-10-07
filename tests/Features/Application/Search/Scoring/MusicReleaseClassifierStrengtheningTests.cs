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

namespace Listenarr.Tests.Features.Application.Search.Scoring
{
    /// <summary>
    /// Tracker #361: strengthens #336's MusicReleaseClassifier against measured weaknesses --
    /// category 3030 (Audiobooks) unconditionally exonerating a release regardless of
    /// trustworthiness, and the strict scene-release title regex missing most real-world
    /// mislabeled music uploads. Covers all four approved strengthenings:
    ///   (a) Artist/Album fields as a corroborating signal
    ///   (b) widened title-pattern matching (bare "Artist - Album", album vocabulary, bitrate
    ///       tags without a year prefix, parenthetical genre markers)
    ///   (c) category 3030 as a weighted signal, not an absolute override
    ///   (d) Size as a weak corroborating nudge only
    ///
    /// All fixtures are synthetic: invented titles and artists, never a real title or author.
    /// </summary>
    [Trait("Name", "MusicReleaseClassifierStrengtheningTests")]
    [Trait("Category", "Application")]
    public sealed class MusicReleaseClassifierStrengtheningTests : BaseTests
    {
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

        // ---------------------------------------------------------------------------------
        // Measured-but-missed shapes (RED against the current #336 classifier, GREEN after
        // the strengthening). These are invented equivalents of the two shapes measured on
        // the high side, not the real near-twin.
        // ---------------------------------------------------------------------------------

        [Fact]
        public void PopNearTwin_MislabeledMusicWithGenreMarker_IsCaught()
        {
            // The legitimate audiobook is "Harbor Lights" (see the sibling test below). This is
            // the mislabeled music release: near-identical wording, a mislabeling indexer's
            // category-3030 tag, and a parenthetical genre marker plus Artist/Album fields that
            // are internally consistent with the title's own "Artist - Album" shape.
            var result = Result(
                category: "3030",
                title: "Jonas Harrow - Harbor Lights (Pop)",
                artist: "Jonas Harrow",
                album: "Harbor Lights (Pop)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        [Fact]
        public void PopNearTwin_LegitimateAudiobookCounterpart_IsNotRejected()
        {
            // Same title family, correctly tagged, no genre marker, no Artist/Album fields -- the
            // near-twin this strengthening must not catch as collateral damage.
            var result = Result(category: "3030", title: "Harbor Lights");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
            Assert.Null(reason);
        }

        [Fact]
        public void MusicSingle_NotAlbum_IsCaught()
        {
            // A single-track release, not an album: no year, no "Discography", nothing the
            // original strict scene-album regex or the discography keyword could ever match.
            var result = Result(category: null, title: "Mira Delgado - Nightfall (Single) [320]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        // ---------------------------------------------------------------------------------
        // (a) Artist/Album as a corroborating signal
        // ---------------------------------------------------------------------------------

        [Fact]
        public void ArtistAlbumConsistentWithDashTitle_CorroboratesAlongsideOtherSignals()
        {
            // Covered end-to-end by PopNearTwin_MislabeledMusicWithGenreMarker_IsCaught above;
            // this isolates the Artist/Album contribution by removing the genre marker and
            // category signal -- bare dash + consistent fields alone must NOT be enough (this is
            // the same shape a "Narrator - Series" audiobook title can legitimately have; the
            // negative controls below exercise that directly).
            var result = Result(
                category: null,
                title: "Soraya Quint - Midnight Transit",
                artist: "Soraya Quint",
                album: "Midnight Transit");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void ArtistAlbumFields_PlusOneWeakTitleSignal_CrossesThreshold()
        {
            // Dash + consistent Artist/Album fields alone is not enough (see above), but combined
            // with one more weak signal (a bitrate tag) it corroborates enough to catch a real
            // mislabeled release.
            var result = Result(
                category: null,
                title: "Soraya Quint - Midnight Transit [FLAC]",
                artist: "Soraya Quint",
                album: "Midnight Transit");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        // ---------------------------------------------------------------------------------
        // (b) Widened title-pattern matching
        // ---------------------------------------------------------------------------------

        [Theory]
        [InlineData("Nightfall Sessions EP [320]")]
        [InlineData("Harmonic Drift LP [FLAC]")]
        [InlineData("The Lowland Tapes Anthology [320]")]
        [InlineData("Carved In Static Soundtrack [FLAC]")]
        public void AlbumVocabularyPlusBitrateTag_IsCaught(string title)
        {
            var result = Result(category: null, title: title);

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        [Theory]
        [InlineData("(Pop)")]
        [InlineData("(Pop/Rock)")]
        [InlineData("(Hip-Hop)")]
        public void ParentheticalGenreMarker_Alone_IsDecisive(string marker)
        {
            // A standalone parenthetical genre marker is unambiguous enough on its own -- real
            // audiobook titles essentially never carry one.
            var result = Result(category: null, title: $"Driftwood Station {marker}");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        [Fact]
        public void DescriptiveParenthetical_ContainingGenreWordAsSubstring_DoesNotMatchGenreMarker()
        {
            // "(Rock Climbing Memoir)" must not trip the genre-marker pattern just because "Rock"
            // appears inside a longer, ordinary descriptive parenthetical.
            var result = Result(category: null, title: "Above the Clouds (Rock Climbing Memoir)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // ---------------------------------------------------------------------------------
        // (c) Category 3030 as a weighted signal, not an absolute override
        // ---------------------------------------------------------------------------------

        [Fact]
        public void Category3030_WithStrongContraryTitleAndFieldSignals_IsOverridden()
        {
            // Restates PopNearTwin_MislabeledMusicWithGenreMarker_IsCaught as a direct (c) test:
            // 3030 is present, but genre marker + dash + Artist/Album corroboration together
            // outweigh it.
            var result = Result(
                category: "3030",
                title: "Nadia Brecht - Low Tide (Rock)",
                artist: "Nadia Brecht",
                album: "Low Tide (Rock)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        [Fact]
        public void Category3030_WithOnlyWeakContraryTitleSignal_StillFailsOpen()
        {
            // 3030 plus a single weak vocabulary word is not enough to override -- this is the
            // fail-open discipline (c) must preserve: ambiguous evidence still loses to 3030.
            var result = Result(category: "3030", title: "The Lighthouse Keeper's Anthology");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
            Assert.Null(reason);
        }

        // ---------------------------------------------------------------------------------
        // (d) Size as a weak corroborating nudge only
        // ---------------------------------------------------------------------------------

        [Fact]
        public void SmallSize_Alone_IsNeverDecisive()
        {
            var result = Result(category: null, title: "An Ordinary Audiobook Title", size: 40_000_000);

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void LargeSize_CannotRescueAClearMusicSignal()
        {
            // Size can never flip a clear, already-decisive accept/reject by itself: a strict
            // scene-album title match stays rejected even with an audiobook-typical large size.
            var result = Result(
                category: null,
                title: "Some Artist - Some Album (2019) [FLAC]",
                size: 900_000_000);

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        [Fact]
        public void TinySize_CannotCondemnAClearAudiobookSignal()
        {
            // Symmetric case: 3030 alone with a tiny, music-typical size still fails open.
            var result = Result(category: "3030", title: "An Ordinary Audiobook Title", size: 20_000_000);

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
            Assert.Null(reason);
        }

        [Fact]
        public void SmallSize_NudgesABorderlineCaseAcrossTheThreshold()
        {
            // A single weak vocabulary word alone is below threshold (see
            // Category3030_WithOnlyWeakContraryTitleSignal_StillFailsOpen's unweighted sibling
            // below); a small, music-typical size is the nudge that tips an otherwise-borderline
            // case over.
            var withoutSizeNudge = Result(category: null, title: "Nightfall Sessions EP", size: 0);
            var withSizeNudge = Result(category: null, title: "Nightfall Sessions EP", size: 40_000_000);

            var withoutNudgeResult = MusicReleaseClassifier.LooksLikeMusicRelease(withoutSizeNudge, out _);
            var withNudgeResult = MusicReleaseClassifier.LooksLikeMusicRelease(withSizeNudge, out var reason);

            Assert.False(withoutNudgeResult);
            Assert.True(withNudgeResult);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        // ---------------------------------------------------------------------------------
        // Negative controls: synthetic audiobooks constructed specifically to try to trip the
        // widened heuristics. These measure the false-positive rate; every one must be accepted.
        // ---------------------------------------------------------------------------------

        [Fact]
        public void NegativeControl_RemasteredInRealAudiobookTitle_WithCategoryTag_IsAccepted()
        {
            var result = Result(category: "3030", title: "Winterfall Keep (Remastered Edition)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_RemasteredInRealAudiobookTitle_NoCategoryTag_IsAccepted()
        {
            var result = Result(category: null, title: "Winterfall Keep (Remastered Edition)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_AnthologyInRealAudiobookTitle_IsAccepted()
        {
            var result = Result(category: null, title: "Myths of the North: An Anthology");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_SoundtrackInRealAudiobookTitle_IsAccepted()
        {
            var result = Result(category: "3030", title: "The Soundtrack of My Life: A Memoir");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ArtistAlbumPopulatedWithNarratorSeries_NoTitleShapeMatch_IsAccepted()
        {
            // Artist/Album carry narrator/series info rather than being empty, but the title does
            // not have the "Artist - Album" dash shape at all -- the corroboration signal must
            // never fire here.
            var result = Result(
                category: null,
                title: "The Clockmaker's Apprentice",
                artist: "Elena Voss",
                album: "The Clockmaker Chronicles");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ArtistAlbumPopulatedWithNarratorSeries_HardestCase_DashTitleShapeToo_IsAccepted()
        {
            // The hardest negative control: Artist/Album hold narrator/series info AND the title
            // happens to have the exact "X - Y" dash shape that matches them, with no category
            // signal at all to help. Structurally indistinguishable from a real "Artist - Album"
            // music title from the fields alone. This is the case the operator specifically asked
            // to be measured honestly.
            var result = Result(
                category: null,
                title: "Elena Voss - The Clockmaker Chronicles",
                artist: "Elena Voss",
                album: "The Clockmaker Chronicles");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ArtistAlbumPopulatedWithNarratorSeries_HardestCase_WithCorrectCategoryTag_IsAccepted()
        {
            // Same hardest shape as above, but with a correctly-applied 3030 tag -- the common
            // real-world case, where the category signal is the thing that protects it.
            var result = Result(
                category: "3030",
                title: "Elena Voss - The Clockmaker Chronicles",
                artist: "Elena Voss",
                album: "The Clockmaker Chronicles");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_BracketedUnabridgedTag_IsAccepted()
        {
            // A bracket is present, but the word inside is an explicit audiobook signal, not a
            // bitrate tag -- must never be confused with the widened bitrate-tag pattern.
            var result = Result(category: null, title: "An Audiobook Memoir [Unabridged Edition]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_SmallLegitimateShortAudiobook_IsAccepted()
        {
            // A short, legitimately small audiobook file -- Size must not condemn it alone.
            var result = Result(category: "3030", title: "A Short Walk at Dusk", size: 80_000_000);

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }
    }
}
