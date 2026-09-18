using Listenarr.Application.Common.Scheduling;
using Listenarr.Infrastructure.HostedServices.Scheduling;
using Listenarr.Tests.Common;
using Listenarr.Tests.Features.Infrastructure.HostedServices.Scheduling;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Tests.Features.Api.Features.SystemDiagnostics;

[Trait("Name", "ScheduledTasksControllerTests")]
[Trait("Category", "Api")]
public sealed class ScheduledTasksControllerTests : BaseTests
{
    [Fact]
    public void GetAll_ReturnsOneRowPerRegisteredTask()
    {
        var registry = new Mock<IScheduledTaskRegistry>(MockBehavior.Strict);
        registry.Setup(candidate => candidate.GetAll()).Returns(new[]
        {
            CreateStatus("MetadataRescanService"),
            CreateStatus("move.scan.handoff.recovery", manualTrigger: ScheduledTaskManualTrigger.Denied)
        });
        var controller = new ScheduledTasksController(registry.Object);

        var result = Assert.IsType<OkObjectResult>(controller.GetAll().Result);
        var tasks = Assert.IsAssignableFrom<IReadOnlyList<ScheduledTaskDto>>(result.Value);

        // The whole allowlist is readable off this one call, which is what keeps it
        // auditable without opening ten registration sites.
        Assert.Equal(new[] { true, false }, tasks.Select(task => task.IsManualRunAllowed));

        Assert.Equal(
            new[] { "MetadataRescanService", "move.scan.handoff.recovery" },
            tasks.Select(task => task.Name));
        Assert.Equal(
            new[] { "Metadata Rescan Service", "Move Scan Handoff Recovery" },
            tasks.Select(task => task.DisplayName));
        Assert.Equal(900d, tasks[0].IntervalSeconds);
        Assert.Equal(nameof(ScheduledTaskOutcome.Succeeded), tasks[0].LastOutcome);
        Assert.True(tasks[0].IsRegistered);
    }

    [Fact]
    public async Task GetAll_TakesItsOrderFromTheRegistryRatherThanRegistrationOrder()
    {
        // The controller does not sort, so asserting an order against a mocked registry
        // only asserts the mock's own input back at itself. Registering out of
        // alphabetical order through the real registry is what actually tests it.
        var registry = CreateRegistry();
        using var second = new PeriodicWorkerHarness(registry, "ZebraWorker");
        using var first = new PeriodicWorkerHarness(registry, "AardvarkWorker");
        await second.WaitForCycleAsync(1);
        await first.WaitForCycleAsync(1);
        var controller = new ScheduledTasksController(registry);

        var result = Assert.IsType<OkObjectResult>(controller.GetAll().Result);
        var tasks = Assert.IsAssignableFrom<IReadOnlyList<ScheduledTaskDto>>(result.Value);

        Assert.Equal(new[] { "AardvarkWorker", "ZebraWorker" }, tasks.Select(task => task.Name));
        await second.StopAsync();
        await first.StopAsync();
    }

    [Fact]
    public void GetByName_UnknownTask_IsNotFound()
    {
        var registry = new Mock<IScheduledTaskRegistry>(MockBehavior.Strict);
        registry.Setup(candidate => candidate.Find("NoSuchWorker")).Returns((ScheduledTaskStatus?)null);
        var controller = new ScheduledTasksController(registry.Object);

        Assert.IsType<NotFoundObjectResult>(controller.GetByName("NoSuchWorker").Result);
    }

    [Fact]
    public void Run_UnknownTask_IsNotFound()
    {
        var registry = new Mock<IScheduledTaskRegistry>(MockBehavior.Strict);
        registry.Setup(candidate => candidate.Trigger("NoSuchWorker"))
            .Returns(ScheduledTaskTriggerOutcome.NotFound);
        var controller = new ScheduledTasksController(registry.Object);

        Assert.IsType<NotFoundObjectResult>(controller.Run("NoSuchWorker").Result);
    }

    [Fact]
    public void Run_TaskAlreadyRunning_AnswersWithTheCycleInFlight()
    {
        // The family answers a duplicate request with the run already under way rather
        // than an error: CommandQueueManager.Push returns the command already queued or
        // started (NzbDrone.Core/Messaging/Commands/CommandQueueManager.cs:111-121) and
        // CommandController hands it straight back as a success
        // (Sonarr.Api.V3/Commands/CommandController.cs:75-77). A user clicking twice gets
        // something to watch, not a failure.
        var registry = new Mock<IScheduledTaskRegistry>(MockBehavior.Strict);
        registry.Setup(candidate => candidate.Trigger("ScanBackgroundService"))
            .Returns(ScheduledTaskTriggerOutcome.For(
                ScheduledTaskTriggerResult.AlreadyRunning,
                CreateStatus("ScanBackgroundService", isRunning: true)));
        var controller = new ScheduledTasksController(registry.Object);

        var result = Assert.IsType<AcceptedResult>(controller.Run("ScanBackgroundService").Result);
        var run = Assert.IsType<ScheduledTaskRunDto>(result.Value);

        Assert.Equal(StatusCodes.Status202Accepted, result.StatusCode);
        Assert.Equal(ScheduledTaskRunDto.AlreadyRunning, run.Triggered);
        Assert.True(run.Task.IsRunning);
        Assert.NotNull(run.Task.LastStartedAt);
    }

