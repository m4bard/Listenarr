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

namespace Listenarr.Api.Features.SystemDiagnostics
{
    /// <summary>
    /// Raised when a registered worker's interval cannot be stated in the unit the task
    /// surface publishes it in, so that the value is refused instead of being rounded
    /// into something that reads as a schedule.
    /// </summary>
    /// <remarks>
    /// The surface reports <c>intervalSeconds</c> as a whole number, because a whole
    /// number is the only shape in which "this cannot be represented" is an event at all:
    /// a floating point field silently absorbs any value, including the ones a consumer
    /// then truncates to 0 on its own side. Having committed to whole seconds, the
    /// conversion has a boundary, and this is what happens at it.
    /// <para>
    /// The value that must never be invented is 0. Every scheduling surface in this
    /// family reads a zero interval as a task that is never due
    /// (<c>NzbDrone.Core/Jobs/TaskManager.cs:49</c> filters on <c>Interval &gt; 0</c>),
    /// and four of the workers on this surface run faster than once a minute, so a unit
    /// that cannot hold them reports them as dead rather than as fast. Seconds holds all
    /// four exactly; anything finer is refused here rather than quietly flattened.
    /// </para>
    /// </remarks>
    public sealed class ScheduledTaskIntervalFormatException : Exception
    {
        public ScheduledTaskIntervalFormatException(string taskName, TimeSpan interval)
            : base(BuildMessage(taskName, interval))
        {
            TaskName = taskName;
            Interval = interval;
        }

        /// <summary>The worker whose interval could not be stated.</summary>
        public string TaskName { get; }

        /// <summary>The interval as the worker declared it.</summary>
        public TimeSpan Interval { get; }

        private static string BuildMessage(string taskName, TimeSpan interval)
        {
            var declared = interval.TotalSeconds.ToString("0.###############", CultureInfo.InvariantCulture);
            var truncated = (interval.Ticks / TimeSpan.TicksPerSecond)
                .ToString(CultureInfo.InvariantCulture);

            return $"'{taskName}' declares an interval of {declared}s, and 'intervalSeconds' " +
                $"carries whole, non-negative seconds only. It is refused rather than " +
                $"truncated to {truncated}, because truncation is how a sub-second worker " +
                $"comes to report 0, and 0 on this surface reads as a task that never runs.";
        }
    }
}
