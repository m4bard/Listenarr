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
        private volatile bool _disposed;
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
            ScheduledTaskManualTrigger manualTrigger,
            CancellationToken workerCancellation,
            TimeProvider timeProvider,
            Action<ScheduledTaskHandle> onDisposed)
        {
            TaskName = taskName;
            _intervalProvider = intervalProvider;
            _runCycle = runCycle;
            ManualTrigger = manualTrigger;
            _workerCancellation = workerCancellation;
            _timeProvider = timeProvider;
            _onDisposed = onDisposed;
            _registeredAt = timeProvider.GetUtcNow();
            _nextExecution = _registeredAt;
        }

        public string TaskName { get; }

        /// <summary>
        /// What the worker said about being run out of band. Read-only for the life of
        /// the registration: nothing can promote a task onto the allowlist at runtime.
        /// </summary>
        public ScheduledTaskManualTrigger ManualTrigger { get; }

        public async Task RunCycleAsync(ScheduledTaskTrigger trigger, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var startedAt = BeginCycle(trigger);
                await RunBodyAsync(startedAt, cancellationToken);
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
        /// Takes the exclusion gate without waiting and, on success, marks the manual
        /// cycle started. Null means a cycle is already in flight, or the worker has
        /// stopped, and the manual request should be refused rather than queued.
        /// </summary>
        /// <remarks>
        /// The state transition belongs here rather than in the cycle body because the
        /// moment the gate is taken is the moment the manual run is committed, and the
        /// body runs on a pool thread that the caller does not wait for. Doing it in the
        /// body meant the row handed back to the caller still described the previous
        /// cycle: not running, last triggered by the schedule.
        /// </remarks>
        public ScheduledTaskStatus? TryBeginManualRun()
        {
            if (_disposed || !_gate.Wait(0))
            {
                return null;
            }

            BeginCycle(ScheduledTaskTrigger.Manual);
            return Snapshot();
        }

        /// <summary>
        /// False once the worker's loop has ended. The row stays on the surface so a
        /// stopped worker reads as stopped rather than as a task that never existed.
        /// </summary>
        public bool IsRegistered => !_disposed;

        /// <summary>
        /// Whether a manual run may even be attempted. Checked before the gate, so a
        /// task that is not on the allowlist never takes the exclusion gate and cannot
        /// be told apart from an idle one by how long the refusal takes.
        /// </summary>
        public bool AllowsManualRun => ManualTrigger == ScheduledTaskManualTrigger.Allowed;

        /// <summary>
        /// Runs a manual cycle on a gate already taken by <see cref="TryBeginManualRun"/>,
        /// which has already recorded the start, bound to the worker's own cancellation
        /// so shutdown stops it.
        /// </summary>
        public async Task RunManualHeldAsync()
        {
            DateTimeOffset startedAt;
            lock (_state)
            {
                // The gate is held, so nothing else can have moved this on since
                // TryBeginManualRun set it.
                startedAt = _lastStartedAt ?? _timeProvider.GetUtcNow();
            }

            try
            {
                await RunBodyAsync(startedAt, _workerCancellation);
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
                    IsRegistered = !_disposed,
                    IsRunning = _isRunning,
                    ManualTrigger = ManualTrigger,
                    LastStartedAt = _lastStartedAt,
                    LastEndedAt = _lastEndedAt,
                    LastDuration = _lastDuration,
                    LastOutcome = _lastOutcome,
                    LastTrigger = _lastTrigger,
                    NextExecution = _nextExecution
                };
            }
        }

        /// <summary>
        /// Ends the registration. The row stays on the surface marked as no longer
        /// registered, because a worker that has stopped is the state an operator most
        /// needs to see, and removing the row reported it as a task that never existed.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _onDisposed(this);

            // The gate is deliberately not disposed. A manual run dispatched onto the
            // pool can still be holding it here, and its Release would then throw and be
            // logged as a failed cycle that did not fail. SemaphoreSlim needs disposing
            // only once AvailableWaitHandle has been taken, and nothing takes it.
        }

        /// <summary>
        /// Records that a cycle has started, on the gate already held by the caller.
        /// </summary>
        private DateTimeOffset BeginCycle(ScheduledTaskTrigger trigger)
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

            return startedAt;
        }

        private async Task RunBodyAsync(DateTimeOffset startedAt, CancellationToken cancellationToken)
        {
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
