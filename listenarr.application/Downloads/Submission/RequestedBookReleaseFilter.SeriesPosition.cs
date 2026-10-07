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
using System.Text.RegularExpressions;

namespace Listenarr.Application.Downloads.Submission
{
    /// <summary>
    /// The series-position half of <see cref="RequestedBookReleaseFilter"/>: a structured-field
    /// check (<see cref="Audiobook.SeriesNumber"/>), not a title-text one, so a catalog record
    /// whose title carries no "Book N"/"Vol N" of its own still gets protection against a
    /// same-author, same-series release honestly titled for a different entry.
    /// </summary>
    public static partial class RequestedBookReleaseFilter
    {
        /// <summary>
        /// <see cref="WordsKeepingFractions"/>' own tokenizer: ASCII digits either side of a
        /// literal decimal point first, so "2.5" stays one token, or an ordinary run of letters
        /// and digits (<c>\p{L}</c>/<c>\p{N}</c>, the same breadth <see
        /// cref="AudiobookSearchQueryBuilder.Tokenize"/> gets from <c>char.IsLetterOrDigit</c>)
        /// otherwise. Anything else (spaces, dashes, brackets, a lone ".") matches neither and is
        /// simply skipped, the same as <c>Tokenize</c> dropping it by turning it into a space.
        /// </summary>
        private static readonly Regex FractionAwareToken = new(
            @"[0-9]+\.[0-9]+|[\p{L}\p{N}]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// A whole number or a "2.5"-shaped fractional one, and nothing else. ASCII digits only,
        /// matching <see cref="IsNumber"/>'s own restriction elsewhere in this file.
        /// </summary>
        private static readonly Regex PositionLikeToken = new(
            @"^[0-9]+(?:\.[0-9]+)?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Numbers that only ever look like "1", "2" or "2.5" in practice, never grouped, so
        /// thousands separators are deliberately not allowed: a malformed field such as "1,2"
        /// fails closed (unparseable, so this check falls open) rather than silently reading as
        /// 12.
        /// </summary>
        private const NumberStyles PositionNumberStyle = NumberStyles.AllowDecimalPoint;

        /// <summary>
        /// Whether the catalog record's own series position and a position parsed from the
        /// release's title are both known, both whole numbers, and name different entries.
        /// Reads <see cref="Audiobook.SeriesNumber"/>, the structured field, instead of the
        /// catalog <c>Title</c> string, so a bare title such as "Barsoom" (no "Book N" or
        /// "Vol N" of its own) still gets this protection. Fails open whenever either side has
        /// no usable position (a record with no series number set, or a release whose title
        /// carries nothing that parses as one) or either side is fractional: a fractional
        /// position is a catalog-vs-publisher numbering disagreement as often as a real
        /// mismatch -- a novella the catalog source counts as entry 0.5 is routinely entry 1 in
        /// a publisher's own release numbering -- so comparing across that boundary is not
        /// reliable enough to reject on. Only a whole-number-vs-whole-number disagreement is.
        /// </summary>
        private static bool NamesADifferentSeriesEntry(
            Audiobook audiobook, string? releaseTitle, List<TitleForm> matchedForms)
        {
            var catalogPosition = ParsePosition(audiobook.SeriesNumber);
            if (catalogPosition is null)
            {
                return false;
            }

            // A tokenizer of its own, not the shared Words()/Tokenize(): those turn "2.5" into
            // separate "2" and "5" tokens the way any other punctuation is dropped, which would
            // read a release honestly for interstitial entry 2.5 as entry 2 and silently miss a
            // real mismatch (or, just as wrong, reject a genuine 2.5-for-2.5 match).
            var releaseTokens = WordsKeepingFractions(releaseTitle);

            foreach (var form in matchedForms)
            {
                var releasePosition = ParseReleasePosition(releaseTokens, form.Words);
                if (releasePosition is decimal position)
                {
                    if (HasFractionalPart(catalogPosition.Value) || HasFractionalPart(position))
                    {
                        return false;
                    }

                    return position != catalogPosition;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a position carries a fractional part (a novella/interstitial slot such as
        /// "0.5" or "2.5") rather than a whole-number entry. Whole-number-vs-whole-number is the
        /// only comparison this filter trusts enough to reject on; see
        /// <see cref="NamesADifferentSeriesEntry"/>.
        /// </summary>
        private static bool HasFractionalPart(decimal value) => value != Math.Truncate(value);

        /// <summary>
        /// The number that follows one of the book's own title words, or a volume marker, in the
        /// release's title tokens -- the same shape <see cref="NamesADifferentVolume"/> treats as
        /// a position, but read from whatever the release names rather than compared against a
        /// number the catalog title itself carries.
        /// </summary>
        private static decimal? ParseReleasePosition(List<string> releaseTokens, IReadOnlySet<string> titleWords)
        {
            for (var index = 0; index + 1 < releaseTokens.Count; index++)
            {
                var next = releaseTokens[index + 1];
                var numbersAPosition = VolumeMarkers.Contains(releaseTokens[index])
                    || (titleWords.Contains(releaseTokens[index]) && !IsYear(next));

                if (numbersAPosition
                    && PositionLikeToken.IsMatch(next)
                    && decimal.TryParse(next, PositionNumberStyle, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        /// <summary>A series-number field read as a number, or null when it is absent or not one.</summary>
        private static decimal? ParsePosition(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && decimal.TryParse(value, PositionNumberStyle, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        /// <summary>
        /// Tokens in the same case-folded, accent-stripped shape <see
        /// cref="AudiobookSearchQueryBuilder.Tokenize"/> produces, except that a run of digits
        /// either side of a literal decimal point stays one token ("2.5") instead of becoming
        /// two whole-number ones ("2", "5") the way any other punctuation does. Only the
        /// series-position check needs this distinction, so it is kept local rather than changed
        /// on the shared tokenizer every other title and query comparison also relies on.
        /// </summary>
        private static List<string> WordsKeepingFractions(string? text)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return tokens;
            }

            var builder = new StringBuilder(text.Length);
            foreach (var ch in text.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark
                    || ch is '\'' or '’' or 'ʼ' or '`' or '´')
                {
                    continue;
                }

                builder.Append(ch);
            }

            var normalized = builder.ToString();
            foreach (Match match in FractionAwareToken.Matches(normalized))
            {
                tokens.Add(match.Value.ToLowerInvariant());
            }

            return tokens;
        }
    }
}
