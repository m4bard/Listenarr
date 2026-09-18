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
        var task = Assert.IsType<ScheduledTaskDto>(result.Value);

        Assert.Equal(StatusCodes.Status202Accepted, result.StatusCode);
        Assert.True(task.IsRunning);
        Assert.NotNull(task.LastStartedAt);
    }

    [Fact]
    public void Run_IdleTask_IsAcceptedAndReturnsTheStartedRow()
    {
        var registry = new Mock<IScheduledTaskRegistry>(MockBehavior.Strict);
        registry.Setup(candidate => candidate.Trigger("MetadataRescanService"))
            .Returns(ScheduledTaskTriggerOutcome.For(
                ScheduledTaskTriggerResult.Accepted,
                CreateStatus("MetadataRescanService", isRunning: true)));
        var controller = new ScheduledTasksController(registry.Object);

        var result = Assert.IsType<AcceptedResult>(controller.Run("MetadataRescanService").Result);
        var task = Assert.IsType<ScheduledTaskDto>(result.Value);

        Assert.Equal(StatusCodes.Status202Accepted, result.StatusCode);
        Assert.True(task.IsRunning);
        Assert.Equal("Manual", task.LastTrigger);
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
        var task = Assert.IsType<ScheduledTaskDto>(result.Value);

        Assert.Equal(StatusCodes.Status202Accepted, result.StatusCode);
        Assert.True(task.IsRunning);
        Assert.Equal("Manual", task.LastTrigger);
        Assert.NotNull(task.LastStartedAt);
        await worker.StopAsync();
    }

    [Fact]
    public async Task Run_StoppedWorker_IsNotFoundEvenThoughItsRowIsStillListed()
    {
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

        Assert.IsType<NotFoundObjectResult>(controller.Run("StoppedTriggerableWorker").Result);
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
