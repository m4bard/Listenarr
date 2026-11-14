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
    public static DateTime NextNoResultAttempt(DateTime nowUtc, int noResultCount, TimeSpan cooldown) => nowUtc;

    public static DateTime NextPairFailureAttempt(DateTime nowUtc, int failureCount) => nowUtc;

    public static DateTime NextBookFailureAttempt(DateTime nowUtc, int bookFailureCount, TimeSpan cooldown) => nowUtc;
}
