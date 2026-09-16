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
[Trait("Name", "CalendarEventStatusTests")]
[Trait("Category", "Unit")]
public sealed class CalendarEventStatusTests : BaseTests
{
    private static readonly DateOnly Today = new(2026, 9, 16);

    [Fact]
    public void Compute_InFlightDownload_OutranksEverythingElse()
    {
        // An unmonitored book with a file, still downloading, reads as downloading: this is the
        // precedence Readarr's getStatusStyle applies, and it is what the legend expects.
        var status = CalendarEventStatus.Compute(
            isDownloading: true,
            hasFile: true,
            monitored: false,
            releaseDate: Today.AddDays(-30),
            today: Today);

        Assert.Equal(CalendarEventStatus.Downloading, status);
    }

    [Fact]
    public void Compute_FileOnDisk_OutranksMonitoringAndReleaseDate()
    {
        var status = CalendarEventStatus.Compute(
            isDownloading: false,
            hasFile: true,
            monitored: false,
            releaseDate: Today.AddDays(30),
            today: Today);

        Assert.Equal(CalendarEventStatus.Downloaded, status);
    }

    [Fact]
    public void Compute_Unmonitored_OutranksMissingAndUnreleased()
    {
        var status = CalendarEventStatus.Compute(
            isDownloading: false,
            hasFile: false,
            monitored: false,
            releaseDate: Today.AddDays(-1),
            today: Today);

        Assert.Equal(CalendarEventStatus.Unmonitored, status);
    }

    [Fact]
    public void Compute_MonitoredAndReleasedWithNoFile_IsMissing()
    {
        var status = CalendarEventStatus.Compute(
            isDownloading: false,
            hasFile: false,
            monitored: true,
            releaseDate: Today.AddDays(-1),
            today: Today);

        Assert.Equal(CalendarEventStatus.Missing, status);
    }

    [Fact]
    public void Compute_ReleasingToday_IsMissingRatherThanUnreleased()
    {
        // The release day itself counts as released. A book that came out this morning belongs in
        // the work queue, not in the "not out yet" bucket.
        var status = CalendarEventStatus.Compute(
            isDownloading: false,
            hasFile: false,
            monitored: true,
            releaseDate: Today,
            today: Today);

        Assert.Equal(CalendarEventStatus.Missing, status);
    }

    [Fact]
    public void Compute_MonitoredAndFutureDated_IsUnreleased()
    {
        var status = CalendarEventStatus.Compute(
            isDownloading: false,
            hasFile: false,
            monitored: true,
            releaseDate: Today.AddDays(1),
            today: Today);

        Assert.Equal(CalendarEventStatus.Unreleased, status);
    }
}
