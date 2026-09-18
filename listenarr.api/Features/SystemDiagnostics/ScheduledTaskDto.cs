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

using System.Text.Json.Serialization;
using Listenarr.Application.Common.Scheduling;

namespace Listenarr.Api.Features.SystemDiagnostics
{
    /// <summary>
    /// One row of the task surface: what the worker is, when it last ran, how long
    /// that took, how it ended, and when it is due again.
    /// </summary>
    public sealed class ScheduledTaskDto
    {
        /// <summary>Stable identifier, and the segment the manual trigger takes.</summary>
        public required string Name { get; init; }

        /// <summary>The same name split for display, as the other *arrs present it.</summary>
        public required string DisplayName { get; init; }

        /// <summary>
        /// The gap between cycles, as a whole number of seconds, or null when the
        /// worker's interval cannot be stated in that unit.
        /// </summary>
        /// <remarks>
        /// Whole seconds rather than a floating point count, so that the unit is a fact
        /// about the contract rather than an approximation: an interval this surface
        /// cannot state exactly is refused at the conversion and reported as null beside
        /// <see cref="IntervalError"/>, instead of being published as a number nobody
        /// declared. Nothing here can report 0 unless the worker really declared 0.
        /// <para>
        /// Null and not simply absent. Controllers serialize with
        /// <c>JsonIgnoreCondition.WhenWritingNull</c>
        /// (<c>Startup/ListenarrServiceRegistration.cs:44</c>), which would drop the key
        /// altogether, and the per-property <c>JsonIgnore(Never)</c> overrides that so the
        /// key set does not depend on the value. A stable key set is what makes
        /// <c>required</c> mean anything in the generated schema: a client generated from
        /// it sees a property that is required and nullable, and its own compiler makes it
        /// handle the null.
        /// </para>
        /// <para>
        /// What this does not buy, because measuring said otherwise: it is not a runtime
        /// trap that fires by itself. Only System.Text.Json is measured, where a null into
        /// a non-nullable numeric throws and an absent one reads as 0. In JavaScript, which
        /// is the consumer this surface will actually get, <c>null</c> and <c>0</c> are
        /// indistinguishable under <c>&gt; 0</c>, <c>??</c> and arithmetic, so a page has
        /// to read <see cref="IntervalError"/> or test for null on purpose. Jackson and Go
        /// are unmeasured and nothing here claims anything about them.
        /// </para>
        /// <para>
        /// The family sends this as <c>Interval</c>, an int in minutes
        /// (<c>Sonarr.Api.V3/System/Tasks/TaskResource.cs:10</c>, where 0 also carries a
        /// meaning: <c>NzbDrone.Core/Jobs/TaskManager.cs:49</c> treats a task with
        /// <c>Interval</c> 0 as never due, and the family's own fastest declared task is
        /// <c>Interval = 1</c>, so a minute is its floor by design). Listenarr cannot use
        /// that shape as it stands, because four of the ten workers on this surface run
        /// faster than once a minute:
        /// <list type="bullet">
        /// <item><c>DirectDownloadService</c>, 10 seconds, hardcoded
        /// (<c>Downloads/DirectDownload/DirectDownloadService.cs:25</c>)</item>
        /// <item><c>move.scan.handoff.recovery</c>, 30 seconds, hardcoded
        /// (<c>Library/Scanning/ScanBackgroundService.cs:42</c>)</item>
        /// <item><c>MovedDownloadCleanupService</c>, 10 seconds by default
        /// (<c>Downloads/Cleanup/MovedDownloadCleanupBackgroundService.cs:16</c>)</item>
        /// <item><c>DownloadMonitorService</c>, 30 seconds by default, from configuration
        /// (<c>Downloads/Monitoring/DownloadMonitorService.cs:37</c> and <c>:65</c>)</item>
        /// </list>
        /// Minutes-as-int would round all four to 0, which in the family's own reading
        /// means "never runs", and two of them unconditionally rather than only on default
        /// configuration. Seconds holds all four exactly, so no rounding can lose one. The
        /// field is named for its unit so nothing reads it as minutes by mistake. It does
        /// not close the family's own hazard: a client generated against <c>Interval</c>
        /// still reads nothing here whatever the type, because the name differs. That is
        /// the field-naming question, and it is not this field's to answer.
        /// </para>
        /// <para>
        /// The forced key is for this field and not for the other nullable numerics on the
        /// row. <see cref="LastDurationSeconds"/> is omitted when null and should be: a
        /// duration of 0 is merely a wrong number, where an interval of 0 is a wrong
        /// meaning, because the family reads it as a task that is never due. Only a zero
        /// that carries a meaning is worth a key that never moves.
        /// </para>
        /// </remarks>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public required long? IntervalSeconds { get; init; }

