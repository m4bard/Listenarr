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
using Listenarr.Application.Common.Scheduling;
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
            CancellationToken workerCancellation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(taskName);
            ArgumentNullException.ThrowIfNull(intervalProvider);
            ArgumentNullException.ThrowIfNull(runCycle);

            var handle = new ScheduledTaskHandle(
                taskName,
                intervalProvider,
                runCycle,
                workerCancellation,
                timeProvider,
                Deregister);

            _tasks.AddOrUpdate(
                taskName,
                handle,
                (_, replaced) =>
                {
                    logger.LogWarning(
                        "Two workers registered as {TaskName}; the newer registration wins",
                        taskName);
                    return handle;
                });

            logger.LogDebug("Registered scheduled task {TaskName}", taskName);
            return handle;
        }

        public IReadOnlyList<ScheduledTaskStatus> GetAll() =>
            _tasks.Values
                .Select(task => task.Snapshot())
                .OrderBy(task => task.TaskName, StringComparer.OrdinalIgnoreCase)
                .ToList();

        public ScheduledTaskStatus? Find(string taskName) =>
            _tasks.TryGetValue(taskName, out var handle) ? handle.Snapshot() : null;

        public ScheduledTaskTriggerResult Trigger(string taskName)
        {
            if (!_tasks.TryGetValue(taskName, out var handle))
            {
                return ScheduledTaskTriggerResult.NotFound;
            }

            if (!handle.TryBeginManualRun())
            {
                return ScheduledTaskTriggerResult.AlreadyRunning;
            }

            _ = Task.Run(() => RunManualAsync(handle));
            logger.LogInformation("Manually triggered scheduled task {TaskName}", taskName);
            return ScheduledTaskTriggerResult.Accepted;
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

        private void Deregister(ScheduledTaskHandle handle)
        {
            if (_tasks.TryRemove(new KeyValuePair<string, ScheduledTaskHandle>(handle.TaskName, handle)))
            {
                logger.LogDebug("Deregistered scheduled task {TaskName}", handle.TaskName);
            }
        }
    }
}
