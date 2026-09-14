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
using System.Text.RegularExpressions;

namespace Listenarr.Application.Search.Parsing
{
    /// <summary>
    /// Tells a bundle or omnibus apart from a single-book edition, from the two signals that
    /// are actually available at the point a release is scored.
    ///
    /// <para><see cref="IsBundleSeriesNumber"/> reads a stored series position and is the
    /// precise one: Audible gives an omnibus a position such as "1-4", and the value survives
    /// as text all the way to the database.</para>
    ///
    /// <para><see cref="LooksLikeBundle"/> reads a release title and is a heuristic. Indexer
    /// results carry no series information at all, so a title is the only thing there is to go
    /// on, and it will be wrong sometimes. Callers should treat a positive as a preference
    /// signal and not as grounds to reject a release outright.</para>
    /// </summary>
    public static partial class ReleaseShapeDetector
    {
        /// <summary>
        /// Title phrases that mark a multi-book product. Shared with
        /// <c>AudiobookOnlyFilter</c>, which has used this vocabulary since before there was
        /// anywhere else to put it; two copies of a list like this drift apart.
        /// </summary>
        public static readonly string[] BundlePhrases =
        {
            "Box Set", "3 Books", "3 Book", "3-Book", "Three Volume", "Three Volume Set",
            "Volume Set", "Trilogy", "Collector's Edition", "Slipcase", "Box Set:", "Box set:"
        };

        /// <summary>
        /// Phrases that mean "several books in one release" but that are not evidence a
        /// product page is print rather than audio, so they belong here and not in the list
        /// <c>AudiobookOnlyFilter</c> drops results on.
        /// </summary>
        private static readonly string[] AdditionalBundlePhrases =
        {
            "Omnibus", "Boxset", "Complete Series"
        };

        /// <summary>
        /// "Books 1-4", "Vol. 1-3", "Volumes 1 - 12", "Parts 2-5". The keyword carries the
        /// meaning, so the numbers are allowed any width.
        /// </summary>
        [GeneratedRegex(@"\b(?:books?|vols?|volumes?|parts?)\.?\s*\d+\s*[-\u2010-\u2015]\s*\d+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex KeyedRangeRegex();

        /// <summary>
        /// A bare range with no keyword, as in "Dune 1-6 [M4B]". Deliberately narrow: both
        /// sides are held to two digits so a year span ("1887-1927") and a bitrate range
        /// ("64-128kbps") do not qualify, and neither side may touch a word character so
        /// "MP3-128" and "Catch-22" cannot produce one either.
        /// </summary>
        [GeneratedRegex(@"(?<![\w.])(\d{1,2})\s*[-\u2010-\u2015]\s*(\d{1,2})(?![\w.])",
            RegexOptions.CultureInvariant)]
        private static partial Regex BareRangeRegex();

        /// <summary>
        /// "8-Book Box Set", "12 books". The count must be two or more: "1 Book" is a single
        /// edition describing itself.
        /// </summary>
        [GeneratedRegex(@"\b(?:[2-9]|\d{2,})\s*-?\s*books?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex BookCountRegex();

        /// <summary>
        /// A range or list of positions inside a series-position string: "1-4", "1 - 4",
        /// "1-3, 5". Anything that reads as one position is not a bundle, including the
        /// fractional positions ("1.5") a series uses for a novella.
        /// </summary>
        [GeneratedRegex(@"\d+\s*[-\u2010-\u2015]\s*\d+", RegexOptions.CultureInvariant)]
        private static partial Regex PositionRangeRegex();

        /// <summary>
        /// Whether a stored series position covers more than one book.
        /// </summary>
        /// <param name="seriesNumber">
        /// The position as the metadata source stated it. This is free text: Audible returns
        /// it through GetString and it is persisted as TEXT, so it is not always a number.
        /// </param>
        public static bool IsBundleSeriesNumber(string? seriesNumber)
        {
            if (string.IsNullOrWhiteSpace(seriesNumber))
            {
                return false;
            }

            var trimmed = seriesNumber.Trim();

            // One number, however written, is one book. NumberStyles.Number is deliberate:
            // it accepts a grouped "20,000" as a single value rather than letting the list
            // check below read the comma as a separator between two positions.
            if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }

            if (PositionRangeRegex().IsMatch(trimmed))
            {
                return true;
            }

            // A list of positions: "1, 2, 5". Two or more of the parts have to be numbers,
            // which is what keeps "2, Dramatized", a real Audible position, out.
            var numericParts = trimmed
                .Split(new[] { ',', ';', '&', '+' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Count(part => decimal.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out _));

            return numericParts >= 2;
        }

        /// <summary>
        /// Whether a release title reads as a bundle or omnibus rather than a single book.
        /// Heuristic by nature; see the class remarks.
        /// </summary>
        public static bool LooksLikeBundle(string? releaseTitle)
        {
            if (string.IsNullOrWhiteSpace(releaseTitle))
            {
                return false;
            }

            var title = releaseTitle;

            if (BundlePhrases.Concat(AdditionalBundlePhrases)
                .Any(phrase => title.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return KeyedRangeRegex().IsMatch(title)
                || BookCountRegex().IsMatch(title)
                || BareRangeRegex().IsMatch(title);
        }
    }
}
