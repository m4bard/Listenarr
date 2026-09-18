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

namespace Listenarr.Application.Common.Scheduling
{
    /// <summary>
    /// The unit a worker's interval is published in, and the one place that decides
    /// whether a given interval can be stated in it.
    /// </summary>
    /// <remarks>
    /// Whole, non-negative seconds. Seconds because that is what the workers use and four
    /// of them cycle faster than once a minute; whole because an integer is the only shape
    /// in which "this cannot be stated" is an event at all, and a value nobody declared is
    /// worse than no value.
    /// <para>
    /// It lives in the application layer rather than beside the API resource because two
    /// layers need the same answer: the resource, to build a row, and the registry, to
    /// warn at registration time so that a worker nobody can describe is audible to an
    /// operator who never calls the API.
    /// </para>
    /// </remarks>
    public static class ScheduledTaskInterval
    {
        /// <summary>
        /// Whether the interval is a whole, non-negative number of seconds.
        /// </summary>
        /// <remarks>
        /// Ticks rather than <c>TotalSeconds</c> so the test for exactness is exact:
        /// asking whether a double is a whole number reintroduces the rounding the unit
        /// exists to avoid.
        /// </remarks>
        public static bool CanBeStated(TimeSpan interval) =>
            interval >= TimeSpan.Zero && interval.Ticks % TimeSpan.TicksPerSecond == 0;

        /// <summary>
        /// The interval in whole seconds, or null if it cannot be stated in them.
        /// </summary>
        /// <remarks>
        /// <c>long</c> because <c>TimeSpan.MaxValue</c> is 922,337,203,685 seconds, which
        /// overflows an <c>int</c> and sits far below 2^53, so a browser parsing the
        /// number keeps it exact.
        /// </remarks>
        public static long? ToWholeSeconds(TimeSpan interval) =>
            CanBeStated(interval) ? interval.Ticks / TimeSpan.TicksPerSecond : null;

        /// <summary>
        /// Why an interval cannot be stated, naming the task, the published field, the
        /// value and the accepted form. Null when there is nothing to refuse.
        /// </summary>
        /// <remarks>
        /// The two reasons are separate sentences because they are separate reasons. A
        /// fraction is refused because truncating it reports a schedule the worker did not
        /// declare, and for anything under a second that schedule is 0. A negative
        /// interval is refused because a negative gap between cycles is not a schedule at
        /// all, which has nothing to do with truncation: truncating -10 seconds lands on
        /// -10, not on 0.
        /// <para>
        /// The field name is spelled here, in the layer that owns the unit, so that the
        /// row and any log about it say the same thing rather than drifting apart.
        /// </para>
        /// </remarks>
        public static string? DescribeRefusal(string taskName, TimeSpan interval)
        {
            if (CanBeStated(interval))
            {
                return null;
            }

            var declared = interval.TotalSeconds.ToString(
                "0.###############",
                CultureInfo.InvariantCulture);

            if (interval < TimeSpan.Zero)
            {
                return $"'{taskName}' declares an interval of {declared}s, and " +
                    "'intervalSeconds' carries whole, non-negative seconds. A negative gap " +
                    "between cycles is not a schedule, so nothing is reported for it.";
            }

            var truncated = (interval.Ticks / TimeSpan.TicksPerSecond)
                .ToString(CultureInfo.InvariantCulture);

            return $"'{taskName}' declares an interval of {declared}s, and 'intervalSeconds' " +
                $"carries whole, non-negative seconds. Nothing is reported rather than " +
                $"truncating to {truncated}, because truncation is how a sub-second worker " +
                $"comes to report 0, and 0 on this surface reads as a task that never runs.";
        }
    }
}
