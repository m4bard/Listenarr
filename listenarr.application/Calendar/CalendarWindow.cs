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

namespace Listenarr.Application.Calendar
{
    /// <summary>
    /// An inclusive day range, plus the coarse string keys used to push the range down to the
    /// PublishedDate TEXT column.
    /// </summary>
    /// <remarks>
    /// Audiobook.PublishedDate is a string, and the writers in this codebase do not agree on a
    /// single format: the metadata path normalises to yyyy-MM-dd, the indexer parsers write a
    /// round-trip "o" value with a time component, and a fallback writes {year}-01-01. A bare
    /// four-digit year also occurs.
    ///
    /// Under binary collation every one of those forms for a given year sorts at or above the
    /// bare year and strictly below "{year}-99", because '9' is above both '-' and every digit
    /// that can legally start a month. So [startYear, endYear + "-99"] is a superset of the day
    /// range that SQL can evaluate, and the exact day filter is applied after parsing.
    /// </remarks>
    public sealed class CalendarWindow
    {
        public CalendarWindow(DateOnly start, DateOnly end)
        {
            if (end < start)
            {
                (start, end) = (end, start);
            }

            Start = start;
            End = end;
        }

        public DateOnly Start { get; }

        public DateOnly End { get; }

        /// <summary>Lower bound for the coarse TEXT comparison, inclusive.</summary>
        public string CoarseLowerBound => Start.Year.ToString("D4", CultureInfo.InvariantCulture);

        /// <summary>Upper bound for the coarse TEXT comparison, inclusive.</summary>
        public string CoarseUpperBound =>
            End.Year.ToString("D4", CultureInfo.InvariantCulture) + "-99";

        public bool Contains(DateOnly day) => day >= Start && day <= End;

        /// <summary>
        /// Builds the window the *arr feeds use: a number of days back and forward from a given day.
        /// </summary>
        public static CalendarWindow FromRelativeDays(DateOnly today, int pastDays, int futureDays)
        {
            var past = pastDays < 0 ? 0 : pastDays;
            var future = futureDays < 0 ? 0 : futureDays;
            return new CalendarWindow(today.AddDays(-past), today.AddDays(future));
        }

        /// <summary>
        /// Parses a stored PublishedDate. Accepts ISO-leading forms only: a four-digit year, a
        /// hyphen, and then either the rest of a yyyy-MM-dd (with anything after it, such as a
        /// round-trip time component) or a single-digit month or day such as 2026-6-15. A bare
        /// four-digit year is also accepted and read as 1 January. Trailing whitespace is
        /// tolerated. Anything else yields null and the book is left off the calendar.
        /// </summary>
        /// <remarks>
        /// The two refusals here are deliberate narrowings rather than parsing limitations, and
        /// both exist for the same reason: the coarse bound in
        /// AudiobookRepository.GetCalendarRowsAsync compares the stored string as it sits in the
        /// column, under binary collation, against a four-digit year. A format whose lexical order
        /// does not match its chronological order can be read here and still never arrive, and a
        /// parser that accepts one is advertising a tolerance the query layer negates.
        ///
        /// Leading whitespace: a space is 0x20 and the digits start at 0x30, so a padded value
        /// sorts below every possible lower bound. Trailing whitespace is the opposite case. It
        /// does not move the value below the lower bound, and "{year}-99" is above every real
        /// month, so those rows do arrive and are read. Hence TrimEnd rather than Trim.
        ///
        /// Non-ISO-leading forms: DateTime.TryParse under the invariant culture reads a wide class
        /// of these, and their sort order is unrelated to their dates. Measured: "06/15/2026" and
        /// "15 June 2026" sort below "2026", "June 15, 2026" sorts above "2026-99", and all three
        /// parse to 2026-06-15, so the query excludes every one of them. The fallback is therefore
        /// gated on four digits and a hyphen, which keeps 2026-6-15 (admitted by the bound, and
        /// readable only by the fallback) and refuses the rest.
        ///
        /// These are reachable rather than theoretical. PublishedDate is written unvalidated from
        /// an API request (Features/Library/LibraryUpdateWorkflow.Metadata.cs:34), and the test
        /// builder writes a current-culture DateOnly.ToString() (tests/Builders/AudiobookBuilder.cs:60),
        /// which is "6/1/1996" on an en-US machine.
        ///
        /// AudiobookRepositoryCalendarTests.GetCalendarRowsAsync_AdmitsEveryStoredFormTheParserClaimsToAccept
        /// holds the two layers to this agreement over real SQLite, and seeds all of the above.
        /// </remarks>
        public static DateOnly? ParsePublishedDate(string? publishedDate)
        {
            var value = publishedDate?.TrimEnd();
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            if (char.IsWhiteSpace(value[0]))
            {
                return null;
            }

            if (value.Length >= 10
                && DateOnly.TryParseExact(
                    value[..10],
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var exact))
            {
                return exact;
            }

            if (value.Length == 4
                && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
                && year is >= 1 and <= 9999)
            {
                return new DateOnly(year, 1, 1);
            }

            if (StartsWithIsoYearAndHyphen(value)
                && DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var parsed))
            {
                return DateOnly.FromDateTime(parsed);
            }

            return null;
        }

        /// <summary>
        /// True when the value opens with four ASCII digits and a hyphen, which is the shape whose
        /// lexical order under binary collation matches its chronological order, and therefore the
        /// only shape the coarse bound can deliver.
        /// </summary>
        private static bool StartsWithIsoYearAndHyphen(string value) =>
            value.Length >= 5
            && char.IsAsciiDigit(value[0])
            && char.IsAsciiDigit(value[1])
            && char.IsAsciiDigit(value[2])
            && char.IsAsciiDigit(value[3])
            && value[4] == '-';
    }
}
