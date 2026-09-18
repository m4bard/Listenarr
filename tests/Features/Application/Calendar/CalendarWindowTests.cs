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

using Listenarr.Application.Calendar;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Calendar;

[Trait("Area", "Calendar")]
[Trait("Name", "CalendarWindowTests")]
[Trait("Category", "Unit")]
public sealed class CalendarWindowTests : BaseTests
{
    [Fact]
    public void CoarseBounds_BracketEveryStoredDateFormatInTheYear()
    {
        var window = new CalendarWindow(new DateOnly(2026, 3, 1), new DateOnly(2026, 10, 31));

        // Every form this codebase writes into PublishedDate must sort inside the coarse bounds,
        // otherwise the SQL pre-filter silently drops rows the calendar should show.
        var storedForms = new[]
        {
            "2026",                       // bare year
            "2026-01-01",                 // MetadataConverters fallback
            "2026-03-01",                 // yyyy-MM-dd, the canonical form
            "2026-10-31T00:00:00.0000000+00:00",  // round-trip "o" from the indexer parsers
            "2026-12-31"
        };

        foreach (var stored in storedForms)
        {
            Assert.True(
                string.CompareOrdinal(stored, window.CoarseLowerBound) >= 0,
                $"{stored} sorted below the coarse lower bound {window.CoarseLowerBound}");
            Assert.True(
                string.CompareOrdinal(stored, window.CoarseUpperBound) <= 0,
                $"{stored} sorted above the coarse upper bound {window.CoarseUpperBound}");
        }
    }

    [Fact]
    public void CoarseBounds_ExcludeAdjacentYears()
    {
        var window = new CalendarWindow(new DateOnly(2026, 3, 1), new DateOnly(2026, 10, 31));

        Assert.True(string.CompareOrdinal("2025-12-31", window.CoarseLowerBound) < 0);
        Assert.True(string.CompareOrdinal("2027-01-01", window.CoarseUpperBound) > 0);
    }

    [Fact]
    public void Constructor_SwapsAnInvertedRange()
    {
        var window = new CalendarWindow(new DateOnly(2026, 10, 31), new DateOnly(2026, 3, 1));

        Assert.Equal(new DateOnly(2026, 3, 1), window.Start);
        Assert.Equal(new DateOnly(2026, 10, 31), window.End);
    }

