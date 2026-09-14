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
using System.Globalization;
using System.Text;

namespace Listenarr.Domain.Common
{
    public class StringUtils
    {
        /// <summary>
        /// Normalizes an author name into a lowercase, diacritic-free, punctuation-free
        /// comparison key. Strips periods rather than replacing them with whitespace so
        /// "J.N. Chaney" and "JN Chaney" collapse to the same key, then merges any run of
        /// single-character tokens left over from spaced-out initials (e.g. "J. N. Chaney",
        /// "P P Corcoran") so those match the tight form too. Full words are never merged,
        /// so distinct people who happen to share initials (e.g. "J. N. Chaney" vs
        /// "J. N. Smith") still normalize differently.
        /// </summary>
        public static string NormalizeAuthorName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (var character in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
                else if (char.IsWhiteSpace(character))
                {
                    builder.Append(' ');
                }
            }

            var tokens = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var merged = new List<string>(tokens.Length);
            var previousTokenWasSingleCharacter = false;

            foreach (var token in tokens)
            {
                var isSingleCharacter = token.Length == 1;
                if (isSingleCharacter && previousTokenWasSingleCharacter && merged.Count > 0)
                {
                    merged[^1] += token;
                }
                else
                {
                    merged.Add(token);
                }

                previousTokenWasSingleCharacter = isSingleCharacter;
            }

            return string.Join(' ', merged);
        }

        /// <summary>
        /// True when two author strings differ only in ways <see cref="NormalizeAuthorName"/>
        /// already erases: case, punctuation, whitespace and diacritics. This is the guard that
        /// makes adopting a provider's or a cache row's spelling a cosmetic correction rather
        /// than a merge -- it can change how an author is spelled and it cannot change who the
        /// author is. Containment is deliberately not enough: the remote author lookup accepts a
        /// bare substring in either direction, so "Conan Doyle" matches "Sir Arthur Conan Doyle",
        /// and a pen name, a translator credit or a deliberately distinct credit would be
        /// destroyed with no way back.
        /// </summary>
        public static bool IsAuthorSpellingVariant(string? candidate, string? other)
        {
            var candidateKey = NormalizeAuthorName(candidate);
            if (string.IsNullOrEmpty(candidateKey))
            {
                return false;
            }

            return string.Equals(candidateKey, NormalizeAuthorName(other), StringComparison.Ordinal);
        }

        /// <summary>
        /// True when a cached author row is the row for <paramref name="normalizedName"/>. The
        /// stored key is checked first and the display name is re-derived as a second chance,
        /// because a row written before the author normalizer was unified carries a key the
        /// current reader never produces -- and a row must not be declared to belong to somebody
        /// else on the strength of a key nobody can reproduce.
        /// </summary>
        public static bool MatchesAuthorKey(string? storedNormalizedName, string? displayName, string? normalizedName)
        {
            if (string.IsNullOrEmpty(normalizedName))
            {
                return false;
            }

            return string.Equals(storedNormalizedName, normalizedName, StringComparison.Ordinal)
                || string.Equals(NormalizeAuthorName(displayName), normalizedName, StringComparison.Ordinal);
        }

        public static int LevenshteinDistance(string s, string t)
        {
            if (s == t) return 0;
            if (string.IsNullOrEmpty(s)) return t.Length;
            if (string.IsNullOrEmpty(t)) return s.Length;

            var n = s.Length;
            var m = t.Length;
            var d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; d[i, 0] = i++) { }
            for (int j = 0; j <= m; d[0, j] = j++) { }

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }
    }
}
