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
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Listenarr.Application.Search.Scoring
{
    /// <summary>
    /// Matches a quality profile's filter terms (MustNotContain, MustContain, PreferredWords)
    /// against a release title on word boundaries rather than as raw substrings, so a forbidden
    /// "abridged" no longer rejects a release labelled "Unabridged". Compiled patterns are
    /// cached because scoring runs once per search result and profiles change rarely.
    /// </summary>
    public static class TitleTermMatcher
    {
        private static readonly ConcurrentDictionary<string, Regex> TermPatterns = new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan TermMatchTimeout = TimeSpan.FromMilliseconds(250);
        private const int TermPatternCacheLimit = 1024;

        /// <summary>
        /// Whether <paramref name="title"/> contains <paramref name="term"/> as a whole word.
        /// An empty or whitespace-only term matches nothing, so a blank entry in a profile's
        /// word list places no requirement and rejects nothing.
        /// </summary>
        public static bool TitleContainsTerm(string? title, string? term)
        {
            if (string.IsNullOrEmpty(title) || string.IsNullOrWhiteSpace(term)) return false;
            return GetTermPattern(term.Trim()).IsMatch(title);
        }

        private static Regex GetTermPattern(string term)
        {
            if (TermPatterns.TryGetValue(term, out var cached)) return cached;

            // \b only asserts a boundary beside a word character, so anchor an end of the term
            // only when that end is itself a word character: "v0" anchors both ends, "(sample)"
            // neither, and "[unabridged" just the trailing one.
            var leading = IsWordCharacter(term[0]) ? "\\b" : string.Empty;
            var trailing = IsWordCharacter(term[^1]) ? "\\b" : string.Empty;
            var pattern = new Regex(
                leading + Regex.Escape(term) + trailing,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TermMatchTimeout);

            if (TermPatterns.Count < TermPatternCacheLimit) TermPatterns.TryAdd(term, pattern);
            return pattern;
        }

        private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}
