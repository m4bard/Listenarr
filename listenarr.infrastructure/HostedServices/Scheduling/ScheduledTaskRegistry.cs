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

            // Q12's "loudly". Without this the refusal exists only on the API row, so a
            // worker nobody can describe is invisible to an operator who never calls the
            // task surface. Logged once per registration rather than per read, which is
            // also why it cannot catch a provider that starts whole and later returns a
            // fraction; the row still reports that case.
            if (!ScheduledTaskInterval.CanBeStated(handle.RegisteredInterval))
            {
                logger.LogWarning(
                    "Scheduled task {TaskName} registered with an interval of {Interval}, which "
                        + "cannot be stated in whole seconds; its row will report no interval "
                        + "rather than a rounded one",
                    taskName,
                    handle.RegisteredInterval);
            }

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
            if (!_tasks.TryGetValue(taskName, out var handle))
            {
                return ScheduledTaskTriggerOutcome.NotFound;
            }

            // A stopped worker keeps its row on the read surface, and answering "no such
            // task" for something the same API lists would contradict the reasoning that
            // gives a scheduled-only worker a refusal rather than a 404.
            if (!handle.IsRegistered)
            {
                logger.LogWarning(
                    "Refused a manual run of {TaskName}: its worker has stopped",
                    taskName);
                return ScheduledTaskTriggerOutcome.For(
                    ScheduledTaskTriggerResult.WorkerStopped,
                    handle.Snapshot());
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

            // The attempt carries its own row, taken in the same critical section as the
            // gate, so a refusal cannot report a cycle that has since finished.
            var attempt = handle.TryBeginManualRun();
            if (!attempt.Started)
            {
                return ScheduledTaskTriggerOutcome.For(
                    ScheduledTaskTriggerResult.AlreadyRunning,
                    attempt.Status);
            }

            _ = Task.Run(() => RunManualAsync(handle));
            logger.LogInformation("Manually triggered scheduled task {TaskName}", taskName);
            return ScheduledTaskTriggerOutcome.For(
                ScheduledTaskTriggerResult.Accepted,
                attempt.Status);
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
        /// The handle reports its own stopped state, so there is nothing to move or copy
        /// and this method mutates nothing: the reference check only decides whether the
        /// log line is about the row that is actually on the surface. The property that
        /// matters, a stale handle not disturbing a live one that took over its name, is
        /// enforced by <see cref="Register"/>'s <c>AddOrUpdate</c>, not here. Change that
        /// and this log line goes quiet; change this and nothing breaks.
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
