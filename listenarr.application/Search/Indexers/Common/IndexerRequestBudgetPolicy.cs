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

namespace Listenarr.Application.Search.Indexers.Common;

/// <summary>
/// The arithmetic of the per-indexer rolling daily request allowance, with no clock and no
/// storage: ceilings by origin, the hourly bucket window, and when a refused request may next
/// be admitted.
/// </summary>
public static class IndexerRequestBudgetPolicy
{
    public const int MinInteractiveReservePercent = 0;
    public const int MaxInteractiveReservePercent = 90;

    // A request counts while its hour bucket starts no more than 24 hours before the current
    // hour, so the window spans 24 to 25 hours. That can over-count against an indexer's own
    // 24-hour or calendar-day window, never under-count.
    private static readonly TimeSpan CountedHistory = TimeSpan.FromHours(24);

    // Rows older than the window plus a 24 hour margin against clock jumps are dead weight.
    private static readonly TimeSpan RetainedHistory = TimeSpan.FromHours(48);

    /// <summary>
    /// The most requests <paramref name="origin"/> may have in the window: the whole limit for
    /// an interactive request, the limit less the reserve (rounded down) for anything else.
    /// </summary>
    public static int CeilingFor(int dailyLimit, SearchOrigin origin, int interactiveReservePercent)
    {
        if (origin == SearchOrigin.Interactive)
            return dailyLimit;

        var reserve = Math.Clamp(interactiveReservePercent, MinInteractiveReservePercent, MaxInteractiveReservePercent);
        return (int)((long)dailyLimit * (100 - reserve) / 100);
    }

    public static DateTime HourStartUtc(DateTimeOffset now)
    {
        var utc = now.UtcDateTime;
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
    }

    public static DateTime WindowStartUtc(DateTime hour) => hour - CountedHistory;

    public static DateTime PruneBeforeUtc(DateTime hour) => hour - RetainedHistory;

    /// <summary>
    /// The earliest hour at which a request refused at <paramref name="hour"/> could be
    /// admitted, assuming nothing else is spent. Buckets leave the window oldest first, and a
    /// bucket starting at b is out of the window from b + 25 hours, so the answer is that
    /// moment for the first bucket whose removal brings the count below
    /// <paramref name="ceiling"/>. A count already below the ceiling means the refusal came from
    /// a race and the next hour is soon enough. A ceiling of zero or less can never be met, so
    /// the request waits a full window.
    /// </summary>
    public static DateTime NextAdmissionUtc(
        IEnumerable<(DateTime HourStartUtc, int Count)> buckets,
        int ceiling,
        DateTime hour)
    {
        var fullWindow = hour + CountedHistory + TimeSpan.FromHours(1);
        if (ceiling <= 0)
            return fullWindow;

        var windowStart = WindowStartUtc(hour);
        var inWindow = buckets
            .Where(bucket => bucket.HourStartUtc >= windowStart && bucket.Count > 0)
            .OrderBy(bucket => bucket.HourStartUtc)
            .ToList();

        var remaining = inWindow.Sum(bucket => (long)bucket.Count);
        if (remaining < ceiling)
            return hour + TimeSpan.FromHours(1);

        foreach (var bucket in inWindow)
        {
            remaining -= bucket.Count;
            if (remaining < ceiling)
                return DateTime.SpecifyKind(bucket.HourStartUtc + CountedHistory + TimeSpan.FromHours(1), DateTimeKind.Utc);
        }

        return fullWindow;
    }
}
