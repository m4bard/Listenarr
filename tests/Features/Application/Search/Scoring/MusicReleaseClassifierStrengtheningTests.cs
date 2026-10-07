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
    /// mislabeled music uploads. Covers the four approved strengthenings as they stand after
    /// nine rounds of independent review:
    ///   (a) Artist/Album fields as a corroborating signal
    ///   (b) widened title-pattern matching (bare "Artist - Album", album vocabulary,
    ///       parenthetical genre markers)
    ///   (c) category 3030 as a weighted signal, not an absolute override
    ///   (d) Size as a weak corroborating nudge only
    ///
    /// A fifth signal, a bitrate/FLAC/kbps release-tag match, was part of (b)'s original scope
    /// and was REMOVED after rounds 2-9 of independent review (operator decision): no
    /// title-derived corroborator could gate it without becoming a false-positive source, and
    /// the one category-side corroborator narrow enough to be safe (an actual decisive music
    /// category signal) made it structurally unreachable -- a release whose category is already
    /// decisive rejects on that alone, with or without the bitrate tag. Fixtures that
    /// demonstrated the removed signal are gone; fixtures that disprove the specific corroborator
    /// choices tried along the way remain as permanent regressions, since those prove the
    /// classifier doesn't quietly reintroduce the behavior removal fixed.
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
        // the strengthening). Originally two fixtures here: an invented "Pop near-twin" (a
        // legitimate audiobook and a mislabeled music release sharing near-identical wording)
        // and an invented mislabeled music single. Both relied on the bitrate/FLAC/kbps signal
        // removed after round 9 (operator decision; see the class doc comment and the removal
        // commit for the full nine-round history) -- category-only gating made that signal
        // structurally unreachable alongside anything weaker than an already-decisive category
        // signal, which these fixtures were specifically designed NOT to need. Both are
        // REMOVED, not rewritten: the pattern they tested (mislabeled music distinguishable from
        // a real near-twin, or a single distinguishable from an album, using ONLY title
        // signals plus a non-decisive category) is exactly the pattern nine rounds of review
        // showed is not safely distinguishable with the signals available. The RED-before-GREEN
        // discipline that motivated them is preserved in TDD practice for any future signal;
        // it does not require keeping a test for a mechanism that no longer exists.
        // PopNearTwin_LegitimateAudiobookCounterpart_IsNotRejected is kept below as a plain
        // negative control (it never depended on the removed signal).
        // ---------------------------------------------------------------------------------

        [Fact]
        public void PopNearTwin_LegitimateAudiobookCounterpart_IsNotRejected()
        {
            // A correctly-tagged, ordinary audiobook title -- no genre marker, no Artist/Album
            // fields, nothing ambiguous. Must stay accepted regardless of how (b)'s title
            // signals evolve.
            var result = Result(category: "3030", title: "Harbor Lights");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
            Assert.Null(reason);
        }

        // ---------------------------------------------------------------------------------
        // (a) Artist/Album as a corroborating signal
        // ---------------------------------------------------------------------------------

        [Fact]
        public void ArtistAlbumConsistentWithDashTitle_CorroboratesAlongsideOtherSignals()
        {
            // Bare dash + consistent Artist/Album fields alone must NOT be enough (this is the
            // same shape a "Narrator - Series" audiobook title can legitimately have; the
            // negative controls below exercise that directly). Round 6 of independent review
            // (tracker #361) went further and proved this signal can never safely gate anything
            // stronger either -- see the class doc comment and the removal commit for why the
            // bitrate/FLAC/kbps signal this test once combined with no longer exists.
            var result = Result(
                category: null,
                title: "Soraya Quint - Midnight Transit",
                artist: "Soraya Quint",
                album: "Midnight Transit");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // ---------------------------------------------------------------------------------
        // (b) Widened title-pattern matching
        // ---------------------------------------------------------------------------------

        [Theory]
        [InlineData("(Pop)")]
        [InlineData("(Pop/Rock)")]
        [InlineData("(Hip-Hop)")]
        public void ParentheticalGenreMarker_Alone_RequiresCorroboration_IsNotDecisive(string marker)
        {
            // Independent review (tracker #361) found a plausible counterexample -- a memoir or
            // nonfiction audiobook subtitled with a bare genre word, e.g. "(Punk)" for a book
            // about punk culture -- so a standalone parenthetical genre marker must NOT decide
            // alone. It still needs at least one more weak signal to corroborate (see the
            // combination test below).
            var result = Result(category: null, title: $"Driftwood Station {marker}");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void ParentheticalGenreMarker_PlusBareDashShape_StillBelowThreshold()
        {
            // Genre marker + a bare dash shape alone is still not enough -- and, unlike an
            // earlier tuning, not even a small Size nudge can tip this specific pair (margin to
            // RejectThreshold is kept well above SizeWeakWeight for every pair of the four
            // ambiguous signals; see SmallSize_NudgesABorderlineCaseAcrossTheThreshold below for
            // the one combination -- all four ambiguous signals together -- that Size can tip).
            var result = Result(category: null, title: "Nova Fenn - Driftwood Station (Pop)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
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
        //
        // The architecture here is unchanged -- CategoryAudiobooksIdWeight (line declaration in
        // MusicReleaseClassifier.cs) is still a weighted pull, not an absolute return, so a
        // future signal strong enough to combine with it could still override it. But the one
        // demonstration of an actual override in this file relied on the removed bitrate/FLAC/
        // kbps signal (confirmed by brute-force enumeration: even the maximally-corroborated
        // combination of all four remaining ambiguous title signals plus a Size nudge, alongside
        // a weak co-occurring category id, totals 44 -- short of RejectThreshold). No override
        // is currently constructible with the signals that remain, so only the fail-open side of
        // (c) has a live test below.
        // ---------------------------------------------------------------------------------

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
            // Independent review (tracker #361, round 2) found that a 2-signal pair sitting too
            // close to RejectThreshold let a small Size nudge tip a plausible real audiobook
            // ("Jonas Harrow - Low Tide (Rock)", genre marker + dash alone) into a false
            // positive. Every pair and triple of the four ambiguous signals now sits with a
            // margin well above SizeWeakWeight (see the constants' own comment), so Size can
            // only tip the ONE case with all four ambiguous signals corroborating at once: a
            // dash-shaped title, one album-vocabulary word, a genre marker, AND Artist/Album
            // fields consistent with that same dash shape, all at once -- already a great deal
            // of independent corroboration before Size adds anything.
            const string title = "Nova Fenn - Midnight Static (Pop) EP";
            const string artist = "Nova Fenn";
            const string album = "Midnight Static (Pop) EP";

            var withoutSizeNudge = Result(category: null, title: title, artist: artist, album: album, size: 0);
            var withSizeNudge = Result(category: null, title: title, artist: artist, album: album, size: 40_000_000);

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

        // ---------------------------------------------------------------------------------
        // Regression fixtures from independent review (tracker #361): six false positives
        // measured against an earlier tuning of these same weights, where a bare "Artist -
        // Album" dash shape plus ONE ordinary album-vocabulary word (no other corroboration at
        // all) reached RejectThreshold on ordinary dash-formatted audiobook/memoir/self-help
        // titles. Permanent negative controls so this exact gap cannot silently reopen if the
        // weights are retuned later.
        // ---------------------------------------------------------------------------------

        [Theory]
        [InlineData("Teodor Fenwick - Soundtrack of My Years")]
        [InlineData("Dr. Marguerite Olin - Single and Unapologetic")]
        [InlineData("Rosalind Pike - Winter Tales Anthology")]
        [InlineData("Preston Ivy - The Lantern Chronicles - Remastered Edition Book Three")]
        public void NegativeControl_ReviewRegression_DashShapePlusOneVocabWord_NoOtherSignal_IsAccepted(string title)
        {
            var result = Result(category: null, title: title);

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ReviewRegression_DashShapePlusVocabPlusArtistAlbum_NonMusicCategory_IsAccepted()
        {
            // Artist/Album fields are consistent with the title's dash shape AND there is one
            // ordinary vocabulary word, but the category is a real, non-music, non-3030 id (e.g.
            // a general-fiction category) -- still not enough without a bitrate tag or genre
            // marker to corroborate.
            var result = Result(
                category: "7020",
                title: "Jonas Whitfield - The River Anthology",
                artist: "Jonas Whitfield",
                album: "The River Anthology");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ReviewRegression_BareGenreMarkerAsNovelSubtitle_IsAccepted()
        {
            // A music-genre word in parens can legitimately be a subtitle on a book ABOUT that
            // genre (a punk-scene memoir, a jazz biography, ...) rather than evidence the release
            // itself is music.
            var result = Result(category: null, title: "Static and Silence (Punk)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // ---------------------------------------------------------------------------------
        // Round 2 of independent review found a second batch of false positives after round 1's
        // fix, under the tuning that preceded this one: GenreMarker+AlbumVocab crossing together
        // (an oversight against the invariant round 1's own fix stated), Size nudging a
        // GenreMarker+BareDash pair that sat too close to the threshold, and the then-wider
        // bitrate-tag pattern matching MP3/CD -- common, non-discriminating tokens, not the
        // scene-specific ones. Permanent negative controls for all six of round 2's fixtures.
        // ---------------------------------------------------------------------------------

        [Fact]
        public void NegativeControl_ReviewRound2_GenreMarkerPlusAlbumVocab_NoOtherSignal_IsAccepted()
        {
            var result = Result(category: null, title: "Life After the Scene (Punk): An Anthology of Essays");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ReviewRound2_GenreMarkerPlusAlbumVocab_IrrelevantCategory_IsAccepted()
        {
            var result = Result(category: "7020", title: "Life After the Scene (Punk): An Anthology of Essays");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ReviewRound2_AlbumVocabPlusGenericEditionBracket_IsAccepted()
        {
            // "[CD Companion Edition]" must not be confused with a bitrate/format tag -- "CD"
            // alone means nothing more specific than "compact disc".
            var result = Result(category: null, title: "The Remastered Letters [CD Companion Edition]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ReviewRound2_DashPlusGenreMarker_PlusSmallSize_IsAccepted()
        {
            // The exact combination round 2 found crossing under the preceding tuning: a small,
            // plausible real audiobook with a dash-shaped title and a genre-word subtitle.
            var result = Result(category: null, title: "Jonas Harrow - Low Tide (Rock)", size: 40_000_000);

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ReviewRound2_DashPlusArtistAlbumPlusMp3Tag_IsAccepted()
        {
            // MP3 is the single most common real-world audiobook distribution format -- it must
            // never corroborate a music signal the way a genuinely scene-specific tag does.
            var result = Result(
                category: null,
                title: "Elena Voss - The Clockmaker Chronicles [MP3]",
                artist: "Elena Voss",
                album: "The Clockmaker Chronicles");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ReviewRound2_DashPlusAlbumVocabPlusGenreMarker_IsAccepted()
        {
            var result = Result(category: null, title: "Priya Nandakumar - Echoes (Jazz): An Anthology");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // ---------------------------------------------------------------------------------
        // Round 3 of independent review found the fix for round 2's token problem (dropping
        // MP3/WEB/CD/128/192/256) had the same problem on a different axis: the surviving
        // V0/V1/V2/320 tokens are not scene-specific either. "V1"/"V2" are the standard
        // book-series volume abbreviation, and a bare "320" is an ordinary page count. Fixed by
        // tightening BitrateTagPattern itself (FLAC, or a number with the literal "kbps" unit)
        // rather than retuning weights a third time. Permanent negative controls for all four of
        // round 3's fixtures.
        // ---------------------------------------------------------------------------------

        [Fact]
        public void NegativeControl_ReviewRound3_DashPlusPageCountInParens_IsAccepted()
        {
            var result = Result(category: null, title: "Teodor Fenwick - A Long Walk (320 Pages)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ReviewRound3_DashPlusVolumeAbbreviation_IsAccepted()
        {
            var result = Result(category: null, title: "Priya Nandakumar - Chronicles of the North (V1)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ReviewRound3_AlbumVocabPlusVolumeAbbreviation_NoDash_IsAccepted()
        {
            var result = Result(category: null, title: "The Second Anthology (V2)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ReviewRound3_DashPlusArtistAlbumPlusVolumeAbbreviation_PlausibleSeriesAudiobook_IsAccepted()
        {
            // The most concrete case: a plausible real series-audiobook upload with a dash-shaped
            // title, matching Artist/Album fields (narrator/series), and a volume marker -- no
            // category at all to help.
            var result = Result(
                category: null,
                title: "Priya Nandakumar - Chronicles of the North (V1)",
                artist: "Priya Nandakumar",
                album: "Chronicles of the North (V1)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // ---------------------------------------------------------------------------------
        // Round 4 of independent review found a narrower but real gap after round 3's token
        // tightening: an audiobook ABOUT audio technology, where "FLAC" or a kbps figure is the
        // book's own subject matter, embedded in ordinary prose, not a release tag. Fixed
        // structurally rather than lexically -- BitrateTagPattern now requires the token to sit
        // in a release-tag-SHAPED position (the end of the title, and a bracketed FLAC contains
        // nothing else), since a real release tag is conventionally the last thing in a title and
        // nothing else. Permanent negative controls for all four of round 4's fixtures.
        // ---------------------------------------------------------------------------------

        [Fact]
        public void NegativeControl_ReviewRound4_FlacAsBookSubjectMidTitle_IsAccepted()
        {
            var result = Result(category: null, title: "Jordan Pierce - The Vinyl Revival (FLAC Format Explained)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ReviewRound4_KbpsAsBookSubjectNotAtEnd_IsAccepted()
        {
            var result = Result(category: null, title: "Jordan Pierce - Engineering Sound: 128kbps and Beyond");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ReviewRound4_AlbumVocabPlusKbpsAsBookSubjectNotAtEnd_IsAccepted()
        {
            var result = Result(category: null, title: "The Remastered History of 128kbps Streaming");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ReviewRound4_FlacAsFictionalAcronymMidTitle_IsAccepted()
        {
            var result = Result(category: null, title: "Senator Avery Lin - The Silent Vote (FLAC Resistance)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // ---------------------------------------------------------------------------------
        // Round 5 of independent review found that "kbps" is a general digital data-rate unit,
        // not an audio-specific one -- a telecom/networking nonfiction subtitle ending in a
        // bitrate figure ("...Life at 56kbps") sits in the exact same end-of-title position a
        // real release tag would, so round 4's positional fix alone couldn't tell them apart.
        // Fixed by gating the signal on a genuinely music-specific corroborator (Artist/Album
        // corroboration, or an actual music category signal) rather than narrowing the regex a
        // third time -- and deliberately NOT accepting ordinary album vocabulary as a
        // corroborator for the kbps path specifically, because a vocabulary word plus a trailing
        // kbps figure is exactly the shape that is ambiguous between a real mislabeled release
        // and an ordinary nonfiction title (vocabulary alone WAS carved out as a sufficient
        // corroborator for FLAC, which has no comparable ambiguity). Permanent negative controls
        // for all three of round 5's fixtures.
        // ---------------------------------------------------------------------------------

        [Fact]
        public void NegativeControl_ReviewRound5_DashPlusKbpsAsBookSubjectAtTitleEnd_NoCorroborator_IsAccepted()
        {
            var result = Result(category: null, title: "Jordan Pierce - The Streaming Wars: Why Everyone Settled on 128kbps");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ReviewRound5_DashPlusKbpsAsTelecomHistorySubject_NoCorroborator_IsAccepted()
        {
            // The sharpest case: not even about audio. "kbps" is a general data-rate unit
            // (modem speeds, telecom history), with zero connection to music.
            var result = Result(category: null, title: "Jordan Pierce - The Dial-Up Years: Life at 56kbps");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ReviewRound5_AlbumVocabPlusKbps_VocabIsNotASufficientKbpsCorroborator_IsAccepted()
        {
            // The case that proves vocabulary cannot be accepted as a kbps corroborator: this is
            // structurally identical (one album-vocabulary word + a trailing kbps figure, nothing
            // else) to a real mislabeled release this gate is supposed to catch, and the two
            // cannot be told apart by vocabulary alone.
            var result = Result(category: null, title: "The Remastered Story of Life at 56kbps");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // ---------------------------------------------------------------------------------
        // Round 6 of independent review found that Artist/Album consistency (CollectArtistAlbum-
        // Signal) was never a music-specific corroborator at all -- it checks whether the fields
        // are internally consistent with the title's dash shape, and a real audiobook with
        // narrator-as-Artist/title-as-Album metadata is exactly as consistent as a real
        // mislabeled release. Round 1 built this signal specifically to tolerate that case (see
        // its own hardest negative control); using it to gate the bitrate/FLAC signal
        // contradicted what it was built to do from day one. Fixed by dropping Artist/Album
        // consistency from the gate entirely -- it still contributes its own weight to the sum,
        // it just can no longer unlock the bitrate signal's weight. Permanent negative controls
        // for both of round 6's fixtures.
        // ---------------------------------------------------------------------------------

        [Fact]
        public void NegativeControl_ReviewRound6_RealAudiobookWithConsistentFieldsAndFlacRelease_IsAccepted()
        {
            var result = Result(
                category: null,
                title: "Elena Voss - The Clockmaker Chronicles [FLAC]",
                artist: "Elena Voss",
                album: "The Clockmaker Chronicles");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ReviewRound6_RealAudiobookWithConsistentFieldsAndKbpsSubtitle_IsAccepted()
        {
            var result = Result(
                category: null,
                title: "Jordan Pierce - The Dial-Up Years: Life at 56kbps",
                artist: "Jordan Pierce",
                album: "The Dial-Up Years: Life at 56kbps");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // ---------------------------------------------------------------------------------
        // Round 7 of independent review disproved the remaining two single-signal corroborators
        // round 6 left standing: album vocabulary alone cannot gate FLAC, and a genre marker
        // alone cannot gate kbps, for the same reason Artist/Album consistency couldn't -- both
        // were explicitly designed (their own code comments say so) to also describe real
        // audiobook content, not just music. Fixed by requiring genuine multi-signal convergence
        // (at least two of {album vocabulary, genre marker, bare dash}, excluding Artist/Album
        // entirely) or an actual music category signal, rather than any one title-derived signal
        // alone. Permanent negative controls for both of round 7's fixtures.
        // ---------------------------------------------------------------------------------

        [Fact]
        public void NegativeControl_ReviewRound7_AlbumVocabAloneCannotGateFlac_IsAccepted()
        {
            // A plausible archival-quality lossless reissue of a public-domain audiobook --
            // "Remastered" used accurately, no second title corroborator present.
            var result = Result(category: null, title: "The Classic Readings (Remastered Edition) [FLAC]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ReviewRound7_GenreMarkerAloneCannotGateKbps_IsAccepted()
        {
            // A punk-scene memoir whose subtitle legitimately references the archival bitrate of
            // its own source recordings -- no second title corroborator present.
            var result = Result(category: null, title: "Growing Up Loud (Punk): Recorded and Archived at 56kbps");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // ---------------------------------------------------------------------------------
        // Round 8 of independent review disproved every two-signal pairing of {album vocabulary,
        // genre marker, bare dash} as a Bitrate/FLAC/kbps gate -- the closing finding of four
        // consecutive rounds (5, 6, 7, 8), each disproving the prior round's choice of
        // "music-specific" title-derived corroborator. The reviewer's diagnosis: a bare
        // "Author - Title" dash shape is one of the dominant REAL audiobook naming conventions,
        // not a rare coincidence, so pairing it with any other signal that also legitimately
        // appears in real audiobook titles (an anthology collection, a genre-themed memoir) is an
        // ordinary shape, not a narrow one. Operator decision: Bitrate/FLAC/kbps now gates on an
        // actual music category signal ONLY -- no title-derived signal, alone or combined,
        // unlocks it any more (see LooksLikeMusicRelease's gating comment for the full history).
        // One consequence of this decision, accepted explicitly: the original no-category
        // "music single via a bare bitrate tag" required test case is retired (see
        // MusicSingle_NotAlbum_IsCaught_WithCategorySignal above for the retirement note and its
        // replacement). Permanent negative controls for all four of round 8's fixtures -- every
        // one of these must now be correctly ACCEPTED, where under the round-7 two-signal gate
        // they were false positives.
        // ---------------------------------------------------------------------------------

        [Fact]
        public void NegativeControl_ReviewRound8_DashPlusAlbumVocab_NoCategory_IsAccepted()
        {
            var result = Result(category: null, title: "Teodor Fenwick - Winter Tales Anthology [FLAC]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ReviewRound8_DashPlusGenreMarker_NoCategory_IsAccepted()
        {
            var result = Result(category: null, title: "Teodor Fenwick - Growing Up Loud (Punk) [FLAC]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ReviewRound8_GenreMarkerPlusAlbumVocab_NoDash_NoCategory_IsAccepted()
        {
            var result = Result(category: null, title: "Growing Up Loud (Punk): An Anthology [FLAC]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void NegativeControl_ReviewRound8_DashPlusGenreMarkerPlusKbps_NoCategory_IsAccepted()
        {
            var result = Result(category: null, title: "Teodor Fenwick - Growing Up Loud (Punk): Archived at 56kbps");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out _);

            Assert.False(looksLikeMusic);
        }

        // ---------------------------------------------------------------------------------
        // Domain-scoping, both directions (independent review verified this by direct test
        // rather than by reading alone -- QualityProfileMusicCategoryGateTests' shared fixture
        // title always carries "(Unabridged)", which only exercises the title-overrides-category
        // direction trivially since its category is never itself decisive there).
        // ---------------------------------------------------------------------------------

        [Fact]
        public void DecisiveMusicCategory_TitleAudiobookOverride_DoesNotRescue()
        {
            // A category-decisive music id must reject even when the title independently carries
            // an explicit audiobook signal word -- the title override protects only the title
            // domain's own contribution, it does not reach across and cancel a category-decided
            // outcome.
            var result = Result(category: "3010", title: "Some Invented Title, narrated by Someone");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }
    }
}
