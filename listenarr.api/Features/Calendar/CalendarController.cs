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
using Listenarr.Application.Calendar.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Calendar
{
    /// <summary>
    /// Dated audiobook releases over a window, the server-side equivalent of the Sonarr and
    /// Readarr calendar APIs.
    /// </summary>
    [ApiController]
    [Route("api/v{version:apiVersion}/calendar")]
    [Tags("Calendar")]
    public class CalendarController : ControllerBase
    {
        private readonly ICalendarService _calendarService;

        public CalendarController(ICalendarService calendarService)
        {
            _calendarService = calendarService;
        }

        /// <summary>
        /// Get audiobook releases between two dates.
        /// </summary>
        /// <param name="start">
        /// Inclusive first day. Defaults to today, matching the *arr calendar APIs.
        /// </param>
        /// <param name="end">
        /// Inclusive last day. Defaults to two days after <paramref name="start"/>, again matching
        /// the family. The calendar page always sends an explicit window, so the default only
        /// matters to a direct caller.
        /// </param>
        /// <param name="unmonitored">Include unmonitored audiobooks.</param>
        /// <param name="tags">
        /// Comma separated tag names. When present, only audiobooks carrying at least one are
        /// returned. Matching is case insensitive.
        /// </param>
        /// <param name="tagList">
        /// The Readarr spelling of the same filter, accepted here as well so the two endpoints
        /// agree. Ignored when <paramref name="tags"/> is present.
        /// </param>
        /// <param name="cancellationToken">Request cancellation token.</param>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<CalendarEventDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<CalendarEventDto>>> GetCalendar(
            [FromQuery] DateOnly? start,
            [FromQuery] DateOnly? end,
            [FromQuery] bool unmonitored = false,
            [FromQuery] string? tags = null,
            [FromQuery] string? tagList = null,
            CancellationToken cancellationToken = default)
        {
            var startDay = start ?? DateOnly.FromDateTime(DateTime.Today);
            var endDay = end ?? startDay.AddDays(2);
            var window = new CalendarWindow(startDay, endDay);

            var events = await _calendarService.GetEventsAsync(
                window,
                unmonitored,
                CalendarQueryParameters.ParseTags(tags, tagList),
                cancellationToken);

            return Ok(events.Select(CalendarEventDtoFactory.Create).ToList());
        }
    }
}
