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
[Trait("Name", "CalendarServiceTests")]
[Trait("Category", "Unit")]
public sealed class CalendarServiceTests : BaseTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 9, 16, 10, 15, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetEventsAsync_PushesTheCoarseWindowAndTheMonitoredFilterIntoTheQuery()
    {
        var audiobooks = new Mock<IAudiobookRepository>();
        audiobooks
            .Setup(repository => repository.GetCalendarRowsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarAudiobookRow>());

        var service = NewService(audiobooks, Array.Empty<int>());
        var window = new CalendarWindow(new DateOnly(2026, 3, 1), new DateOnly(2026, 10, 31));

        await service.GetEventsAsync(window, includeUnmonitored: false);

        // The date window is a SQL concern, not something the service filters after loading the
        // library. That is the whole point of the endpoint existing.
        audiobooks.Verify(
            repository => repository.GetCalendarRowsAsync(
                "2026",
                "2026-99",
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetEventsAsync_DropsRowsThatFallOutsideTheExactDayWindow()
    {
        // The coarse SQL bound is a whole year, so the service must narrow to the requested days.
        var rows = new[]
        {
            Row(1, "2026-09-01"),
            Row(2, "2026-09-15"),
            Row(3, "2026-09-30")
        };
        var service = NewService(RepositoryReturning(rows), Array.Empty<int>());
        var window = new CalendarWindow(new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 20));

        var events = await service.GetEventsAsync(window, includeUnmonitored: true);

        Assert.Equal(new[] { 2 }, events.Select(calendarEvent => calendarEvent.AudiobookId));
    }

    [Fact]
    public async Task GetEventsAsync_DropsRowsWhosePublishedDateCannotBeRead()
    {
        var rows = new[]
        {
            Row(1, "not a date"),
            Row(2, "2026-09-15")
        };
        var service = NewService(RepositoryReturning(rows), Array.Empty<int>());
        var window = new CalendarWindow(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var events = await service.GetEventsAsync(window, includeUnmonitored: true);

        Assert.Equal(new[] { 2 }, events.Select(calendarEvent => calendarEvent.AudiobookId));
    }

    [Fact]
    public async Task GetEventsAsync_ResolvesStatusFromDiskDownloadAndMonitoring()
    {
        var rows = new[]
        {
            Row(1, "2026-09-15", filePath: "/library/on-disk.m4b"),
            Row(2, "2026-09-15"),
            Row(3, "2026-09-15", monitored: false),
            Row(4, "2026-09-30"),
            Row(5, "2026-09-15")
        };
        var service = NewService(RepositoryReturning(rows), new[] { 5 });
        var window = new CalendarWindow(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var events = await service.GetEventsAsync(window, includeUnmonitored: true);
        var byId = events.ToDictionary(calendarEvent => calendarEvent.AudiobookId);

        Assert.Equal(CalendarEventStatus.Downloaded, byId[1].Status);
        Assert.Equal(CalendarEventStatus.Missing, byId[2].Status);
        Assert.Equal(CalendarEventStatus.Unmonitored, byId[3].Status);
        Assert.Equal(CalendarEventStatus.Unreleased, byId[4].Status);
        Assert.Equal(CalendarEventStatus.Downloading, byId[5].Status);
    }

    [Fact]
    public async Task GetEventsAsync_CountsTrackedFilesAsOnDiskEvenWithoutTheLegacyPath()
    {
        var rows = new[] { Row(1, "2026-09-15", fileCount: 3) };
        var service = NewService(RepositoryReturning(rows), Array.Empty<int>());
        var window = new CalendarWindow(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var events = await service.GetEventsAsync(window, includeUnmonitored: true);

        Assert.True(events.Single().HasFile);
        Assert.Equal(CalendarEventStatus.Downloaded, events.Single().Status);
    }

    [Fact]
    public async Task GetEventsAsync_FiltersOnTagsCaseInsensitively()
    {
        var rows = new[]
        {
            Row(1, "2026-09-15", tags: new List<string> { "TBR", "Scifi" }),
            Row(2, "2026-09-15", tags: new List<string> { "Reference" }),
            Row(3, "2026-09-15")
        };
        var service = NewService(RepositoryReturning(rows), Array.Empty<int>());
        var window = new CalendarWindow(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var events = await service.GetEventsAsync(
            window,
            includeUnmonitored: true,
            tags: new[] { "scifi" });

        Assert.Equal(new[] { 1 }, events.Select(calendarEvent => calendarEvent.AudiobookId));
    }

    [Fact]
    public async Task GetEventsAsync_WithNoTagFilter_ReturnsUntaggedBooksToo()
    {
        var rows = new[] { Row(1, "2026-09-15"), Row(2, "2026-09-15", tags: new List<string> { "TBR" }) };
        var service = NewService(RepositoryReturning(rows), Array.Empty<int>());
        var window = new CalendarWindow(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var events = await service.GetEventsAsync(window, includeUnmonitored: true);

        Assert.Equal(new[] { 1, 2 }, events.Select(calendarEvent => calendarEvent.AudiobookId).Order());
    }

    [Fact]
    public async Task GetEventsAsync_OrdersByReleaseDateThenTitle()
    {
        var rows = new[]
        {
            Row(1, "2026-09-20", title: "Zeta"),
            Row(2, "2026-09-10", title: "Beta"),
            Row(3, "2026-09-10", title: "Alpha")
        };
        var service = NewService(RepositoryReturning(rows), Array.Empty<int>());
        var window = new CalendarWindow(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var events = await service.GetEventsAsync(window, includeUnmonitored: true);

        Assert.Equal(new[] { 3, 2, 1 }, events.Select(calendarEvent => calendarEvent.AudiobookId));
    }

    private static Mock<IAudiobookRepository> RepositoryReturning(IReadOnlyCollection<CalendarAudiobookRow> rows)
    {
        var repository = new Mock<IAudiobookRepository>();
        repository
            .Setup(candidate => candidate.GetCalendarRowsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows.ToList());
        return repository;
    }

    private static CalendarService NewService(
        Mock<IAudiobookRepository> audiobooks,
        IReadOnlyCollection<int> activeDownloadAudiobookIds)
    {
        var downloads = new Mock<IDownloadRepository>();
        downloads
            .Setup(repository => repository.GetActiveAudiobookIdsAsync(It.IsAny<IEnumerable<DownloadStatus>>()))
            .ReturnsAsync(activeDownloadAudiobookIds.ToList());

        return new CalendarService(
            audiobooks.Object,
            downloads.Object,
            new FixedTimeProvider(FixedNow));
    }

    private static CalendarAudiobookRow Row(
        int id,
        string publishedDate,
        string title = "A Title",
        bool monitored = true,
        string? filePath = null,
        int fileCount = 0,
        List<string>? tags = null) =>
        new()
        {
            Id = id,
            Title = title,
            Authors = new List<string> { "An Author" },
            PublishedDate = publishedDate,
            Monitored = monitored,
            FilePath = filePath,
            FileCount = fileCount,
            Tags = tags
        };

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
