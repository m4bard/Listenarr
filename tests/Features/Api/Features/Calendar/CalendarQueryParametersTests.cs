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

using Listenarr.Api.Features.Calendar;
using Listenarr.Application.Calendar;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Api.Features.Calendar;

[Trait("Area", "Calendar")]
[Trait("Name", "CalendarQueryParametersTests")]
[Trait("Category", "Unit")]
public sealed class CalendarQueryParametersTests : BaseTests
{
    [Fact]
    public void ParseTags_ReadsTheSonarrSpelling()
    {
        Assert.Equal(
            new[] { "tbr", "classics" },
            CalendarQueryParameters.ParseTags("tbr, classics", tagList: null));
    }

    [Fact]
    public void ParseTags_AlsoReadsReadarrsSpelling()
    {
        // Sonarr names this parameter "tags"
        // (src/Sonarr.Api.V3/Calendar/CalendarFeedController.cs:31). Readarr names it "tagList"
        // (src/Readarr.Api.V1/Calendar/CalendarFeedController.cs:31), and Readarr is the lineage
        // parent, so an operator migrating from it arrives with a URL spelled that way. Accepting
        // both costs one parameter and means the pasted URL filters instead of quietly returning
        // everything.
        Assert.Equal(
            new[] { "tbr" },
            CalendarQueryParameters.ParseTags(tags: null, tagList: "tbr"));
    }

    [Fact]
    public void ParseTags_PrefersTagsWhenBothAreGiven()
    {
        Assert.Equal(
            new[] { "wins" },
            CalendarQueryParameters.ParseTags("wins", tagList: "loses"));
    }

    [Fact]
    public void ParseTags_FallsBackToTagListWhenTagsIsBlankRatherThanAbsent()
    {
        // A client that always emits tags= with nothing after it must not shadow tagList.
        Assert.Equal(
            new[] { "tbr" },
            CalendarQueryParameters.ParseTags("   ", tagList: "tbr"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", null)]
    [InlineData(",,", null)]
    public void ParseTags_WithNothingUsable_ReturnsAnEmptyFilter(string? tags, string? tagList)
    {
        Assert.Empty(CalendarQueryParameters.ParseTags(tags, tagList));
    }

    [Fact]
    public void ParseTags_TrimsAndDropsEmptyEntries()
    {
        Assert.Equal(
            new[] { "a", "b" },
            CalendarQueryParameters.ParseTags(" a , , b ,", tagList: null));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 7)]
    [InlineData(-1, 0)]
    [InlineData(int.MinValue, 0)]
    [InlineData(CalendarQueryParameters.MaxFeedDays, CalendarQueryParameters.MaxFeedDays)]
    [InlineData(CalendarQueryParameters.MaxFeedDays + 1, CalendarQueryParameters.MaxFeedDays)]
    [InlineData(int.MaxValue, CalendarQueryParameters.MaxFeedDays)]
    public void ClampFeedDays_HoldsTheSupportedRange(int input, int expected)
    {
        Assert.Equal(expected, CalendarQueryParameters.ClampFeedDays(input));
    }

    [Fact]
    public void MaxFeedDays_MakesTheWorstCaseWindowTwentyYearsNotThree()
    {
        // Worth stating in one place rather than being surprised by it: the ceiling applies to
        // each direction independently, so a caller asking for the maximum both ways spans twice
        // MaxFeedDays, which is 20 years, and the coarse bound then covers 21 calendar years.
        var window = CalendarWindow.FromRelativeDays(
            new DateOnly(2026, 9, 16),
            CalendarQueryParameters.ClampFeedDays(int.MaxValue),
            CalendarQueryParameters.ClampFeedDays(int.MaxValue));

        Assert.Equal(21, (window.End.Year - window.Start.Year) + 1);
    }
}
