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

using Listenarr.Api.Attributes;
using Listenarr.Application.Calendar;
using Listenarr.Application.Calendar.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Calendar
{
    /// <summary>
    /// The iCalendar subscription feed.
    /// </summary>
    /// <remarks>
    /// Route and filename follow the family, which serves feed/v{n}/calendar/{App}.ics. Readarr
    /// builds the template in VersionedFeedControllerAttribute
    /// (readarr src/Readarr.Http/VersionedFeedControllerAttribute.cs:11,
    /// Template = $"feed/v{Version}/{resource}") and applies it as [V1FeedController("calendar")]
    /// with the action at [HttpGet("Readarr.ics")]
    /// (src/Readarr.Api.V1/Calendar/CalendarFeedController.cs:30). Sonarr's controller carries
    /// [V3FeedController("calendar")] and [HttpGet("Sonarr.ics")]
    /// (sonarr src/Sonarr.Api.V3/Calendar/CalendarFeedController.cs:16,30); its Sonarr.Http
    /// project is not in the checkout used here, so the attribute's own definition is unread and
    /// is cited from Readarr's identical copy rather than asserted of Sonarr.
    ///
    /// The parameter names are not uniform across the family, which is the one place a migrating
    /// operator's pasted URL can silently do nothing. pastDays, futureDays and unmonitored are
    /// the same everywhere. The tag filter is "tags" in Sonarr
    /// (sonarr src/Sonarr.Api.V3/Calendar/CalendarFeedController.cs:31) and "tagList" in Readarr
    /// (readarr src/Readarr.Api.V1/Calendar/CalendarFeedController.cs:31). Both are accepted here,
    /// with tags preferred; see CalendarQueryParameters.ParseTags.
    ///
    /// The feed sits outside the api/v{version} template because calendar clients hold one URL
    /// for years. This copies the family rather than improving on it: the family's feed routes are
    /// versioned too, at feed/v{n}, so they have the same problem rather than having solved it.
    /// What this route avoids is being rewritten every time the api/v{version} template moves.
    /// </remarks>
    [ApiController]
    [Route(FeedRoute)]
    [Tags("Calendar")]
    [RequireApiKey]
    public class CalendarFeedController : ControllerBase
    {
        /// <summary>Route prefix, unversioned against the API version on purpose.</summary>
        public const string FeedRoute = "feed/v1/calendar";

        /// <summary>Filename clients see. Several sniff the extension before trusting the type.</summary>
        public const string FeedFileName = "Listenarr.ics";

        /// <summary>Value of the NAME and X-WR-CALNAME properties.</summary>
        public const string CalendarName = "Listenarr Audiobook Schedule";

        private readonly ICalendarService _calendarService;
        private readonly ICalendarDocumentWriter _documentWriter;

        public CalendarFeedController(
            ICalendarService calendarService,
            ICalendarDocumentWriter documentWriter)
        {
            _calendarService = calendarService;
            _documentWriter = documentWriter;
        }

        /// <summary>
        /// Get audiobook releases as an iCalendar document.
        /// </summary>
        /// <param name="pastDays">Days back from today. Defaults to 7, as in the *arr feeds.</param>
        /// <param name="futureDays">Days forward from today. Defaults to 28, as in the *arr feeds.</param>
        /// <param name="unmonitored">Include unmonitored audiobooks.</param>
        /// <param name="tags">
        /// Comma separated tag names to filter on. Sonarr's spelling.
        /// </param>
        /// <param name="tagList">
        /// The Readarr spelling of the same filter, accepted so a URL carried over from Readarr
        /// still filters. Ignored when <paramref name="tags"/> is present.
        /// </param>
        /// <param name="cancellationToken">Request cancellation token.</param>
        [HttpGet(FeedFileName)]
        [Produces("text/calendar")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCalendarFeed(
            [FromQuery] int pastDays = 7,
            [FromQuery] int futureDays = 28,
            [FromQuery] bool unmonitored = false,
            [FromQuery] string? tags = null,
            [FromQuery] string? tagList = null,
            CancellationToken cancellationToken = default)
        {
            var window = CalendarWindow.FromRelativeDays(
                DateOnly.FromDateTime(DateTime.Today),
                CalendarQueryParameters.ClampFeedDays(pastDays),
                CalendarQueryParameters.ClampFeedDays(futureDays));

            var events = await _calendarService.GetEventsAsync(
                window,
                unmonitored,
                CalendarQueryParameters.ParseTags(tags, tagList),
                cancellationToken);

            var document = _documentWriter.Write(events, CalendarName);

            // The subscription URL carries the API key, so it must never be cached by anything
            // between here and the client.
            Response.Headers.CacheControl = "no-store";
            return Content(document, "text/calendar; charset=utf-8");
        }
    }
}
