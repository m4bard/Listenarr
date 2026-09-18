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
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Repositories;

[Trait("Area", "Calendar")]
[Trait("Name", "AudiobookRepositoryCalendarTests")]
[Trait("Category", "Persistence")]
public sealed class AudiobookRepositoryCalendarTests : BaseTests
{
    [Fact]
    public async Task GetCalendarRowsAsync_TranslatesTheCoarseRangeToSqlAndKeepsEveryStoredDateFormat()
    {
        // Real SQLite, not the in-memory provider: the coarse bound is a string comparison pushed
        // into the query, and an in-memory provider would evaluate it in LINQ and prove nothing
        // about whether it translates.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = await NewContextAsync(connection);

        context.Audiobooks.AddRange(
            Book("bare year", "2026"),
            Book("canonical", "2026-06-15"),
            Book("round trip", "2026-12-31T18:00:00.0000000+00:00"),
            Book("year before", "2025-12-31"),
            Book("year after", "2027-01-01"),
            Book("null date", null),
            Book("empty date", string.Empty));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var repository = new AudiobookRepository(context);
        var rows = await repository.GetCalendarRowsAsync("2026", "2026-99", includeUnmonitored: true);

        Assert.Equal(
            new[] { "bare year", "canonical", "round trip" },
            rows.Select(row => row.Title).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetCalendarRowsAsync_ExcludesUnmonitoredUnlessAsked()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = await NewContextAsync(connection);

        context.Audiobooks.AddRange(
            Book("monitored", "2026-06-15"),
            Book("unmonitored", "2026-06-15", monitored: false));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var repository = new AudiobookRepository(context);

        var withoutUnmonitored =
            await repository.GetCalendarRowsAsync("2026", "2026-99", includeUnmonitored: false);
        var withUnmonitored =
            await repository.GetCalendarRowsAsync("2026", "2026-99", includeUnmonitored: true);

        Assert.Equal(new[] { "monitored" }, withoutUnmonitored.Select(row => row.Title));
        Assert.Equal(2, withUnmonitored.Count);
    }

    [Fact]
    public async Task GetCalendarRowsAsync_ProjectsFileStateWithoutLoadingTheFileGraph()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = await NewContextAsync(connection);

        var tracked = Book("tracked files", "2026-06-15");
        var legacy = Book("legacy path", "2026-06-15");
        legacy.FilePath = "/library/legacy.m4b";
        var neither = Book("no files", "2026-06-15");
        context.Audiobooks.AddRange(tracked, legacy, neither);
        await context.SaveChangesAsync();

        foreach (var index in Enumerable.Range(0, 3))
        {
            var file = AudiobookFile.CreateUnresolved($"/library/tracked/part-{index}.m4b");
            file.AudiobookId = tracked.Id;
            context.AudiobookFiles.Add(file);
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var repository = new AudiobookRepository(context);
        var rows = await repository.GetCalendarRowsAsync("2026", "2026-99", includeUnmonitored: true);
        var byTitle = rows.ToDictionary(row => row.Title!, StringComparer.Ordinal);

        Assert.Equal(3, byTitle["tracked files"].FileCount);
        Assert.True(byTitle["tracked files"].HasFile);
        Assert.True(byTitle["legacy path"].HasFile);
        Assert.False(byTitle["no files"].HasFile);

        // The projection must not drag the file rows into the change tracker: the calendar reads a
        // window, and materialising every file in it is the cost this endpoint exists to avoid.
        Assert.Empty(context.ChangeTracker.Entries<AudiobookFile>());
    }

    [Fact]
    public async Task GetCalendarRowsAsync_MaterialisesTheJsonBackedListColumns()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = await NewContextAsync(connection);

        var book = Book("with lists", "2026-06-15");
        book.Authors = new List<string> { "H. G. Wells" };
        book.Genres = new List<string> { "Science Fiction" };
        book.Tags = new List<string> { "TBR" };
        context.Audiobooks.Add(book);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var repository = new AudiobookRepository(context);
        var row = Assert.Single(
            await repository.GetCalendarRowsAsync("2026", "2026-99", includeUnmonitored: true));

        // These are TEXT columns behind a JSON value converter. Projecting them in a Select rather
        // than loading the entity has to keep the conversion.
        Assert.Equal(new[] { "H. G. Wells" }, row.Authors);
        Assert.Equal(new[] { "Science Fiction" }, row.Genres);
        Assert.Equal(new[] { "TBR" }, row.Tags);
    }

    [Fact]
    public async Task GetCalendarRowsAsync_AdmitsEveryStoredFormTheParserClaimsToAccept()
    {
        // The seam. CalendarWindow.ParsePublishedDate decides which stored strings become events;
        // the coarse bound in this query decides which rows the parser ever sees. Each half was
        // tested where it is right and nothing crossed them, which is how a value gets asserted
        // acceptable in one file and silently dropped in another.
        //
        // The invariant is one directional: anything the parser accepts must be admitted by SQL.
        // The reverse does not hold, and should not: "2026-13-45" sorts inside the bound and is
        // not a date, so the parser is right to refuse it.
        var storedForms = new[]
        {
            "2026",
            "2026-06-15",
            "2026-12-31T18:00:00.0000000+00:00",
            "2026-6-15",            // single digit month, only the TryParse fallback reads this
            "2026-06-15  ",         // trailing padding: sorts inside the bound
            "  2026-06-15",         // leading padding: sorts below "2026" because ' ' < '2'
            "2026-13-45",           // inside the bound, not a date
            "not a date"
        };

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = await NewContextAsync(connection);

        context.Audiobooks.AddRange(storedForms.Select(form => Book(form, form)));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var repository = new AudiobookRepository(context);
        var admitted = (await repository.GetCalendarRowsAsync("2026", "2026-99", includeUnmonitored: true))
            .Select(row => row.Title!)
            .ToHashSet(StringComparer.Ordinal);

        var advertisedButUnreachable = storedForms
            .Where(form => CalendarWindow.ParsePublishedDate(form) is not null)
            .Where(form => !admitted.Contains(form))
            .ToList();

        Assert.Empty(advertisedButUnreachable);
    }

    [Fact]
    public async Task GetCalendarRowsAsync_SpansAYearBoundary()
    {
        // The default 7/28 day feed crosses a year boundary for 35 days out of every 365, and
        // surviving that is the only reason the coarse bound is a year range rather than a year.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = await NewContextAsync(connection);

        context.Audiobooks.AddRange(
            Book("late december", "2025-12-30"),
            Book("early january", "2026-01-05"),
            Book("bare year before", "2025"),
            Book("round trip in december", "2025-12-31T18:00:00.0000000+00:00"),
            Book("two years earlier", "2024-12-30"),
            Book("year after", "2027-01-01"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var window = new CalendarWindow(new DateOnly(2025, 12, 28), new DateOnly(2026, 1, 10));
        var repository = new AudiobookRepository(context);
        var rows = await repository.GetCalendarRowsAsync(
            window.CoarseLowerBound,
            window.CoarseUpperBound,
            includeUnmonitored: true);

        Assert.Equal(
            new[] { "bare year before", "early january", "late december", "round trip in december" },
            rows.Select(row => row.Title).Order(StringComparer.Ordinal));
    }

    private static async Task<ListenArrDbContext> NewContextAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new ListenArrDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static Audiobook Book(string title, string? publishedDate, bool monitored = true) =>
        new()
        {
            Title = title,
            PublishedDate = publishedDate,
            Monitored = monitored
        };
}
