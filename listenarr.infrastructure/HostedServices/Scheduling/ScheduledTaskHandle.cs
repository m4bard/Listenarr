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

using Listenarr.Application.Common.Scheduling;

namespace Listenarr.Infrastructure.HostedServices.Scheduling
{
    /// <summary>
    /// Tracks one registered worker: its exclusion gate, and the timing and outcome
    /// of its most recent cycle. The cycle runner owns the instance for the life of
    /// the worker loop; the registry hands it to manual triggers.
    /// </summary>
    internal sealed class ScheduledTaskHandle : IScheduledTaskHandle
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly object _state = new();
        private readonly Func<TimeSpan> _intervalProvider;
        private readonly Func<CancellationToken, Task> _runCycle;
        private readonly CancellationToken _workerCancellation;
        private readonly TimeProvider _timeProvider;
        private readonly Action<ScheduledTaskHandle> _onDisposed;
        private readonly DateTimeOffset _registeredAt;

        private bool _isRunning;
        private bool _disposed;
        private DateTimeOffset? _lastStartedAt;
        private DateTimeOffset? _lastEndedAt;
        private TimeSpan? _lastDuration;
        private DateTimeOffset? _nextExecution;
        private ScheduledTaskOutcome _lastOutcome = ScheduledTaskOutcome.Unknown;
        private ScheduledTaskTrigger? _lastTrigger;

        public ScheduledTaskHandle(
            string taskName,
            Func<TimeSpan> intervalProvider,
            Func<CancellationToken, Task> runCycle,
            CancellationToken workerCancellation,
            TimeProvider timeProvider,
            Action<ScheduledTaskHandle> onDisposed)
        {
            TaskName = taskName;
            _intervalProvider = intervalProvider;
            _runCycle = runCycle;
            _workerCancellation = workerCancellation;
            _timeProvider = timeProvider;
            _onDisposed = onDisposed;
            _registeredAt = timeProvider.GetUtcNow();
            _nextExecution = _registeredAt;
        }

        public string TaskName { get; }

        public async Task RunCycleAsync(ScheduledTaskTrigger trigger, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                await RunHeldAsync(trigger, cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }

        public void RecordNextExecution(DateTimeOffset nextExecution)
        {
            lock (_state)
            {
                _nextExecution = nextExecution;
            }
        }

        /// <summary>
        /// Takes the exclusion gate without waiting. False means a cycle is already
        /// in flight and the manual request should be refused rather than queued.
        /// </summary>
        public bool TryBeginManualRun() => !_disposed && _gate.Wait(0);

        /// <summary>
        /// Runs a manual cycle on a gate already taken by <see cref="TryBeginManualRun"/>,
        /// bound to the worker's own cancellation so shutdown stops it.
        /// </summary>
        public async Task RunManualHeldAsync()
        {
            try
            {
                await RunHeldAsync(ScheduledTaskTrigger.Manual, _workerCancellation);
            }
            finally
            {
                _gate.Release();
            }
        }

        public ScheduledTaskStatus Snapshot()
        {
            var interval = _intervalProvider();
            lock (_state)
            {
                return new ScheduledTaskStatus
                {
                    TaskName = TaskName,
                    Interval = interval,
                    RegisteredAt = _registeredAt,
                    IsRunning = _isRunning,
                    LastStartedAt = _lastStartedAt,
                    LastEndedAt = _lastEndedAt,
                    LastDuration = _lastDuration,
                    LastOutcome = _lastOutcome,
                    LastTrigger = _lastTrigger,
                    NextExecution = _nextExecution
                };
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _onDisposed(this);
            _gate.Dispose();
        }

        private async Task RunHeldAsync(ScheduledTaskTrigger trigger, CancellationToken cancellationToken)
        {
            var startedAt = _timeProvider.GetUtcNow();
            lock (_state)
            {
                _isRunning = true;
                _lastStartedAt = startedAt;
                _lastTrigger = trigger;

                // A scheduled cycle consumes the deadline it was waiting for; the
                // runner publishes the next one when this cycle ends. A manual run
                // happens beside that wait, so it leaves the deadline alone.
                if (trigger == ScheduledTaskTrigger.Scheduled)
                {
                    _nextExecution = null;
                }
            }

            var outcome = ScheduledTaskOutcome.Unknown;
            try
            {
                await _runCycle(cancellationToken);
                outcome = ScheduledTaskOutcome.Succeeded;
            }
            catch (OperationCanceledException)
            {
                // A cycle stopped by shutdown is not a failure; one stopped by its
                // own timeout is, which is the split the cycle runner's metrics make.
                outcome = cancellationToken.IsCancellationRequested
                    ? ScheduledTaskOutcome.Canceled
                    : ScheduledTaskOutcome.Failed;
                throw;
            }
            catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
            {
                outcome = ScheduledTaskOutcome.Failed;
                throw;
            }
            finally
            {
                var endedAt = _timeProvider.GetUtcNow();
                lock (_state)
                {
                    _isRunning = false;
                    _lastEndedAt = endedAt;
                    _lastDuration = endedAt - startedAt;
                    _lastOutcome = outcome;
                }
            }
        }
    }
}