    [Fact]
    public void Run_IdleTask_RelaysWhateverRowTheRegistryHandsBack()
    {
        // Renamed from a name that promised a production behaviour this cannot observe.
        // With a mocked registry, relaying is the only claim available; the real-registry
        // test below is the one that carries the behavioural claim.
        var registry = new Mock<IScheduledTaskRegistry>(MockBehavior.Strict);
        registry.Setup(candidate => candidate.Trigger("MetadataRescanService"))
            .Returns(ScheduledTaskTriggerOutcome.For(
                ScheduledTaskTriggerResult.Accepted,
                CreateStatus("MetadataRescanService", isRunning: true)));
        var controller = new ScheduledTasksController(registry.Object);

        var result = Assert.IsType<AcceptedResult>(controller.Run("MetadataRescanService").Result);
        var run = Assert.IsType<ScheduledTaskRunDto>(result.Value);

        Assert.Equal(StatusCodes.Status202Accepted, result.StatusCode);
        Assert.Equal(ScheduledTaskRunDto.Started, run.Triggered);
        Assert.True(run.Task.IsRunning);
        Assert.Equal("Manual", run.Task.LastTrigger);
    }

    [Fact]
    public async Task Run_TwiceWhileTheCycleIsHeld_SaysWhichRequestStartedIt()
    {
        // The rows for the two outcomes are identical, down to lastStartedAt, because the
        // second caller is being told about the cycle the first caller started. Without a
        // discriminator in the body a UI cannot tell "started" from "already running",
        // and the endpoint's documentation used to claim it could.
        var registry = CreateRegistry();
        using var gate = new SemaphoreSlim(0, 1);
        using var worker = new PeriodicWorkerHarness(
            registry,
            "DoubleClickedWorker",
            holdCycleOn: gate,
            holdFromCycle: 2,
            manualTrigger: ScheduledTaskManualTrigger.Allowed);
        var controller = new ScheduledTasksController(registry);

        await worker.WaitForCycleAsync(1);
        await WaitForIdleAsync(registry, "DoubleClickedWorker");

        var first = Assert.IsType<AcceptedResult>(controller.Run("DoubleClickedWorker").Result);
        await worker.WaitForCycleAsync(2);
        var second = Assert.IsType<AcceptedResult>(controller.Run("DoubleClickedWorker").Result);

        var started = Assert.IsType<ScheduledTaskRunDto>(first.Value);
        var joined = Assert.IsType<ScheduledTaskRunDto>(second.Value);

        Assert.Equal(ScheduledTaskRunDto.Started, started.Triggered);
        Assert.Equal(ScheduledTaskRunDto.AlreadyRunning, joined.Triggered);

        // The rows really are indistinguishable, which is why the discriminator exists.
        Assert.Equal(started.Task.IsRunning, joined.Task.IsRunning);
        Assert.Equal(started.Task.LastTrigger, joined.Task.LastTrigger);
        Assert.Equal(started.Task.LastStartedAt, joined.Task.LastStartedAt);

        // The rig has to be live, or "the second call did not start anything" is also what
        // a dead harness looks like: one scheduled cycle plus exactly one manual cycle.
        gate.Release();
        await Task.Delay(200);
        Assert.Equal(2, worker.CycleCount);
        await worker.StopAsync();
    }

    [Fact]
    public async Task Run_IdleTask_ReturnsTheStartedRowFromTheRealRegistry()
    {
        // The test above can only show that the controller relays what it is handed. A
        // mocked registry cannot say whether the production path produces that row, and
        // the row it used to produce said isRunning false, lastTrigger Scheduled. This
        // one drives the real registry and the real cycle runner, so the 202 body is the
        // one a caller would actually receive.
        var registry = CreateRegistry();
        using var worker = new PeriodicWorkerHarness(
            registry,
            "RealTriggerableWorker",
            manualTrigger: ScheduledTaskManualTrigger.Allowed);
        var controller = new ScheduledTasksController(registry);

        await worker.WaitForCycleAsync(1);
        await WaitForIdleAsync(registry, "RealTriggerableWorker");

        var result = Assert.IsType<AcceptedResult>(controller.Run("RealTriggerableWorker").Result);
        var run = Assert.IsType<ScheduledTaskRunDto>(result.Value);

        Assert.Equal(StatusCodes.Status202Accepted, result.StatusCode);
        Assert.Equal(ScheduledTaskRunDto.Started, run.Triggered);
        Assert.True(run.Task.IsRunning);
        Assert.Equal("Manual", run.Task.LastTrigger);
        Assert.NotNull(run.Task.LastStartedAt);
        await worker.StopAsync();
    }

