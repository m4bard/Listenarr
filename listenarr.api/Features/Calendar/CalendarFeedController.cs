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
    /// Route, filename and parameter names follow Sonarr, Radarr and Readarr, which all serve
    /// feed/v{n}/calendar/{App}.ics with pastDays, futureDays, unmonitored and tags. An operator
    /// moving from one of those copies their existing subscription shape across unchanged.
    ///
    /// The feed sits outside the api/v{version} template on purpose: calendar clients fetch a
    /// fixed URL for years, and pinning it to an API version would break every subscription the
    /// day the API version moves.
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
        /// <param name="tags">Comma separated tag names to filter on.</param>
        /// <param name="cancellationToken">Request cancellation token.</param>
        [HttpGet(FeedFileName)]
        [Produces("text/calendar")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCalendarFeed(
            [FromQuery] int pastDays = 7,
            [FromQuery] int futureDays = 28,
            [FromQuery] bool unmonitored = false,
            [FromQuery] string? tags = null,
            CancellationToken cancellationToken = default)
        {
            var window = CalendarWindow.FromRelativeDays(
                DateOnly.FromDateTime(DateTime.Today),
                CalendarQueryParameters.ClampFeedDays(pastDays),
                CalendarQueryParameters.ClampFeedDays(futureDays));

            var events = await _calendarService.GetEventsAsync(
                window,
                unmonitored,
                CalendarQueryParameters.ParseTags(tags),
                cancellationToken);

            var document = _documentWriter.Write(events, CalendarName);

            // The subscription URL carries the API key, so it must never be cached by anything
            // between here and the client.
            Response.Headers.CacheControl = "no-store";
            return Content(document, "text/calendar; charset=utf-8");
        }
    }
}
