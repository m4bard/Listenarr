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
        /// Parses a stored PublishedDate. Accepts the yyyy-MM-dd the metadata path writes, any
        /// value with a leading yyyy-MM-dd, and a bare four-digit year, which is read as 1 January.
        /// Anything else yields null and the book is left off the calendar.
        /// </summary>
        public static DateOnly? ParsePublishedDate(string? publishedDate)
        {
            var value = publishedDate?.Trim();
            if (string.IsNullOrEmpty(value))
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

            if (DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var parsed))
            {
                return DateOnly.FromDateTime(parsed);
            }

            return null;
        }
    }
}
