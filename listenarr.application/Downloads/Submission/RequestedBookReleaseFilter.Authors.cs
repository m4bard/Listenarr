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

using Listenarr.Domain.Common;

namespace Listenarr.Application.Downloads.Submission
{
    /// <summary>
    /// The author half of <see cref="RequestedBookReleaseFilter"/>: whose name a release carries,
    /// and whether it is the book's author.
    /// </summary>
    public static partial class RequestedBookReleaseFilter
    {
        /// <summary>
        /// What parsers and metadata write when they have no author. A placeholder says nothing
        /// about who wrote the release, so it can neither corroborate nor contradict the book.
        /// </summary>
        private static readonly IReadOnlySet<string> PlaceholderAuthors =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "unknown", "unknown author", "various", "various authors", "various artists",
                "anonymous", "anon", "na", "n a"
            };

        /// <summary>
        /// Generational and honorific suffixes, which follow the surname and are not it.
        /// </summary>
        private static readonly IReadOnlySet<string> NameSuffixes =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "jr", "sr", "ii", "iii", "iv", "phd", "md"
            };

        /// <summary>
        /// Whether the release's author field holds anything besides the book's own title words,
        /// series words and numbers. The Torznab parser fills that field with whatever precedes the
        /// first " - " of the title, so "Barsoom 01 - A Princess of Mars" arrives with "Barsoom 01"
        /// as its author, and that must not read as a different author. "Mark Twain" does.
        /// </summary>
        private static bool NamesAnotherAuthor(List<string> releaseAuthorTokens, Audiobook audiobook)
        {
            var ownWords = new HashSet<string>(Words(audiobook.Title), StringComparer.Ordinal);
            ownWords.UnionWith(Words(audiobook.Series));

            return releaseAuthorTokens
                .Where(AudiobookSearchQueryBuilder.IsSignificantWord)
                .Where(word => !NoiseWords.Contains(word) && !IsNumber(word))
                .Any(word => !ownWords.Contains(word));
        }

        /// <summary>
        /// The surname of each usable author, plus the surname joined to the word before it so a
        /// particle name matches however it is spaced ("Le Guin" and "LeGuin").
        /// </summary>
        /// <remarks>
        /// Only the text before a comma is read, which is the surname in "Doyle, Arthur Conan" and
        /// the whole name in "Martin Luther King, Jr.". Initials are merged by
        /// <see cref="StringUtils.NormalizeAuthorName"/> first, so "H. G. Wells" and "HG Wells"
        /// both end in "wells".
        /// </remarks>
        private static HashSet<string> BuildSurnameKeys(IEnumerable<string>? authors)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var author in authors ?? [])
            {
                if (IsPlaceholderAuthor(author))
                {
                    continue;
                }

                var beforeComma = author.Split(',', 2)[0];
                var words = StringUtils.NormalizeAuthorName(beforeComma)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(word => !NameSuffixes.Contains(word))
                    .ToList();

                if (words.Count == 0 || words[^1].Length < 2)
                {
                    continue;
                }

                keys.Add(words[^1]);
                if (words.Count >= 2)
                {
                    keys.Add(words[^2] + words[^1]);
                }
            }

            return keys;
        }

        private static IEnumerable<string> WordsAndJoinedPairs(IReadOnlyList<string> tokens)
        {
            for (var index = 0; index < tokens.Count; index++)
            {
                yield return tokens[index];
                if (index + 1 < tokens.Count)
                {
                    yield return tokens[index] + tokens[index + 1];
                }
            }
        }

        private static bool IsPlaceholderAuthor(string? author)
        {
            var normalized = StringUtils.NormalizeAuthorName(author);
            return normalized.Length == 0 || PlaceholderAuthors.Contains(normalized);
        }
    }
}
