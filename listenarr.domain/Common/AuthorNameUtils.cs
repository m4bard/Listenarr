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

namespace Listenarr.Domain.Common
{
    /// <summary>
    /// Settles how initials are spaced and punctuated when a person's name is written into a
    /// folder or file name.
    ///
    /// The author string used for naming is whatever the metadata source returned for that one
    /// release, and sources disagree about the spacing around initials. "J.M. Barrie",
    /// "J. M. Barrie" and "J M Barrie" are the same name typed three ways, and each one becomes
    /// its own top-level folder. All three are written here as "J. M. Barrie".
    ///
    /// The limit of this is worth stating, because it is easy to assume otherwise. This is a
    /// typography rule. It cannot tell that "James M. Barrie" and "J. M. Barrie" name one
    /// person, because deciding that means either abbreviating a given name that was spelled
    /// out or expanding an initial into a name that is not present in the string. Both need an
    /// author record to resolve against, and neither belongs in a rendering helper.
    /// </summary>
    public static class AuthorNameUtils
    {
        // A period straight after a lone letter closes an initial, so whatever follows it starts
        // a new part of the name. The lookbehind is what keeps "Dr." and "St." whole: there the
        // letter before the period has another letter in front of it, so it is not standing alone.
        private static readonly Regex InitialClosedByPeriod =
            new(@"(?<![\p{L}\p{N}])(\p{L})\.", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex WhitespaceRun =
            new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Rewrite the spacing and periods around any initials in <paramref name="name"/>.
        /// A blank input gives back an empty string, so callers keep their own fallback.
        /// </summary>
        public static string CanonicalizeForPath(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var spaced = InitialClosedByPeriod.Replace(name, "$1. ");
            var tokens = WhitespaceRun.Split(spaced.Trim());

            for (var i = 0; i < tokens.Length - 1; i++)
            {
                // A lone letter ahead of the surname is an initial that lost its period.
                if (tokens[i].Length == 1 && UsesLatinInitials(tokens[i][0]))
                {
                    tokens[i] += ".";
                }
            }

            return string.Join(' ', tokens);
        }

        // Writing an initial as a bare letter is a Latin-script habit. A single character in a
        // script that does not do that, a CJK given name for one, is a whole part of the name,
        // and putting a period after it would invent a spelling nobody uses.
        private static bool UsesLatinInitials(char character)
        {
            if (character is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            {
                return true;
            }

            return character is >= 'À' and <= 'ɏ' && char.IsLetter(character);
        }
    }
}