    [Fact]
    public async Task Run_StoppedWorker_IsConflictRatherThanNotFoundBecauseGetStillListsIt()
    {
        // Answering "no such task" for a row the same API lists contradicts the reasoning
        // that gives a scheduled-only worker a 403 rather than a 404: a caller looking
        // straight at the row goes hunting for a spelling mistake that is not there.
        var registry = CreateRegistry();
        using var worker = new PeriodicWorkerHarness(
            registry,
            "StoppedTriggerableWorker",
            manualTrigger: ScheduledTaskManualTrigger.Allowed);
        var controller = new ScheduledTasksController(registry);

        await worker.WaitForCycleAsync(1);
        await worker.StopAsync();

        var listed = Assert.IsType<OkObjectResult>(controller.GetAll().Result);
        var tasks = Assert.IsAssignableFrom<IReadOnlyList<ScheduledTaskDto>>(listed.Value);
        Assert.False(Assert.Single(tasks).IsRegistered);

        var refused = Assert.IsType<ConflictObjectResult>(
            controller.Run("StoppedTriggerableWorker").Result);

        Assert.Equal(StatusCodes.Status409Conflict, refused.StatusCode);
        Assert.Contains("stopped", refused.Value?.ToString(), StringComparison.OrdinalIgnoreCase);

        // And a name nothing ever registered is still a 404, or the two have collapsed.
        Assert.IsType<NotFoundObjectResult>(controller.Run("NeverRegisteredWorker").Result);
    }

    [Fact]
    public void Run_TaskNotOnTheAllowlist_IsForbiddenRatherThanNotFound()
    {
        var registry = new Mock<IScheduledTaskRegistry>(MockBehavior.Strict);
        registry.Setup(candidate => candidate.Trigger("DownloadProcessingJobCleanupService"))
            .Returns(ScheduledTaskTriggerOutcome.For(
                ScheduledTaskTriggerResult.NotAllowed,
                CreateStatus(
                    "DownloadProcessingJobCleanupService",
                    manualTrigger: ScheduledTaskManualTrigger.Denied)));
        var controller = new ScheduledTasksController(registry.Object);

        var result = Assert.IsType<ObjectResult>(
            controller.Run("DownloadProcessingJobCleanupService").Result);

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);

        // A registered task the caller may not start is not the same as a task that does
        // not exist, and the body has to say which it is.
        Assert.Contains("schedule", result.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No scheduled task named", result.Value?.ToString());
    }

    [Fact]
    public void Run_UnknownTriggerResult_ThrowsRatherThanQuietlyAnswering202()
    {
        // Accepted is 0, so anything unmapped would previously have fallen into the
        // default arm and been reported as a started run. A result added later has to
        // fail loudly instead.
        var registry = new Mock<IScheduledTaskRegistry>(MockBehavior.Strict);
        registry.Setup(candidate => candidate.Trigger("FutureWorker"))
            .Returns(ScheduledTaskTriggerOutcome.For(
                (ScheduledTaskTriggerResult)99,
                CreateStatus("FutureWorker")));
        var controller = new ScheduledTasksController(registry.Object);

        var thrown = Assert.Throws<InvalidOperationException>(() => controller.Run("FutureWorker"));
        Assert.Contains("99", thrown.Message);
    }

    private static ScheduledTaskRegistry CreateRegistry() =>
        new(TimeProvider.System, Mock.Of<ILogger<ScheduledTaskRegistry>>());

    private static async Task<ScheduledTaskStatus> WaitForIdleAsync(
        IScheduledTaskRegistry registry,
        string taskName)
    {
        var deadline = DateTimeOffset.UtcNow + PeriodicWorkerHarness.Patience;
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

    private static ScheduledTaskStatus CreateStatus(
        string taskName,
        bool isRunning = false,
        ScheduledTaskManualTrigger manualTrigger = ScheduledTaskManualTrigger.Allowed)
    {
        var endedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        return new ScheduledTaskStatus
        {
            TaskName = taskName,
            Interval = TimeSpan.FromMinutes(15),
            RegisteredAt = endedAt.AddHours(-1),
            IsRegistered = true,
            IsRunning = isRunning,
            ManualTrigger = manualTrigger,
            LastStartedAt = endedAt.AddSeconds(-30),
            LastEndedAt = endedAt,
            LastDuration = TimeSpan.FromSeconds(30),
            LastOutcome = ScheduledTaskOutcome.Succeeded,
            LastTrigger = isRunning ? ScheduledTaskTrigger.Manual : ScheduledTaskTrigger.Scheduled,
            NextExecution = endedAt.AddMinutes(15)
        };
    }
}
