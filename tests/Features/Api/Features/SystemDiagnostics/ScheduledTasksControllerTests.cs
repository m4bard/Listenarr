using Listenarr.Application.Common.Scheduling;
using Listenarr.Tests.Common;
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
            CreateStatus("move.scan.handoff.recovery")
        });
        var controller = new ScheduledTasksController(registry.Object);

        var result = Assert.IsType<OkObjectResult>(controller.GetAll().Result);
        var tasks = Assert.IsAssignableFrom<IReadOnlyList<ScheduledTaskDto>>(result.Value);

        Assert.Equal(
            new[] { "MetadataRescanService", "move.scan.handoff.recovery" },
            tasks.Select(task => task.Name));
        Assert.Equal(
            new[] { "Metadata Rescan Service", "Move Scan Handoff Recovery" },
            tasks.Select(task => task.DisplayName));
        Assert.Equal(900d, tasks[0].IntervalSeconds);
        Assert.Equal(nameof(ScheduledTaskOutcome.Succeeded), tasks[0].LastOutcome);
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
            .Returns(ScheduledTaskTriggerResult.NotFound);
        var controller = new ScheduledTasksController(registry.Object);

        Assert.IsType<NotFoundObjectResult>(controller.Run("NoSuchWorker").Result);
    }

    [Fact]
    public void Run_TaskAlreadyRunning_IsConflict()
    {
        var registry = new Mock<IScheduledTaskRegistry>(MockBehavior.Strict);
        registry.Setup(candidate => candidate.Trigger("ScanBackgroundService"))
            .Returns(ScheduledTaskTriggerResult.AlreadyRunning);
        var controller = new ScheduledTasksController(registry.Object);

        var result = Assert.IsType<ConflictObjectResult>(controller.Run("ScanBackgroundService").Result);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
    }

    [Fact]
    public void Run_IdleTask_IsAcceptedAndReturnsTheStartedRow()
    {
        var registry = new Mock<IScheduledTaskRegistry>(MockBehavior.Strict);
        registry.Setup(candidate => candidate.Trigger("MetadataRescanService"))
            .Returns(ScheduledTaskTriggerResult.Accepted);
        registry.Setup(candidate => candidate.Find("MetadataRescanService"))
            .Returns(CreateStatus("MetadataRescanService", isRunning: true));
        var controller = new ScheduledTasksController(registry.Object);

        var result = Assert.IsType<AcceptedResult>(controller.Run("MetadataRescanService").Result);
        var task = Assert.IsType<ScheduledTaskDto>(result.Value);

        Assert.Equal(StatusCodes.Status202Accepted, result.StatusCode);
        Assert.True(task.IsRunning);
        Assert.Equal("Manual", task.LastTrigger);
    }

    private static ScheduledTaskStatus CreateStatus(string taskName, bool isRunning = false)
    {
        var endedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        return new ScheduledTaskStatus
        {
            TaskName = taskName,
            Interval = TimeSpan.FromMinutes(15),
            RegisteredAt = endedAt.AddHours(-1),
            IsRunning = isRunning,
            LastStartedAt = endedAt.AddSeconds(-30),
            LastEndedAt = endedAt,
            LastDuration = TimeSpan.FromSeconds(30),
            LastOutcome = ScheduledTaskOutcome.Succeeded,
            LastTrigger = isRunning ? ScheduledTaskTrigger.Manual : ScheduledTaskTrigger.Scheduled,
            NextExecution = endedAt.AddMinutes(15)
        };
    }
}
