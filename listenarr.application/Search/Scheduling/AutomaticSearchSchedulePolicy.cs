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

namespace Listenarr.Application.Search.Scheduling;

/// <summary>
/// The wait ladders of the automatic search. Each takes an explicit <c>nowUtc</c> and the count
/// after the event being recorded (the first failure is 1), and never throws on an out-of-range count.
/// </summary>
public static class AutomaticSearchSchedulePolicy
{
    /// <summary>
    /// After a Completed search found nothing acceptable: the cooldown, doubling with each
    /// consecutive no-result up to the larger of the cooldown and a week.
    /// </summary>
    public static DateTime NextNoResultAttempt(DateTime nowUtc, int noResultCount, TimeSpan cooldown) =>
        Add(nowUtc, Ladder(
            cooldown,
            noResultCount,
            Max(cooldown, TimeSpan.FromHours(AutomaticSearchBounds.NoResultMaxIntervalHours))));

    /// <summary>
    /// After an indexer failed for a book: an hour, doubling with each consecutive failure up to a day.
    /// </summary>
    public static DateTime NextPairFailureAttempt(DateTime nowUtc, int failureCount) =>
        Add(nowUtc, Ladder(
            TimeSpan.FromMinutes(AutomaticSearchBounds.PairFailureBaseMinutes),
            failureCount,
            TimeSpan.FromHours(AutomaticSearchBounds.PairFailureMaxIntervalHours)));

    /// <summary>
    /// After a book-level failure or a grab that did not complete: a quarter hour, doubling with
    /// each consecutive failure up to the cooldown.
    /// </summary>
    public static DateTime NextBookFailureAttempt(DateTime nowUtc, int bookFailureCount, TimeSpan cooldown) =>
        Add(nowUtc, Ladder(
            TimeSpan.FromMinutes(AutomaticSearchBounds.BookFailureBaseMinutes),
            bookFailureCount,
            cooldown));

    /// <summary>
    /// <paramref name="firstDelay"/> doubled <paramref name="count"/> - 1 times, never above
    /// <paramref name="cap"/>. A count below 1 is the first step. Each doubling is checked against
    /// half the cap first, so no count can overflow.
    /// </summary>
    private static TimeSpan Ladder(TimeSpan firstDelay, int count, TimeSpan cap)
    {
        var delay = firstDelay < cap ? firstDelay : cap;
        for (var step = 1; step < count && delay < cap; step++)
        {
            delay = delay > cap / 2 ? cap : delay * 2;
        }

        return delay;
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

    private static DateTime Add(DateTime nowUtc, TimeSpan delay) =>
        delay.Ticks > DateTime.MaxValue.Ticks - nowUtc.Ticks
            ? new DateTime(DateTime.MaxValue.Ticks, nowUtc.Kind)
            : nowUtc.Add(delay);
}
