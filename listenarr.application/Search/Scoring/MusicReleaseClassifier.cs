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
using System.Text.RegularExpressions;

namespace Listenarr.Application.Search.Scoring
{
    /// <summary>
    /// Narrow, conservative detector for "this release is clearly music, not an audiobook".
    /// Only consulted when the operator has opted into ApplicationSettings.RejectClearlyMusicReleases
    /// (see SearchResultScorer). Fails open by design: every path that cannot establish a net
    /// positive music signal returns false, including missing category, ambiguous category, and
    /// plain titles.
    ///
    /// Tracker #361 strengthening: signals are combined into a weighted score rather than any
    /// single boolean check deciding outright. Every signal #336 originally shipped with keeps a
    /// weight far above <see cref="RejectThreshold"/> on its own, so none of its original
    /// behavior changes. Everything new or weakened here -- category 3030's co-occurrence
    /// weight, the widened title vocabulary, Artist/Album corroboration, Size -- is deliberately
    /// too small to decide anything alone; they only add up when several corroborate together.
    ///
    /// Two checks remain absolute, unweighted overrides, exactly as before: an explicit
    /// audiobook token in the category text, and an explicit audiobook signal word in the
    /// title. Each protects only its own domain -- a title override can never rescue a release a
    /// decisive category match already condemned, and vice versa. That split is deliberate: it's
    /// what lets a category id alone decide <c>QualityProfileMusicCategoryGateTests</c>' shared
    /// fixture title, which always carries "(Unabridged)".
    /// </summary>
    public static class MusicReleaseClassifier
    {
        // Newznab/Torznab "Audio" parent category (3000) and its subcategories, per the standard
        // Newznab taxonomy. 3030 is Audiobooks.
        private const int AudioCategoryMin = 3000;
        private const int AudioCategoryMax = 3040;
        private const int AudiobooksCategoryId = 3030;

