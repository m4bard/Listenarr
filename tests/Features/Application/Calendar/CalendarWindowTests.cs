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
    [InlineData("  2026-09-14  ", 2026, 9, 14)]
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
    public void ParsePublishedDate_RejectsWhatItCannotRead(string? stored)
    {
        Assert.Null(CalendarWindow.ParsePublishedDate(stored));
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
