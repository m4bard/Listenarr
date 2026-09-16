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
using System.Text;
using Listenarr.Application.Calendar.Contracts;

namespace Listenarr.Application.Calendar
{
    /// <summary>
    /// Writes all-day VEVENTs, one per dated audiobook release.
    /// </summary>
    public sealed class CalendarDocumentWriter : ICalendarDocumentWriter
    {
        /// <summary>Product identifier, in the form the rest of the *arr family uses.</summary>
        public const string ProductId = "-//listenarr//Listenarr//EN";

        /// <summary>UID prefix, stable across regenerations so clients update rather than duplicate.</summary>
        public const string UidPrefix = "Listenarr_audiobook_";

        private readonly TimeProvider _timeProvider;

        public CalendarDocumentWriter(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        public string Write(IReadOnlyCollection<CalendarEvent> events, string calendarName)
        {
            ArgumentNullException.ThrowIfNull(events);

            var name = string.IsNullOrWhiteSpace(calendarName) ? "Listenarr" : calendarName.Trim();
            var stamp = CalendarContentLine.FormatUtcTimestamp(_timeProvider.GetUtcNow());
            var document = new StringBuilder();

            Append(document, "BEGIN:VCALENDAR");
            Append(document, "VERSION:2.0");
            Append(document, "PRODID:" + ProductId);
            Append(document, "CALSCALE:GREGORIAN");
            Append(document, "METHOD:PUBLISH");
            Append(document, "NAME:" + CalendarContentLine.EscapeText(name));
            Append(document, "X-WR-CALNAME:" + CalendarContentLine.EscapeText(name));

            foreach (var calendarEvent in events)
            {
                AppendEvent(document, calendarEvent, stamp);
            }

            Append(document, "END:VCALENDAR");
            return document.ToString();
        }

        private static void AppendEvent(StringBuilder document, CalendarEvent calendarEvent, string stamp)
        {
            Append(document, "BEGIN:VEVENT");
            Append(
                document,
                "UID:" + UidPrefix
                       + calendarEvent.AudiobookId.ToString(CultureInfo.InvariantCulture)
                       + "@listenarr");
            Append(document, "DTSTAMP:" + stamp);

            // RFC 5545 section 3.6.1: for a DATE value the end is exclusive, so a single day event
            // ends on the following day. Clients that take DTEND literally otherwise drop the event
            // from the day it belongs to.
            Append(
                document,
                "DTSTART;VALUE=DATE:" + CalendarContentLine.FormatDate(calendarEvent.ReleaseDate));
            Append(
                document,
                "DTEND;VALUE=DATE:"
                + CalendarContentLine.FormatDate(calendarEvent.ReleaseDate.AddDays(1)));

            Append(document, "SUMMARY:" + CalendarContentLine.EscapeText(calendarEvent.DisplayTitle));

            if (!string.IsNullOrWhiteSpace(calendarEvent.Description))
            {
                Append(
                    document,
                    "DESCRIPTION:" + CalendarContentLine.EscapeText(calendarEvent.Description));
            }

            var categories = BuildCategories(calendarEvent.Genres);
            if (categories.Length > 0)
            {
                Append(document, "CATEGORIES:" + categories);
            }

            // Sonarr's rule: on disk is CONFIRMED, anything else is still provisional. These are
            // the only two STATUS values RFC 5545 defines that fit a release that has not landed.
            Append(document, "STATUS:" + (calendarEvent.HasFile ? "CONFIRMED" : "TENTATIVE"));
            Append(document, "TRANSP:TRANSPARENT");
            Append(document, "X-LISTENARR-STATUS:" + CalendarContentLine.EscapeText(calendarEvent.Status));
            Append(document, "END:VEVENT");
        }

        /// <summary>
        /// CATEGORIES is a comma separated list, so each value is escaped on its own and the
        /// separating commas are left bare.
        /// </summary>
        private static string BuildCategories(string[]? genres) =>
            genres is null
                ? string.Empty
                : string.Join(
                    ',',
                    genres
                        .Where(genre => !string.IsNullOrWhiteSpace(genre))
                        .Select(genre => CalendarContentLine.EscapeText(genre.Trim())));

        private static void Append(StringBuilder document, string contentLine) =>
            CalendarContentLine.AppendFolded(document, contentLine);
    }
}
