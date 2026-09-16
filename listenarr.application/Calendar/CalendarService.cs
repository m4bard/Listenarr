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

using Listenarr.Application.Calendar.Contracts;

namespace Listenarr.Application.Calendar
{
    /// <summary>
    /// Builds calendar events from the audiobook window query, resolving each event's state from
    /// what is on disk, what is in flight, and whether the release day has passed.
    /// </summary>
    public sealed class CalendarService : ICalendarService
    {
        private static readonly DownloadStatus[] ActiveCalendarDownloadStatuses =
        {
            DownloadStatus.Queued,
            DownloadStatus.Downloading,
            DownloadStatus.Paused,
            DownloadStatus.Processing,
            DownloadStatus.ImportPending
        };

        private readonly IAudiobookRepository _audiobookRepository;
        private readonly IDownloadRepository _downloadRepository;
        private readonly TimeProvider _timeProvider;

        public CalendarService(
            IAudiobookRepository audiobookRepository,
            IDownloadRepository downloadRepository,
            TimeProvider timeProvider)
        {
            _audiobookRepository = audiobookRepository;
            _downloadRepository = downloadRepository;
            _timeProvider = timeProvider;
        }

        public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
            CalendarWindow window,
            bool includeUnmonitored,
            IReadOnlyCollection<string>? tags = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(window);

            // The download repository resolves its own DbContext from IDbContextFactory, so this
            // read is safe to start before the audiobook query rather than after it.
            var activeDownloadIdsTask =
                _downloadRepository.GetActiveAudiobookIdsAsync(ActiveCalendarDownloadStatuses);

            var rows = await _audiobookRepository.GetCalendarRowsAsync(
                window.CoarseLowerBound,
                window.CoarseUpperBound,
                includeUnmonitored,
                ct);

            var activeDownloadIds = (await activeDownloadIdsTask).ToHashSet();
            // Local, not UTC: the *arr calendars all window on DateTime.Today, and an operator east of
            // UTC would otherwise see today's releases fall out of a window they asked for.
            var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
            var tagFilter = NormalizeTags(tags);

            var events = new List<CalendarEvent>(rows.Count);
            foreach (var row in rows)
            {
                var releaseDate = CalendarWindow.ParsePublishedDate(row.PublishedDate);
                if (releaseDate is null || !window.Contains(releaseDate.Value))
                {
                    continue;
                }

                if (tagFilter.Count > 0 && !MatchesAnyTag(row.Tags, tagFilter))
                {
                    continue;
                }

                events.Add(new CalendarEvent
                {
                    AudiobookId = row.Id,
                    Title = row.Title,
                    Authors = row.Authors?.ToArray(),
                    Series = row.Series,
                    SeriesNumber = row.SeriesNumber,
                    Genres = row.Genres?.ToArray(),
                    Asin = row.Asin,
                    ImageUrl = row.ImageUrl,
                    Description = row.Description,
                    Runtime = row.Runtime,
                    ReleaseDate = releaseDate.Value,
                    Monitored = row.Monitored,
                    HasFile = row.HasFile,
                    Status = CalendarEventStatus.Compute(
                        activeDownloadIds.Contains(row.Id),
                        row.HasFile,
                        row.Monitored,
                        releaseDate.Value,
                        today)
                });
            }

            return events
                .OrderBy(calendarEvent => calendarEvent.ReleaseDate)
                .ThenBy(calendarEvent => calendarEvent.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(calendarEvent => calendarEvent.AudiobookId)
                .ToList();
        }

        private static HashSet<string> NormalizeTags(IReadOnlyCollection<string>? tags) =>
            tags is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : tags
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Select(tag => tag.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static bool MatchesAnyTag(List<string>? rowTags, HashSet<string> filter) =>
            rowTags is not null
            && rowTags.Any(tag => !string.IsNullOrWhiteSpace(tag) && filter.Contains(tag.Trim()));
    }
}
