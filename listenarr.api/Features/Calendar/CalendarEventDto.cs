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

namespace Listenarr.Api.Features.Calendar
{
    /// <summary>
    /// One dated audiobook release, as returned by the calendar API.
    /// </summary>
    public sealed class CalendarEventDto
    {
        public int AudiobookId { get; init; }
        public string? Title { get; init; }
        public string[]? Authors { get; init; }
        public string? Series { get; init; }
        public string? SeriesNumber { get; init; }
        public string[]? Genres { get; init; }
        public string? Asin { get; init; }
        public string? ImageUrl { get; init; }
        public int? Runtime { get; init; }

        /// <summary>Release day as yyyy-MM-dd. No time component: releases are all-day events.</summary>
        public string ReleaseDate { get; init; } = string.Empty;

        public bool Monitored { get; init; }
        public bool HasFile { get; init; }

        /// <summary>
        /// One of downloading, downloaded, unmonitored, missing or unreleased. Same vocabulary as
        /// the Sonarr and Readarr calendar legends.
        /// </summary>
        public string Status { get; init; } = string.Empty;
    }
}
