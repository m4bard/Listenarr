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

using System.Text;
using Listenarr.Application.Calendar;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Calendar;

[Trait("Area", "Calendar")]
[Trait("Name", "CalendarDocumentWriterTests")]
[Trait("Category", "Unit")]
public sealed class CalendarDocumentWriterTests : BaseTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 9, 16, 10, 15, 0, TimeSpan.Zero);

    [Fact]
    public void Write_ProducesAWellFormedCalendarEnvelope()
    {
        var document = NewWriter().Write(new[] { SampleEvent() }, "Listenarr Audiobook Schedule");
        var lines = Unfold(document);

        Assert.Equal("BEGIN:VCALENDAR", lines[0]);
        Assert.Equal("END:VCALENDAR", lines[^1]);
        Assert.Contains("VERSION:2.0", lines);
        Assert.Contains("PRODID:" + CalendarDocumentWriter.ProductId, lines);
        Assert.Contains("CALSCALE:GREGORIAN", lines);

        // Both spellings: NAME is the RFC 7986 property, X-WR-CALNAME is what Google and Apple
        // actually read. The *arr feeds set both and so must this one.
        Assert.Contains("NAME:Listenarr Audiobook Schedule", lines);
        Assert.Contains("X-WR-CALNAME:Listenarr Audiobook Schedule", lines);
    }

    [Fact]
    public void Write_UsesCrLfLineEndingsThroughout()
    {
        var document = NewWriter().Write(new[] { SampleEvent() }, "Listenarr");

        // RFC 5545 section 3.1. A bare LF is the single most common reason a hand-built feed is
        // rejected by strict clients, so assert the absence of one rather than the presence of CRLF.
        Assert.DoesNotContain('\n', document.Replace("\r\n", string.Empty, StringComparison.Ordinal));
        Assert.EndsWith("END:VCALENDAR\r\n", document, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_EmitsAnExclusiveEndDateForAnAllDayEvent()
    {
        var document = NewWriter().Write(new[] { SampleEvent() }, "Listenarr");
        var lines = Unfold(document);

        Assert.Contains("DTSTART;VALUE=DATE:20260914", lines);

        // RFC 5545 section 3.6.1: DTEND is exclusive. A one-day event on the 14th ends on the 15th.
        Assert.Contains("DTEND;VALUE=DATE:20260915", lines);
    }

    [Fact]
    public void Write_SetsStatusFromWhatIsOnDisk()
    {
        var onDisk = NewWriter().Write(new[] { SampleEvent(hasFile: true) }, "Listenarr");
        var missing = NewWriter().Write(new[] { SampleEvent(hasFile: false) }, "Listenarr");

        Assert.Contains("STATUS:CONFIRMED", Unfold(onDisk));
        Assert.Contains("STATUS:TENTATIVE", Unfold(missing));
    }

    [Fact]
    public void Write_CarriesTheRicherStateOnAnExtensionProperty()
    {
        var document = NewWriter().Write(
            new[] { SampleEvent(status: CalendarEventStatus.Downloading) },
            "Listenarr");

        // STATUS only has two usable values, so the five-way state the calendar API returns rides
        // on an X- property rather than being lost at the feed boundary.
        Assert.Contains("X-LISTENARR-STATUS:downloading", Unfold(document));
    }

    [Fact]
    public void Write_GivesEachEventAStableUid()
    {
        var first = NewWriter().Write(new[] { SampleEvent() }, "Listenarr");
        var second = NewWriter().Write(new[] { SampleEvent() }, "Listenarr");

        var uid = Unfold(first).Single(line => line.StartsWith("UID:", StringComparison.Ordinal));

        Assert.Equal("UID:" + CalendarDocumentWriter.UidPrefix + "41@listenarr", uid);
        Assert.Contains(uid, Unfold(second));
    }

    [Fact]
    public void Write_EscapesTextValues()
    {
        var document = NewWriter().Write(
            new[] { SampleEvent(description: "A comma, a semicolon; a backslash \\ and\na newline") },
            "Listenarr");

        var description = Unfold(document)
            .Single(line => line.StartsWith("DESCRIPTION:", StringComparison.Ordinal));

        Assert.Equal(
            "DESCRIPTION:A comma\\, a semicolon\\; a backslash \\\\ and\\na newline",
            description);
    }

    [Fact]
    public void Write_TreatsCategoryCommasAsListSeparatorsNotText()
    {
        var document = NewWriter().Write(
            new[] { SampleEvent(genres: new[] { "Science Fiction", "Time Travel" }) },
            "Listenarr");

        var categories = Unfold(document)
            .Single(line => line.StartsWith("CATEGORIES:", StringComparison.Ordinal));

        Assert.Equal("CATEGORIES:Science Fiction,Time Travel", categories);
    }

    [Fact]
    public void Write_FoldsLongLinesAtSeventyFiveOctets()
    {
        var longTitle = new string('x', 400);
        var document = NewWriter().Write(new[] { SampleEvent(title: longTitle) }, "Listenarr");

        foreach (var line in document.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.True(
                Encoding.UTF8.GetByteCount(line) <= 75,
                $"line exceeded 75 octets: {Encoding.UTF8.GetByteCount(line)}");
        }

        // Folding must be reversible: unfolding has to give the summary back intact.
        Assert.Contains(
            "SUMMARY:H. G. Wells - " + longTitle,
            Unfold(document));
    }

    [Fact]
    public void Write_NeverFoldsInsideAMultiByteCharacter()
    {
        // Three-octet characters do not divide into the 75 octet budget, so a naive byte-slice
        // fold lands mid-sequence and the client renders mojibake or rejects the feed.
        var title = string.Concat(Enumerable.Repeat("\u6f22", 200));
        var document = NewWriter().Write(new[] { SampleEvent(title: title) }, "Listenarr");

        foreach (var line in document.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.True(Encoding.UTF8.GetByteCount(line) <= 75);
        }

        Assert.Contains("SUMMARY:H. G. Wells - " + title, Unfold(document));
    }

    [Fact]
    public void Write_OmitsOptionalPropertiesThatHaveNoValue()
    {
        var document = NewWriter().Write(
            new[] { SampleEvent(description: null, genres: null) },
            "Listenarr");

        Assert.DoesNotContain("DESCRIPTION:", document, StringComparison.Ordinal);
        Assert.DoesNotContain("CATEGORIES:", document, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WithNoEvents_StillProducesASubscribableCalendar()
    {
        var document = NewWriter().Write(Array.Empty<CalendarEvent>(), "Listenarr");
        var lines = Unfold(document);

        Assert.Equal("BEGIN:VCALENDAR", lines[0]);
        Assert.Equal("END:VCALENDAR", lines[^1]);
        Assert.DoesNotContain("BEGIN:VEVENT", lines);
    }

    private static CalendarDocumentWriter NewWriter() =>
        new(new FixedTimeProvider(FixedNow));

    private static CalendarEvent SampleEvent(
        string title = "The Time Machine",
        bool hasFile = false,
        string? description = "An inventor travels forward.",
        string[]? genres = null,
        string status = CalendarEventStatus.Missing) =>
        new()
        {
            AudiobookId = 41,
            Title = title,
            Authors = new[] { "H. G. Wells" },
            Genres = genres,
            Description = description,
            ReleaseDate = new DateOnly(2026, 9, 14),
            Monitored = true,
            HasFile = hasFile,
            Status = status
        };

    /// <summary>Reverses RFC 5545 folding so assertions can talk about logical lines.</summary>
    private static string[] Unfold(string document) =>
        document
            .Replace("\r\n ", string.Empty, StringComparison.Ordinal)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