        // Torrent indexers routinely expose categories as free text instead of a Newznab numeric id
        // (e.g. "Music", "Audio - Music").
        private static readonly Regex MusicCategoryWord = new(@"\bmusic\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] AudiobookCategoryTokens = { "audiobook", "spoken word", "spokenword" };

        // Scene-style music release naming: "Artist - Album (Year) [FLAC]", "(2019) [MP3 320]", etc.
        // Deliberately narrow -- requires a 4-digit year in parentheses immediately followed by a
        // bracketed audio-release tag, a convention audiobook releases essentially never use.
        private static readonly Regex SceneAlbumPattern = new(
            @"\(\d{4}\)\s*\[(FLAC|MP3|WEB|CD|V0|V1|V2|320)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] AlbumShapeKeywords = { "discography" };

        // Widened ordinary-album vocabulary (tracker #361, part (b)): common release-shape words
        // that are, alone, too ordinary to decide anything (an audiobook can legitimately be a
        // "Soundtrack" memoir or an "Anthology" collection) but corroborate real signals elsewhere.
        private static readonly Regex AlbumVocabPattern = new(
            @"\b(EP|LP|Remastered|Anthology|Soundtrack|Single)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // A bitrate/audio-format tag, without SceneAlbumPattern's strict year-prefix requirement.
        // Independent review (tracker #361) found this reintroducing the same class of bug three
        // times running, each time on a different axis:
        //   - round 2: MP3/WEB/CD/128/192/256 are poor discriminators (MP3 is the single most
        //     common real-world audiobook distribution format; "CD" means nothing more specific
        //     than "compact disc").
        //   - round 3: the fix for that -- keeping bare V0/V1/V2/320 -- had the SAME problem from
        //     a different direction: "V1"/"V2" are the standard book-series volume abbreviation
        //     ("Chronicles of the North (V1)" = Volume 1), and a bare "320" is an ordinary page
        //     count ("A Long Walk (320 Pages)").
        //   - round 4: narrowing the tokens to just FLAC/kbps (unambiguous words in isolation)
        //     still matched a narrow but real nonfiction case -- an audiobook ABOUT audio
        //     technology, where "FLAC" or a kbps figure is the book's subject matter, embedded in
        //     ordinary prose ("The Vinyl Revival (FLAC Format Explained)", "128kbps and Beyond"),
        //     not a release tag.
        // Round 4's fix is structural rather than lexical: a real release tag is conventionally
        // the LAST thing in a title, and a bracketed one contains nothing but the tag itself.
        // Prose mentioning the same word mid-title, or inside a bracket with other words, no
        // longer matches -- regardless of what the token list contains. This is deliberately a
        // different kind of fix than rounds 2-3 (which only ever narrowed the token list): three
        // rounds of token-list narrowing kept finding a new collision, so this closes the whole
        // class by requiring release-tag SHAPE, not just spelling.
        private static readonly Regex BitrateTagPattern = new(
            @"[\[\(]\s*FLAC\s*[\]\)]\s*$|\b\d{2,3}\s*kbps\b\s*[\]\)]?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // A parenthetical that is ENTIRELY one or two slash-joined music genre names, e.g. "(Pop)",
        // "(Pop/Rock)". Deliberately narrow to standalone genre words so it never fires on an
        // ordinary descriptive parenthetical that happens to contain one as a substring, e.g.
        // "(Rock Climbing Memoir)".
        private static readonly Regex GenreMarkerPattern = new(
            @"\((?:Pop|Rock|Jazz|Metal|Reggae|Blues|Techno|Punk|Disco|Grunge|Hip-Hop|R&B)" +
            @"(?:\s*/\s*(?:Pop|Rock|Jazz|Metal|Reggae|Blues|Techno|Punk|Disco|Grunge|Hip-Hop|R&B))*\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] AudiobookSignalWords =
        {
            "unabridged", "abridged", "narrated by", "narrator", "audiobook", "chapters", "read by"
        };

        // Weighted-signal constants. Weights at or above RejectThreshold on their own preserve
        // every decisive signal #336 originally shipped with, unchanged. Everything introduced or
        // weakened by this strengthening is deliberately kept well under RejectThreshold alone.
        private const int RejectThreshold = 50;

        private const int CategoryMusicIdDecisiveWeight = 1000;
        private const int CategoryTextMusicWeight = 1000;
        private const int CategoryAudiobooksIdWeight = -25;    // (c): weighted, not absolute
        private const int CategoryMusicIdWeakWeight = 15;      // music id co-occurring with 3030

        private const int TitleStrictScenePatternWeight = 1000;
        private const int TitleDiscographyKeywordWeight = 1000;
        // Everything below is deliberately kept small enough that NO COMBINATION of two, three,
        // or even all four of the ordinary/ambiguous signals (GenreMarker, AlbumVocab, BareDash,
        // ArtistAlbumCorroboration) can cross RejectThreshold by itself -- verified by brute-force
        // enumeration of every pair/triple/quad, not by hand, after two rounds of independent
        // review (tracker #361) each found a combination that crossed under an earlier tuning:
        // round 1 found AlbumVocab+BareDash reaching threshold on an ordinary "Author - Title"
        // audiobook title containing one common English word; round 2 found GenreMarker+AlbumVocab
        // crossing the same way (an arithmetic oversight against this very invariant), plus a
        // Size nudge tipping a GenreMarker+BareDash pair that was sitting too close to the
        // threshold to begin with. Only a genuinely scene-specific bitrate/format tag
        // (TitleBitrateTagWeight -- see its narrowed token list above) or the combination of ALL
        // FOUR ambiguous signals plus a Size nudge can now cross. The margin from any single pair
        // or triple of the four ambiguous signals to RejectThreshold is always at least 12, well
        // above SizeWeakWeight, so Size can only ever tip the one maximally-corroborated case.
        private const int TitleGenreMarkerWeight = 18;         // near-twin "(Pop)" case, (b)
        private const int TitleAlbumVocabWeight = 12;          // (b): weak, ambiguous alone
        private const int TitleBitrateTagWeight = 48;          // (b): stronger -- see note above
        private const int TitleBareArtistAlbumDashWeight = 8;  // (b): weak, ambiguous alone

        private const int ArtistAlbumCorroborationWeight = 8;  // (a): weak corroboration only
        private const int SizeWeakWeight = 8;                  // (d): weak nudge only
        private const long SmallSizeThresholdBytes = 300L * 1024 * 1024;
        private const long LargeSizeThresholdBytes = 600L * 1024 * 1024;

        public static bool LooksLikeMusicRelease(SearchResult result, out string? reason)
        {
            reason = null;
            if (result == null)
            {
                return false;
            }

            var signals = new List<(int Weight, string Reason)>();

            CollectCategorySignals(result.Category, signals);
            CollectTitleSignals(result.Title, signals);
            CollectArtistAlbumSignal(result, signals);
            CollectSizeSignal(result.Size, signals);

            var total = signals.Sum(s => s.Weight);
            if (total < RejectThreshold)
            {
                return false;
            }

            reason = string.Join("; ", signals.Where(s => s.Weight > 0).Select(s => s.Reason).Distinct());
            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = "Weighted signals indicate a music release";
            }
            return true;
        }

        private static void CollectCategorySignals(string? category, List<(int, string)> signals)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return;
            }

            var categoryLower = category.ToLowerInvariant();
            if (AudiobookCategoryTokens.Any(categoryLower.Contains))
            {
                // Explicit audiobook category text always protects the category domain outright;
                // it never contributes a music signal regardless of any numeric id riding along.
                return;
            }

            // Numeric Newznab/Torznab category ids can appear standalone ("3010") or embedded in a
            // separated list ("3000,3010"); treat any embedded run of digits as a candidate id.
            var ids = Regex.Matches(category, @"\d+")
                .Select(m => int.TryParse(m.Value, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            if (ids.Count > 0)
            {
                var hasAudiobooksId = ids.Contains(AudiobooksCategoryId);
                var musicIds = ids.Where(id => id >= AudioCategoryMin && id <= AudioCategoryMax && id != AudiobooksCategoryId).ToList();

                if (hasAudiobooksId)
                {
                    // (c): the Audiobooks id is now ONE weighted signal toward "not music", strong
                    // but no longer an absolute, unconditional exoneration -- a strong enough
                    // contrary signal elsewhere (title shape, Artist/Album corroboration) can still
                    // outweigh it.
                    signals.Add((CategoryAudiobooksIdWeight, $"Category also carries the Audiobooks id ({AudiobooksCategoryId})"));
                    if (musicIds.Count > 0)
                    {
                        signals.Add((CategoryMusicIdWeakWeight, $"Category also carries music-range id {musicIds[0]} alongside Audiobooks"));
                    }
                    return;
                }

                if (musicIds.Count > 0)
                {
                    signals.Add((CategoryMusicIdDecisiveWeight, $"Category id {musicIds[0]} is a Newznab/Torznab music category ({AudioCategoryMin}-{AudioCategoryMax} excluding {AudiobooksCategoryId}/Audiobooks)"));
                    return;
                }

                // Numeric ids present but none in the music range and no Audiobooks id either:
                // fall through to the textual check below in case the string also carries a name.
            }

            if (MusicCategoryWord.IsMatch(category))
            {
                signals.Add((CategoryTextMusicWeight, $"Category '{category}' is a torrent-indexer music category"));
            }
        }

        private static void CollectTitleSignals(string? title, List<(int, string)> signals)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            var titleLower = title.ToLowerInvariant();
            if (AudiobookSignalWords.Any(titleLower.Contains))
            {
                // Explicit audiobook signal words always protect the title domain outright.
                return;
            }

            if (SceneAlbumPattern.IsMatch(title))
            {
                signals.Add((TitleStrictScenePatternWeight, "Title matches a scene-style music album release pattern (Artist - Album (Year) [FLAC/MP3/...])"));
            }

            if (AlbumShapeKeywords.Any(titleLower.Contains))
            {
                signals.Add((TitleDiscographyKeywordWeight, "Title contains a music-discography marker"));
            }

            if (GenreMarkerPattern.IsMatch(title))
            {
                signals.Add((TitleGenreMarkerWeight, "Title carries a parenthetical music-genre marker"));
            }

            if (AlbumVocabPattern.IsMatch(title))
            {
                signals.Add((TitleAlbumVocabWeight, "Title contains ordinary album vocabulary (EP/LP/Remastered/Anthology/Soundtrack/Single)"));
            }

            if (BitrateTagPattern.IsMatch(title))
            {
                signals.Add((TitleBitrateTagWeight, "Title carries a bitrate/audio-format tag"));
            }

            if (TrySplitArtistAlbumDash(title, out _, out _))
            {
                signals.Add((TitleBareArtistAlbumDashWeight, "Title has a bare \"Artist - Album\" shape"));
            }
        }

        private static void CollectArtistAlbumSignal(SearchResult result, List<(int, string)> signals)
        {
            var artist = (result.Artist ?? string.Empty).Trim();
            var album = (result.Album ?? string.Empty).Trim();
            if (artist.Length == 0 || album.Length == 0)
            {
                return;
            }

            var artistLower = artist.ToLowerInvariant();
            var albumLower = album.ToLowerInvariant();
            if (AudiobookSignalWords.Any(artistLower.Contains) || AudiobookSignalWords.Any(albumLower.Contains))
            {
                return;
            }

            if (!TrySplitArtistAlbumDash(result.Title, out var left, out var right))
            {
                return;
            }

            if (string.Equals(left, artist, StringComparison.OrdinalIgnoreCase) &&
                right.StartsWith(album, StringComparison.OrdinalIgnoreCase))
            {
                // (a): the structured Artist/Album fields corroborate the title's own
                // "Artist - Album" shape -- consistency between the two is the signal, not merely
                // both fields being non-empty. An audiobook with a narrator/series name sitting in
                // these fields, but no matching title shape, must never trip this.
                signals.Add((ArtistAlbumCorroborationWeight, "Artist/Album fields match the title's \"Artist - Album\" shape"));
            }
        }

        private static void CollectSizeSignal(long size, List<(int, string)> signals)
        {
            // (d): Size is a weak corroborating nudge only, never decisive by itself -- its weight
            // is well under RejectThreshold on its own in every case.
            if (size > 0 && size < SmallSizeThresholdBytes)
            {
                signals.Add((SizeWeakWeight, "Size is small, more typical of a music release than an audiobook"));
            }
            else if (size > LargeSizeThresholdBytes)
            {
                signals.Add((-SizeWeakWeight, "Size is large, more typical of an audiobook than a music release"));
            }
        }

        private static bool TrySplitArtistAlbumDash(string? title, out string left, out string right)
        {
            left = string.Empty;
            right = string.Empty;
            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            var dashIndex = title.IndexOf(" - ", StringComparison.Ordinal);
            if (dashIndex <= 0)
            {
                return false;
            }

            left = title[..dashIndex].Trim();
            right = title[(dashIndex + 3)..].Trim();
            return left.Length >= 2 && right.Length >= 2;
        }
    }
}
