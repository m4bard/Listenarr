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
using System.Net;
using Listenarr.Api.Features.Calendar;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Listenarr.Tests.Features.Api.Features.Calendar;

/// <summary>
/// The feed served over HTTP from real rows in the database.
/// </summary>
/// <remarks>
/// Every other calendar test stops short of this seam: the repository tests never reach the
/// service, the service tests mock the repository, the writer tests hand-build CalendarEvent, and
/// the one HTTP test that reads the body runs against an empty library. So the path where a stored
/// row with JSON-converted Authors, Genres and Tags becomes a VEVENT line was never crossed, and
/// neither was the DateOnly conversion in between.
/// </remarks>
[Trait("Area", "Calendar")]
[Trait("Name", "CalendarFeedContentTests")]
[Trait("Category", "Integration")]
public sealed class CalendarFeedContentTests : BaseTests, IClassFixture<ListenarrWebApplicationFactory>
{
    private const string FeedPath = "/feed/v1/calendar/" + CalendarFeedController.FeedFileName;

    private readonly ListenarrWebApplicationFactory _factory;

    public CalendarFeedContentTests(ListenarrWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Feed_TurnsAStoredRowIntoAVeventWithItsConvertedColumns()
    {
        var releaseDay = DateOnly.FromDateTime(DateTime.Today).AddDays(3);
        var title = $"The Time Machine {Guid.NewGuid():N}";

        Seed(book =>
        {
            book.Title = title;
            book.PublishedDate = releaseDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            book.Authors = new List<string> { "H. G. Wells" };
            book.Genres = new List<string> { "Science Fiction" };
        });

        var body = await GetFeedAsync(FeedPath);
        var lines = Unfold(body);

        // DisplayTitle joins the first author, so this line is only right if the JSON-backed
        // Authors column survived the projection and the DateOnly round trip landed on the day.
        Assert.Contains($"SUMMARY:H. G. Wells - {title}", lines);
        Assert.Contains(
            "DTSTART;VALUE=DATE:" + releaseDay.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            lines);
        Assert.Contains(
            "DTEND;VALUE=DATE:"
            + releaseDay.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            lines);
        Assert.Contains("CATEGORIES:Science Fiction", lines);
        Assert.Contains("X-LISTENARR-STATUS:unreleased", lines);
    }

    [Fact]
    public async Task Feed_FiltersOnTagsAndOnReadarrsTagListSpellingAlike()
    {
        var releaseDay = DateOnly.FromDateTime(DateTime.Today).AddDays(4);
        var stamp = Guid.NewGuid().ToString("N");
        var wanted = $"Tagged {stamp}";
        var other = $"Untagged {stamp}";

        Seed(book =>
        {
            book.Title = wanted;
            book.PublishedDate = releaseDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            book.Tags = new List<string> { $"keep-{stamp}" };
        });
        Seed(book =>
        {
            book.Title = other;
            book.PublishedDate = releaseDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            book.Tags = new List<string> { $"drop-{stamp}" };
        });

        var viaTags = await GetFeedAsync($"{FeedPath}?tags=keep-{stamp}");
        var viaTagList = await GetFeedAsync($"{FeedPath}?tagList=keep-{stamp}");

        foreach (var body in new[] { viaTags, viaTagList })
        {
            Assert.Contains(wanted, body, StringComparison.Ordinal);
            Assert.DoesNotContain(other, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Feed_LeavesABookOutsideTheWindowOffTheDocument()
    {
        var stamp = Guid.NewGuid().ToString("N");
        var farFuture = DateOnly.FromDateTime(DateTime.Today).AddDays(400);

        Seed(book =>
        {
            book.Title = $"Far Future {stamp}";
            book.PublishedDate = farFuture.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        });

        // The default window is 28 days forward, so this book must not appear, and must appear
        // once the caller asks for a window that reaches it. Both directions, one seeded row.
        var defaultWindow = await GetFeedAsync(FeedPath);
        var wideWindow = await GetFeedAsync($"{FeedPath}?futureDays=500");

        Assert.DoesNotContain(stamp, defaultWindow, StringComparison.Ordinal);
        Assert.Contains(stamp, wideWindow, StringComparison.Ordinal);
    }

    private async Task<string> GetFeedAsync(string path)
    {
        using var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private void Seed(Action<Audiobook> configure)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();

        var book = new Audiobook { Title = "seeded", Monitored = true };
        configure(book);

        db.Audiobooks.Add(book);
        db.SaveChanges();
    }

    private static string[] Unfold(string document) =>
        document
            .Replace("\r\n ", string.Empty, StringComparison.Ordinal)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
}
