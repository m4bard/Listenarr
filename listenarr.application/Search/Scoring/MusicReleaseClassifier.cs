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
    /// (see SearchResultScorer). Fails open by design: every path that cannot establish a positive
    /// music signal returns false, including missing category, ambiguous category, and plain titles.
    /// </summary>
    public static class MusicReleaseClassifier
    {
        // Newznab/Torznab "Audio" parent category (3000) and its subcategories, per the standard
        // Newznab taxonomy. 3030 is Audiobooks and is deliberately excluded from the music range.
        private const int AudioCategoryMin = 3000;
        private const int AudioCategoryMax = 3040;
        private const int AudiobooksCategoryId = 3030;

        // Torrent indexers routinely expose categories as free text instead of a Newznab numeric id
        // (e.g. "Music", "Audio - Music"). Matched as a whole word so this never fires on a category
        // string that also names an audiobook/spoken-word category -- that always wins.
        private static readonly Regex MusicCategoryWord = new(@"\bmusic\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] AudiobookCategoryTokens = { "audiobook", "spoken word", "spokenword" };

        // Scene-style music release naming: "Artist - Album (Year) [FLAC]", "(2019) [MP3 320]", etc.
        // Deliberately narrow -- requires a 4-digit year in parentheses immediately followed by a
        // bracketed audio-release tag, a convention audiobook releases essentially never use.
        private static readonly Regex SceneAlbumPattern = new(
            @"\(\d{4}\)\s*\[(FLAC|MP3|WEB|CD|V0|V1|V2|320)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] AlbumShapeKeywords = { "discography" };

        private static readonly string[] AudiobookSignalWords =
        {
            "unabridged", "abridged", "narrated by", "narrator", "audiobook", "chapters", "read by"
        };

        public static bool LooksLikeMusicRelease(SearchResult result, out string? reason)
        {
            reason = null;
            if (result == null)
            {
                return false;
            }

            if (CategoryIndicatesMusic(result.Category, out var categoryReason))
            {
                reason = categoryReason;
                return true;
            }

            if (TitleLooksLikeMusicAlbum(result.Title, out var titleReason))
            {
                reason = titleReason;
                return true;
            }

            return false;
        }

        private static bool CategoryIndicatesMusic(string? category, out string? reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(category))
            {
                return false;
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
                // An explicit Audiobooks id anywhere in a multi-category string (e.g. "3030,3000"
                // from an indexer that also tags the generic Audio parent) always overrides: this is
                // an unambiguous audiobook signal and must never be rejected just because a broader
                // Audio-range id rides along with it.
                if (ids.Contains(AudiobooksCategoryId))
                {
                    return false;
                }

                var musicIds = ids.Where(id => id >= AudioCategoryMin && id <= AudioCategoryMax).ToList();
                if (musicIds.Count > 0)
                {
                    reason = $"Category id {musicIds[0]} is a Newznab/Torznab music category ({AudioCategoryMin}-{AudioCategoryMax} excluding {AudiobooksCategoryId}/Audiobooks)";
                    return true;
                }

                // Had numeric ids but none in the music range and no Audiobooks id either:
                // fall through to the textual check below in case the string also carries a name.
            }

            var categoryLower = category.ToLowerInvariant();
            if (AudiobookCategoryTokens.Any(categoryLower.Contains))
            {
                return false;
            }

            if (MusicCategoryWord.IsMatch(category))
            {
                reason = $"Category '{category}' is a torrent-indexer music category";
                return true;
            }

            return false;
        }

        private static bool TitleLooksLikeMusicAlbum(string? title, out string? reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            var titleLower = title.ToLowerInvariant();
            if (AudiobookSignalWords.Any(titleLower.Contains))
            {
                return false;
            }

            if (SceneAlbumPattern.IsMatch(title))
            {
                reason = "Title matches a scene-style music album release pattern (Artist - Album (Year) [FLAC/MP3/...])";
                return true;
            }

            if (AlbumShapeKeywords.Any(titleLower.Contains))
            {
                reason = "Title contains a music-discography marker";
                return true;
            }

            return false;
        }
    }
}