    [Fact]
    public void Contains_IsInclusiveOnBothEnds()
    {
        var window = new CalendarWindow(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        Assert.True(window.Contains(new DateOnly(2026, 9, 1)));
        Assert.True(window.Contains(new DateOnly(2026, 9, 30)));
        Assert.False(window.Contains(new DateOnly(2026, 8, 31)));
        Assert.False(window.Contains(new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void FromRelativeDays_MatchesTheArrFeedDefaults()
    {
        var window = CalendarWindow.FromRelativeDays(new DateOnly(2026, 9, 16), 7, 28);

        Assert.Equal(new DateOnly(2026, 9, 9), window.Start);
        Assert.Equal(new DateOnly(2026, 10, 14), window.End);
    }

    [Fact]
    public void FromRelativeDays_TreatsNegativeCountsAsZero()
    {
        var window = CalendarWindow.FromRelativeDays(new DateOnly(2026, 9, 16), -5, -5);

        Assert.Equal(new DateOnly(2026, 9, 16), window.Start);
        Assert.Equal(new DateOnly(2026, 9, 16), window.End);
    }

    [Theory]
    [InlineData("2026-09-14", 2026, 9, 14)]
    [InlineData("2026-09-14T13:45:00.0000000+00:00", 2026, 9, 14)]
    [InlineData("2026-09-14T23:30:00Z", 2026, 9, 14)]
    [InlineData("2026-9-14", 2026, 9, 14)]
    [InlineData("2026-09-14  ", 2026, 9, 14)]
    [InlineData("2026", 2026, 1, 1)]
    public void ParsePublishedDate_AcceptsEveryFormatThisCodebaseWrites(
        string stored,
        int year,
        int month,
        int day)
    {
        Assert.Equal(new DateOnly(year, month, day), CalendarWindow.ParsePublishedDate(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a date")]
    [InlineData("0000")]
    [InlineData("  2026-09-14")]
    [InlineData("  2026-09-14  ")]
    [InlineData("\t2026-09-14")]
    [InlineData("06/15/2026")]
    [InlineData("15 June 2026")]
    [InlineData("June 15, 2026")]
    [InlineData("6/1/1996")]
    [InlineData("20260914")]
    public void ParsePublishedDate_RejectsWhatItCannotRead(string? stored)
    {
        Assert.Null(CalendarWindow.ParsePublishedDate(stored));
    }

    [Fact]
    public void ParsePublishedDate_RefusesFormsWhoseSortOrderIsNotTheirDateOrder()
    {
        // The narrowing this exists for. DateTime.TryParse under the invariant culture reads a
        // wide class of forms whose lexical order has nothing to do with their chronological
        // order, and the coarse bound compares the stored string as it sits in the column. So a
        // parser that accepted these would be claiming a tolerance the query layer negates, which
        // is the same defect as the leading-whitespace case one level further out.
        //
        // All three parse to 2026-06-15 under a bare TryParse. Two sort below the lower bound and
        // one sorts above the upper bound, which is worth spelling out: they are excluded by
        // opposite ends of the range, not by one mechanism.
        var window = new CalendarWindow(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        Assert.True(string.CompareOrdinal("06/15/2026", window.CoarseLowerBound) < 0);
        Assert.True(string.CompareOrdinal("15 June 2026", window.CoarseLowerBound) < 0);
        Assert.True(string.CompareOrdinal("June 15, 2026", window.CoarseUpperBound) > 0);

        Assert.Null(CalendarWindow.ParsePublishedDate("06/15/2026"));
        Assert.Null(CalendarWindow.ParsePublishedDate("15 June 2026"));
        Assert.Null(CalendarWindow.ParsePublishedDate("June 15, 2026"));

        // The one non-canonical form the bound does deliver is kept, and it is readable only by
        // the gated fallback: length 9 skips TryParseExact and it is not a bare year.
        Assert.True(string.CompareOrdinal("2026-6-15", window.CoarseLowerBound) > 0);
        Assert.True(string.CompareOrdinal("2026-6-15", window.CoarseUpperBound) < 0);
        Assert.Equal(new DateOnly(2026, 6, 15), CalendarWindow.ParsePublishedDate("2026-6-15"));
    }

    [Fact]
    public void ParsePublishedDate_RefusesLeadingWhitespaceBecauseTheQueryLayerNeverDeliversIt()
    {
        // Spelled out separately from the reject theory because this is a deliberate narrowing,
        // not a parsing limitation. A space is 0x20 and the coarse lower bound is always four
        // digits starting at 0x30, so a padded value sorts below every possible bound and the row
        // never reaches the parser. Trimming here would make the parser claim a format it cannot
        // actually see, which is what the crossing test in AudiobookRepositoryCalendarTests
        // exists to prevent.
        var window = new CalendarWindow(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        Assert.True(string.CompareOrdinal("  2026-09-14", window.CoarseLowerBound) < 0);
        Assert.Null(CalendarWindow.ParsePublishedDate("  2026-09-14"));

        // Trailing padding is the opposite case: it sorts inside the bound, so it is read.
        Assert.True(string.CompareOrdinal("2026-09-14  ", window.CoarseLowerBound) > 0);
        Assert.True(string.CompareOrdinal("2026-09-14  ", window.CoarseUpperBound) < 0);
        Assert.Equal(new DateOnly(2026, 9, 14), CalendarWindow.ParsePublishedDate("2026-09-14  "));
    }

    [Fact]
    public void CoarseBounds_SpanAYearBoundary()
    {
        // The default 7/28 day feed crosses a year boundary for 35 days out of every 365, and
        // spanning years is the only reason the bound is a range rather than a single year. No
        // test at any layer constructed a window that crossed one.
        var window = new CalendarWindow(new DateOnly(2025, 12, 28), new DateOnly(2026, 1, 10));

        Assert.Equal("2025", window.CoarseLowerBound);
        Assert.Equal("2026-99", window.CoarseUpperBound);

        foreach (var stored in new[]
                 {
                     "2025",
                     "2025-12-28",
                     "2025-12-31T18:00:00.0000000+00:00",
                     "2026",
                     "2026-01-10"
                 })
        {
            Assert.True(
                string.CompareOrdinal(stored, window.CoarseLowerBound) >= 0,
                $"{stored} sorted below the coarse lower bound {window.CoarseLowerBound}");
            Assert.True(
                string.CompareOrdinal(stored, window.CoarseUpperBound) <= 0,
                $"{stored} sorted above the coarse upper bound {window.CoarseUpperBound}");
        }

        Assert.True(string.CompareOrdinal("2024-12-31", window.CoarseLowerBound) < 0);
        Assert.True(string.CompareOrdinal("2027-01-01", window.CoarseUpperBound) > 0);
    }

    [Fact]
    public void ParsePublishedDate_KeepsTheStoredDayRatherThanShiftingItByTimezone()
    {
        // A stored value of 2026-09-14T23:30:00Z is a release on the 14th. Converting to local
        // time would move it to the 15th for anyone east of UTC, which is how a release silently
        // lands on the wrong calendar day.
        Assert.Equal(
            new DateOnly(2026, 9, 14),
            CalendarWindow.ParsePublishedDate("2026-09-14T23:30:00Z"));
    }
}
