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

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.HostedServices.Scheduling
{
    /// <summary>
    /// Holds the periodic workers that are currently running. Nothing registers
    /// itself here: <c>WorkerCycleRunner</c> announces every worker it is asked to
    /// drive, so the list is whatever the host actually started.
    /// </summary>
    public sealed class ScheduledTaskRegistry(
        TimeProvider timeProvider,
        ILogger<ScheduledTaskRegistry> logger) : IScheduledTaskRegistry
    {
        private readonly ConcurrentDictionary<string, ScheduledTaskHandle> _tasks =
            new(StringComparer.OrdinalIgnoreCase);

        public IScheduledTaskHandle Register(
            string taskName,
            Func<TimeSpan> intervalProvider,
            Func<CancellationToken, Task> runCycle,
            ScheduledTaskManualTrigger manualTrigger,
            CancellationToken workerCancellation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(taskName);
            ArgumentNullException.ThrowIfNull(intervalProvider);
            ArgumentNullException.ThrowIfNull(runCycle);

            var handle = new ScheduledTaskHandle(
                taskName,
                intervalProvider,
                runCycle,
                manualTrigger,
                workerCancellation,
                timeProvider,
                Deregister);

            _tasks.AddOrUpdate(
                taskName,
                handle,
                (_, replaced) =>
                {
                    // A stopped worker keeps its row, so finding one here is the ordinary
                    // restart case and not a name collision. Only a live registration
                    // losing its name is worth warning about.
                    if (replaced.IsRegistered)
                    {
                        logger.LogWarning(
                            "Two workers registered as {TaskName}; the newer registration wins",
                            taskName);
                    }

                    return handle;
                });

            logger.LogDebug(
                "Registered scheduled task {TaskName} (manual run {ManualTrigger})",
                taskName,
                manualTrigger);
            return handle;
        }

        public IReadOnlyList<ScheduledTaskStatus> GetAll() =>
            _tasks.Values
                .Select(task => task.Snapshot())
                .OrderBy(task => task.TaskName, StringComparer.OrdinalIgnoreCase)
                .ToList();

        public ScheduledTaskStatus? Find(string taskName) =>
            _tasks.TryGetValue(taskName, out var handle) ? handle.Snapshot() : null;

        public ScheduledTaskTriggerOutcome Trigger(string taskName)
        {
            // A stopped worker keeps its row on the read surface, but there is no loop
            // left to bring a cycle forward on, so a trigger against one is answered the
            // same way as a name nobody ever registered.
            if (!_tasks.TryGetValue(taskName, out var handle) || !handle.IsRegistered)
            {
                return ScheduledTaskTriggerOutcome.NotFound;
            }

            // The allowlist is checked before the gate, so a scheduled-only worker is
            // refused without any part of its cycle being reached.
            if (!handle.AllowsManualRun)
            {
                logger.LogWarning(
                    "Refused a manual run of {TaskName}: it is not on the manual-run allowlist",
                    taskName);
                return ScheduledTaskTriggerOutcome.For(
                    ScheduledTaskTriggerResult.NotAllowed,
                    handle.Snapshot());
            }

            if (handle.TryBeginManualRun() is not { } started)
            {
                return ScheduledTaskTriggerOutcome.For(
                    ScheduledTaskTriggerResult.AlreadyRunning,
                    handle.Snapshot());
            }

            _ = Task.Run(() => RunManualAsync(handle));
            logger.LogInformation("Manually triggered scheduled task {TaskName}", taskName);
            return ScheduledTaskTriggerOutcome.For(ScheduledTaskTriggerResult.Accepted, started);
        }

        private async Task RunManualAsync(ScheduledTaskHandle handle)
        {
            try
            {
                await handle.RunManualHeldAsync();
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation(
                    "Manual run of {TaskName} stopped because the worker is shutting down",
                    handle.TaskName);
            }
            catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
            {
                logger.LogError(exception, "Manual run of {TaskName} failed", handle.TaskName);
            }
        }

        /// <summary>
        /// Ends a registration without taking the row off the surface.
        /// </summary>
        /// <remarks>
        /// The handle itself reports that it has stopped, so nothing has to be moved or
        /// copied here; what matters is that a handle whose name has since been taken
        /// over by a newer registration does not disturb the live one. The reference
        /// check is what guarantees that, and it is the reason the dictionary is keyed on
        /// name rather than on the handle.
        /// </remarks>
        private void Deregister(ScheduledTaskHandle handle)
        {
            if (_tasks.TryGetValue(handle.TaskName, out var current) && ReferenceEquals(current, handle))
            {
                logger.LogDebug(
                    "Scheduled task {TaskName} stopped; its last cycle stays on the surface",
                    handle.TaskName);
            }
        }
    }
}
