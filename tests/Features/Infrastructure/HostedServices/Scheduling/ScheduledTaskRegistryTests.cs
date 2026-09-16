using Listenarr.Application.Common.Scheduling;
using Listenarr.Infrastructure.HostedServices;
using Listenarr.Infrastructure.HostedServices.Scheduling;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Scheduling;

[Trait("Name", "ScheduledTaskRegistryTests")]
[Trait("Category", "BackgroundWorkers")]
public sealed class ScheduledTaskRegistryTests : BaseTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Worker_IsDiscoveredWithoutRegisteringItself()
    {
        var registry = CreateRegistry();
        using var worker = new FakeWorker(registry, "DiscoveredWorker");

        await worker.WaitForCycleAsync(1);

        var listed = registry.GetAll();
        Assert.Equal(new[] { "DiscoveredWorker" }, listed.Select(task => task.TaskName));
        await worker.StopAsync();
    }

    [Fact]
    public async Task SucceededCycle_RecordsTimingOutcomeAndNextExecution()
    {
        var registry = CreateRegistry();
        using var worker = new FakeWorker(registry, "SucceedingWorker");

        await worker.WaitForCycleAsync(1);
        var status = await WaitForIdleAsync(registry, "SucceedingWorker");

        Assert.Equal(ScheduledTaskOutcome.Succeeded, status.LastOutcome);
        Assert.Equal(ScheduledTaskTrigger.Scheduled, status.LastTrigger);
        Assert.NotNull(status.LastStartedAt);
        Assert.NotNull(status.LastEndedAt);
        Assert.NotNull(status.LastDuration);
        Assert.Equal(Interval, status.Interval);
        Assert.NotNull(status.NextExecution);
        Assert.True(
            status.NextExecution >= status.LastEndedAt,
            "Next execution must be scheduled after the cycle that preceded it.");
        await worker.StopAsync();
    }

    [Fact]
    public async Task FailedCycle_IsRecordedAsFailedRatherThanHidden()
    {
        var registry = CreateRegistry();
        using var worker = new FakeWorker(
            registry,
            "FailingWorker",
            failure: () => new InvalidOperationException("cycle blew up"));

        await worker.WaitForCycleAsync(1);
        var status = await WaitForIdleAsync(registry, "FailingWorker");

        Assert.Equal(ScheduledTaskOutcome.Failed, status.LastOutcome);
        Assert.NotNull(status.LastEndedAt);
        await worker.StopAsync();
    }

    [Fact]
    public void Trigger_UnknownTask_IsNotFound()
    {
        var registry = CreateRegistry();

        Assert.Equal(ScheduledTaskTriggerResult.NotFound, registry.Trigger("NoSuchWorker"));
    }

    [Fact]
    public async Task Trigger_IdleTask_RunsAnExtraCycleAndRecordsTheManualTrigger()
    {
        var registry = CreateRegistry();
        using var worker = new FakeWorker(registry, "TriggerableWorker");

        await worker.WaitForCycleAsync(1);
        await WaitForIdleAsync(registry, "TriggerableWorker");

        Assert.Equal(ScheduledTaskTriggerResult.Accepted, registry.Trigger("TriggerableWorker"));

        await worker.WaitForCycleAsync(2);
        var status = await WaitForIdleAsync(registry, "TriggerableWorker");

        Assert.Equal(ScheduledTaskTrigger.Manual, status.LastTrigger);
        Assert.Equal(ScheduledTaskOutcome.Succeeded, status.LastOutcome);
        await worker.StopAsync();
    }

    [Fact]
    public async Task Trigger_TaskAlreadyRunning_IsRefusedInsteadOfOverlapping()
    {
        var registry = CreateRegistry();
        using var gate = new SemaphoreSlim(0, 1);
        using var worker = new FakeWorker(registry, "BusyWorker", holdCycleOn: gate);

        await worker.WaitForCycleAsync(1);

        var refused = registry.Trigger("BusyWorker");
        gate.Release();

        Assert.Equal(ScheduledTaskTriggerResult.AlreadyRunning, refused);
        await worker.StopAsync();
    }

    [Fact]
    public async Task StoppedWorker_LeavesTheSurface()
    {
        var registry = CreateRegistry();
        using var worker = new FakeWorker(registry, "StoppingWorker");

        await worker.WaitForCycleAsync(1);
        await worker.StopAsync();

        Assert.Empty(registry.GetAll());
    }

    private static ScheduledTaskRegistry CreateRegistry() =>
        new(TimeProvider.System, Mock.Of<ILogger<ScheduledTaskRegistry>>());

    private static async Task<ScheduledTaskStatus> WaitForIdleAsync(
        IScheduledTaskRegistry registry,
        string taskName)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;
        ScheduledTaskStatus? status;

        do
        {
            status = registry.Find(taskName);
            if (status is { IsRunning: false, LastEndedAt: not null, NextExecution: not null })
            {
                return status;
            }

            await Task.Delay(10);
        }
        while (DateTimeOffset.UtcNow < deadline);

        Assert.Fail($"'{taskName}' never came to rest: {status?.LastOutcome.ToString() ?? "not registered"}");
        throw new InvalidOperationException("unreachable");
    }

    /// <summary>
    /// A worker driven by the real cycle runner, so the tests exercise the same
    /// path the eighteen background services take rather than the registry alone.
    /// </summary>
    private sealed class FakeWorker : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task _loop;
        private readonly List<TaskCompletionSource> _cycleWaiters = [];
        private readonly object _cycleLock = new();
        private readonly Func<Exception>? _failure;
        private readonly SemaphoreSlim? _holdCycleOn;
        private int _cycleCount;

        public FakeWorker(
            IScheduledTaskRegistry registry,
            string workerName,
            Func<Exception>? failure = null,
            SemaphoreSlim? holdCycleOn = null)
        {
            _failure = failure;
            _holdCycleOn = holdCycleOn;

            var runner = new WorkerCycleRunner(
                TimeProvider.System,
                Mock.Of<IAppMetricsService>(),
                registry,
                Mock.Of<ILogger<WorkerCycleRunner>>());

            _loop = runner.RunPeriodicAsync(
                workerName,
                initialDelay: null,
                intervalProvider: () => Interval,
                runCycle: RunCycleAsync,
                _cancellation.Token);
        }

        public Task WaitForCycleAsync(int cycleNumber)
        {
            lock (_cycleLock)
            {
                if (_cycleCount >= cycleNumber)
                {
                    return Task.CompletedTask;
                }

                var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _cycleWaiters.Add(waiter);
                return waiter.Task.WaitAsync(Patience);
            }
        }

        public async Task StopAsync()
        {
            await _cancellation.CancelAsync();
            await _loop.WaitAsync(Patience);
        }

        public void Dispose()
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
        }

        private async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            if (_holdCycleOn is { } hold)
            {
                CountCycle();
                await hold.WaitAsync(cancellationToken);
                return;
            }

            CountCycle();
            await Task.Yield();

            if (_failure?.Invoke() is { } failure)
            {
                throw failure;
            }
        }

        private void CountCycle()
        {
            TaskCompletionSource[] waiters;
            lock (_cycleLock)
            {
                _cycleCount++;
                waiters = [.. _cycleWaiters];
                _cycleWaiters.Clear();
            }

            foreach (var waiter in waiters)
            {
                waiter.TrySetResult();
            }
        }
    }
}