        /// <summary>
        /// Why <see cref="IntervalSeconds"/> is null, on the one row where it is, and
        /// absent everywhere else.
        /// </summary>
        /// <remarks>
        /// The refusal travels on the row rather than in the status code, so that one
        /// worker nobody can describe does not withhold the nine that can be.
        /// <c>ScheduledTaskHandle.ReadInterval</c> declines the same trade one layer down,
        /// where a worker whose interval delegate throws falls back to its last known
        /// value rather than failing the whole list. Only the refusal to fail the list is
        /// shared: the handle has a last good number to print and this does not, which is
        /// why one publishes a value and the other publishes null.
        /// <para>
        /// The refusal is advisory. It does not set a status code, and the registry logs a
        /// warning at registration so that an operator who never calls this API still
        /// hears about it.
        /// </para>
        /// </remarks>
        public string? IntervalError { get; init; }

        public required DateTimeOffset RegisteredAt { get; init; }

        /// <summary>
        /// Whether the worker's loop is still running. A worker that has stopped keeps
        /// its row, with this false and its last outcome intact, rather than dropping off
        /// the list: a monitoring surface on which failure is the one invisible state is
        /// worse than no surface.
        /// </summary>
        public required bool IsRegistered { get; init; }

        public required bool IsRunning { get; init; }

        /// <summary>
        /// Whether POST to this task's run route will be accepted. Present so the whole
        /// manual-run allowlist can be read off one GET, and so a UI can render the
        /// button disabled rather than discovering the refusal by pressing it.
        /// </summary>
        public required bool IsManualRunAllowed { get; init; }

        public DateTimeOffset? LastStartedAt { get; init; }

        public DateTimeOffset? LastEndedAt { get; init; }

        public double? LastDurationSeconds { get; init; }

        public required string LastOutcome { get; init; }

        public string? LastTrigger { get; init; }

        public DateTimeOffset? NextExecution { get; init; }

        public static ScheduledTaskDto FromStatus(ScheduledTaskStatus status)
        {
            // One decision, read once. Asking twice would let the number and the
            // explanation of its absence disagree with each other.
            var intervalSeconds = ScheduledTaskInterval.ToWholeSeconds(status.Interval);

            return new ScheduledTaskDto
            {
                Name = status.TaskName,
                DisplayName = SplitName(status.TaskName),
                IntervalSeconds = intervalSeconds,
                IntervalError = intervalSeconds is null
                    ? ScheduledTaskInterval.DescribeRefusal(status.TaskName, status.Interval)
                    : null,
                RegisteredAt = status.RegisteredAt,
                IsRegistered = status.IsRegistered,
                IsRunning = status.IsRunning,
                IsManualRunAllowed = status.ManualTrigger == ScheduledTaskManualTrigger.Allowed,
                LastStartedAt = status.LastStartedAt,
                LastEndedAt = status.LastEndedAt,
                LastDurationSeconds = status.LastDuration?.TotalSeconds,
                LastOutcome = status.LastOutcome.ToString(),
                LastTrigger = status.LastTrigger?.ToString(),
                NextExecution = status.NextExecution
            };
        }

        /// <summary>
        /// Worker names arrive either as a type name or as a dotted identifier, so
        /// both are turned into spaced words rather than only camel case being split.
        /// </summary>
        private static string SplitName(string taskName)
        {
            var words = new List<string>();
            var word = new System.Text.StringBuilder();

            foreach (var character in taskName)
            {
                if (character is '.' or '_' or '-' or ' ')
                {
                    Flush(words, word);
                    continue;
                }

                if (char.IsUpper(character) && word.Length > 0 && !char.IsUpper(word[^1]))
                {
                    Flush(words, word);
                }

                word.Append(character);
            }

            Flush(words, word);
            return words.Count == 0 ? taskName : string.Join(' ', words);
        }

        private static void Flush(List<string> words, System.Text.StringBuilder word)
        {
            if (word.Length == 0)
            {
                return;
            }

            words.Add(char.ToUpperInvariant(word[0]) + word.ToString(1, word.Length - 1));
            word.Clear();
        }
    }
}
