using Listenarr.Application.Common.Scheduling;
using Listenarr.Infrastructure.HostedServices.Scheduling;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Scheduling;

[Trait("Name", "ScheduledTaskRegistryTests")]
[Trait("Category", "BackgroundWorkers")]
public sealed class ScheduledTaskRegistryTests : BaseTests
{
    private static readonly TimeSpan Interval = PeriodicWorkerHarness.DefaultInterval;
    private static readonly TimeSpan Patience = PeriodicWorkerHarness.Patience;

    [Fact]
    public async Task Worker_IsDiscoveredWithoutRegisteringItself()
    {
        var registry = CreateRegistry();
        using var worker = new PeriodicWorkerHarness(registry, "DiscoveredWorker");

        await worker.WaitForCycleAsync(1);

        var listed = registry.GetAll();
        Assert.Equal(new[] { "DiscoveredWorker" }, listed.Select(task => task.TaskName));
        Assert.True(listed[0].IsRegistered);
        await worker.StopAsync();
    }

    [Fact]
    public async Task SucceededCycle_RecordsTimingOutcomeAndNextExecution()
    {
        var registry = CreateRegistry();
        using var worker = new PeriodicWorkerHarness(registry, "SucceedingWorker");

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
        using var worker = new PeriodicWorkerHarness(
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

        var outcome = registry.Trigger("NoSuchWorker");

        Assert.Equal(ScheduledTaskTriggerResult.NotFound, outcome.Result);
        Assert.Null(outcome.Status);
    }

    [Fact]
    public async Task Trigger_IdleTask_RunsAnExtraCycleAndRecordsTheManualTrigger()
    {
        var registry = CreateRegistry();
        using var worker = new PeriodicWorkerHarness(
            registry,
            "TriggerableWorker",
            manualTrigger: ScheduledTaskManualTrigger.Allowed);

        await worker.WaitForCycleAsync(1);
        await WaitForIdleAsync(registry, "TriggerableWorker");

        Assert.Equal(
            ScheduledTaskTriggerResult.Accepted,
            registry.Trigger("TriggerableWorker").Result);

        await worker.WaitForCycleAsync(2);
        var status = await WaitForIdleAsync(registry, "TriggerableWorker");

        Assert.Equal(ScheduledTaskTrigger.Manual, status.LastTrigger);
        Assert.Equal(ScheduledTaskOutcome.Succeeded, status.LastOutcome);
        await worker.StopAsync();
    }

    [Fact]
    public async Task Trigger_IdleTask_AnswersWithTheManualCycleAlreadyStarted()
    {
        // The row handed back has to describe the run the caller just asked for. The
        // cycle body is dispatched onto the thread pool, so anything that waits for the
        // body to set the state describes the cycle before it instead: not running, last
        // triggered by the schedule. Nothing here awaits between the trigger and the
        // assertions, which is what makes the claim settleable in process rather than
        // needing a live instance and a stopwatch.
        var registry = CreateRegistry();
        using var worker = new PeriodicWorkerHarness(
            registry,
            "PromptWorker",
            manualTrigger: ScheduledTaskManualTrigger.Allowed);

        await worker.WaitForCycleAsync(1);
        await WaitForIdleAsync(registry, "PromptWorker");

        var outcome = registry.Trigger("PromptWorker");

        Assert.Equal(ScheduledTaskTriggerResult.Accepted, outcome.Result);
        Assert.NotNull(outcome.Status);
        Assert.True(outcome.Status.IsRunning);
        Assert.Equal(ScheduledTaskTrigger.Manual, outcome.Status.LastTrigger);
        Assert.NotNull(outcome.Status.LastStartedAt);
        await worker.StopAsync();
    }

    [Fact]
    public async Task Trigger_TaskAlreadyRunning_AnswersWithTheCycleInFlightWithoutOverlapping()
    {
        var registry = CreateRegistry();
        using var gate = new SemaphoreSlim(0, 1);
        using var worker = new PeriodicWorkerHarness(
            registry,
            "BusyWorker",
            holdCycleOn: gate,
            manualTrigger: ScheduledTaskManualTrigger.Allowed);

        await worker.WaitForCycleAsync(1);

        var outcome = registry.Trigger("BusyWorker");

        Assert.Equal(ScheduledTaskTriggerResult.AlreadyRunning, outcome.Result);

        // The answer carries the cycle that is in flight, the way the family's
        // CommandQueueManager.Push hands back a command already queued or started
        // (NzbDrone.Core/Messaging/Commands/CommandQueueManager.cs:111-121).
        Assert.NotNull(outcome.Status);
        Assert.True(outcome.Status.IsRunning);
        Assert.Equal(ScheduledTaskTrigger.Scheduled, outcome.Status.LastTrigger);

        // Answering with it is not the same as starting it twice. Give a wrongly
        // accepted run room to appear rather than racing it.
        await Task.Delay(200);
        Assert.Equal(1, worker.CycleCount);

        gate.Release();
        await worker.StopAsync();
    }

    [Fact]
    public async Task ScheduledCycleInFlight_HasNoNextExecutionUntilItEnds()
    {
        // A scheduled cycle consumes the deadline it was waiting for, so while it runs
        // there is no next execution to report. Without the clear, the row keeps
        // advertising a deadline that has already been spent, which for the first cycle
        // is the registration time and therefore already in the past.
        var registry = CreateRegistry();
        using var gate = new SemaphoreSlim(0, 1);
        using var worker = new PeriodicWorkerHarness(registry, "HeldWorker", holdCycleOn: gate);

        await worker.WaitForCycleAsync(1);

        var midCycle = registry.Find("HeldWorker");
        Assert.NotNull(midCycle);
        Assert.True(midCycle.IsRunning);
        Assert.Null(midCycle.NextExecution);

        gate.Release();
        var afterCycle = await WaitForIdleAsync(registry, "HeldWorker");
        Assert.NotNull(afterCycle.NextExecution);
        await worker.StopAsync();
    }

    [Fact]
    public async Task ManualCycleInFlight_LeavesTheScheduledDeadlineStanding()
    {
        // The mirror of the case above, and the reason the clear is keyed on the trigger
        // rather than applied to every cycle. A manual run happens beside the interval
        // wait, so the scheduled deadline is still real and has to survive it.
        var registry = CreateRegistry();
        using var gate = new SemaphoreSlim(0, 1);
        using var worker = new PeriodicWorkerHarness(
            registry,
            "ManuallyHeldWorker",
            holdCycleOn: gate,
            holdFromCycle: 2,
            manualTrigger: ScheduledTaskManualTrigger.Allowed);

        await worker.WaitForCycleAsync(1);
        var idle = await WaitForIdleAsync(registry, "ManuallyHeldWorker");
        var deadline = idle.NextExecution;
        Assert.NotNull(deadline);

        var outcome = registry.Trigger("ManuallyHeldWorker");

        Assert.Equal(ScheduledTaskTriggerResult.Accepted, outcome.Result);
        Assert.NotNull(outcome.Status);
        Assert.True(outcome.Status.IsRunning);
        Assert.Equal(deadline, outcome.Status.NextExecution);

        await worker.WaitForCycleAsync(2);
        var midManual = registry.Find("ManuallyHeldWorker");
        Assert.NotNull(midManual);
        Assert.Equal(deadline, midManual.NextExecution);

        gate.Release();
        await worker.StopAsync();
    }

    [Fact]
    public void InitialDelay_IsPublishedAsTheFirstNextExecution()
    {
        // Four of the shipped workers wait before their first cycle, and until that
        // cycle runs the deadline is the only thing the surface can say about them.
        // RunPeriodicAsync registers and publishes the deadline before it reaches its
        // first await, so nothing has to be polled for here.
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var registry = CreateRegistry(clock);
        var delay = TimeSpan.FromMinutes(5);
        using var worker = new PeriodicWorkerHarness(
            registry,
            "DelayedWorker",
            initialDelay: delay,
            timeProvider: clock);

        var status = registry.Find("DelayedWorker");

        Assert.NotNull(status);
        Assert.Equal(clock.GetUtcNow(), status.RegisteredAt);
        Assert.Equal(clock.GetUtcNow() + delay, status.NextExecution);
        Assert.Equal(ScheduledTaskOutcome.Unknown, status.LastOutcome);
        Assert.Null(status.LastStartedAt);
        Assert.Null(status.LastTrigger);
        Assert.False(status.IsRunning);
        Assert.Equal(0, worker.CycleCount);
    }

    [Fact]
    public async Task StoppedWorker_StaysOnTheSurfaceMarkedStopped()
    {
        // A monitoring surface whose one invisible state is "this worker is gone" is
        // worse than no surface. The row keeps its last outcome and says it stopped.
        var registry = CreateRegistry();
        using var worker = new PeriodicWorkerHarness(registry, "StoppingWorker");

        await worker.WaitForCycleAsync(1);
        await WaitForIdleAsync(registry, "StoppingWorker");
        await worker.StopAsync();

        var stopped = Assert.Single(registry.GetAll());
        Assert.Equal("StoppingWorker", stopped.TaskName);
        Assert.False(stopped.IsRegistered);
        Assert.False(stopped.IsRunning);
        Assert.Equal(ScheduledTaskOutcome.Succeeded, stopped.LastOutcome);
        Assert.NotNull(stopped.LastEndedAt);
    }

    [Fact]
    public async Task Trigger_StoppedWorker_IsNotFoundBecauseThereIsNoLoopLeft()
    {
        var registry = CreateRegistry();
        using var worker = new PeriodicWorkerHarness(
            registry,
            "GoneWorker",
            manualTrigger: ScheduledTaskManualTrigger.Allowed);

        await worker.WaitForCycleAsync(1);
        await worker.StopAsync();
        var cyclesBefore = worker.CycleCount;

        var outcome = registry.Trigger("GoneWorker");

        Assert.Equal(ScheduledTaskTriggerResult.NotFound, outcome.Result);
        await Task.Delay(200);
        Assert.Equal(cyclesBefore, worker.CycleCount);
    }

    [Fact]
    public async Task RestartedWorker_ReplacesItsOwnStoppedRow()
    {
        // Keeping stopped rows must not turn an ordinary stop and start into two rows,
        // or into the "two workers registered as X" warning that means a real collision.
        var registry = CreateRegistry();
        using var first = new PeriodicWorkerHarness(registry, "RestartingWorker");
        await first.WaitForCycleAsync(1);
        await first.StopAsync();

        using var second = new PeriodicWorkerHarness(registry, "RestartingWorker");
        await second.WaitForCycleAsync(1);

        var listed = Assert.Single(registry.GetAll());
        Assert.True(listed.IsRegistered);
        await second.StopAsync();
    }

    [Fact]
    public async Task Worker_ThatSaysNothing_IsNotTriggerable()
    {
        var registry = CreateRegistry();
        using var worker = new PeriodicWorkerHarness(registry, "QuietWorker");

        await worker.WaitForCycleAsync(1);

        var status = registry.Find("QuietWorker");
        Assert.NotNull(status);
        Assert.Equal(ScheduledTaskManualTrigger.Denied, status.ManualTrigger);
        await worker.StopAsync();
    }

    [Fact]
    public async Task Trigger_TaskNotOnTheAllowlist_IsRefusedAndItsCycleIsNeverEntered()
    {
        // The case the allowlist exists for. A deny-list would have had to know this
        // worker's name in advance; here it is refused because nobody said otherwise.
        var registry = CreateRegistry();
        using var worker = new PeriodicWorkerHarness(registry, "DestructiveWorker");

        await worker.WaitForCycleAsync(1);
        await WaitForIdleAsync(registry, "DestructiveWorker");
        var cyclesBefore = worker.CycleCount;

        var refused = registry.Trigger("DestructiveWorker");

        Assert.Equal(ScheduledTaskTriggerResult.NotAllowed, refused.Result);

        // Refusing is not enough on its own: prove the cycle body was never reached.
        // The manual run is dispatched on a pool thread when it is accepted, so give a
        // wrongly accepted one room to show up rather than racing it.
        await Task.Delay(200);
        Assert.Equal(cyclesBefore, worker.CycleCount);
        await worker.StopAsync();
    }

    [Fact]
    public async Task Trigger_RefusalIsDistinctFromAnUnknownTask()
    {
        // NotFound and NotAllowed must not collapse into each other, or a caller cannot
        // tell a typo from a task they are simply not allowed to start.
        var registry = CreateRegistry();
        using var worker = new PeriodicWorkerHarness(registry, "PresentButDeniedWorker");

        await worker.WaitForCycleAsync(1);

        Assert.Equal(
            ScheduledTaskTriggerResult.NotAllowed,
            registry.Trigger("PresentButDeniedWorker").Result);
        Assert.Equal(
            ScheduledTaskTriggerResult.NotFound,
            registry.Trigger("NoSuchWorker").Result);
        await worker.StopAsync();
    }

    private static ScheduledTaskRegistry CreateRegistry(TimeProvider? timeProvider = null) =>
        new(timeProvider ?? TimeProvider.System, Mock.Of<ILogger<ScheduledTaskRegistry>>());

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
}
