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

namespace Listenarr.Application.Calendar
{
    /// <summary>
    /// The columns the calendar needs from one audiobook, projected in a single query so the
    /// window does not have to materialise the full entity graph.
    /// </summary>
    public sealed class CalendarAudiobookRow
    {
        public int Id { get; init; }
        public string? Title { get; init; }
        public List<string>? Authors { get; init; }
        public List<string>? Genres { get; init; }
        public List<string>? Tags { get; init; }
        public string? Series { get; init; }
        public string? SeriesNumber { get; init; }
        public string? Asin { get; init; }
        public string? ImageUrl { get; init; }
        public string? Description { get; init; }
        public int? Runtime { get; init; }
        public string? PublishedDate { get; init; }
        public bool Monitored { get; init; }

        /// <summary>Legacy single-file path, still the only file marker on older rows.</summary>
        public string? FilePath { get; init; }

        /// <summary>Number of tracked AudiobookFile rows.</summary>
        public int FileCount { get; init; }

        public bool HasFile => FileCount > 0 || !string.IsNullOrWhiteSpace(FilePath);
    }
}
