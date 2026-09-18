using System.Text.Json;
using Listenarr.Application.Common.Scheduling;
using Listenarr.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Tests.Features.Api.Features.SystemDiagnostics;

/// <summary>
/// The unit <c>intervalSeconds</c> is published in, and what happens to a value that does
/// not fit it.
/// </summary>
/// <remarks>
/// The one number this surface must never invent is 0. A zero interval is read as a task
/// that is never due, so an interval flattened into 0 does not report a fast worker
/// inaccurately, it reports a working worker as dead. Minutes could not hold the four
/// sub-minute workers at all; seconds holds them exactly, and anything finer than a
/// second is refused here rather than truncated.
/// <para>
/// Every refusal test below is paired with a value one step away that is accepted, so
/// that "it refused" cannot also be what a rig that refuses everything looks like.
/// </para>
/// </remarks>
[Trait("Name", "ScheduledTaskIntervalWireFormatTests")]
[Trait("Category", "Api")]
public sealed class ScheduledTaskIntervalWireFormatTests : BaseTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(900)]
    [InlineData(86400)]
    public void WholeSecondsInterval_IsPublished_AndRoundTripsBackToTheIntervalTheWorkerDeclared(
        int seconds)
    {
        var declared = TimeSpan.FromSeconds(seconds);

        var row = ScheduledTaskDto.FromStatus(CreateStatus("SomeWorker", declared));

        Assert.Equal((long)seconds, row.IntervalSeconds);
        Assert.Equal(declared, TimeSpan.FromSeconds(row.IntervalSeconds));
    }

    [Theory]
    [InlineData(1500)]   // 1.5s: truncation would report 1, a schedule the worker never declared
    [InlineData(500)]    // 0.5s: truncation would report 0, which reads as never due
    [InlineData(1)]      // a millisecond, the smallest thing a sub-second worker could declare
    [InlineData(-500)]   // negative also truncates to 0, so it is refused the same way
    public void IntervalFinerThanASecond_IsRefused_AndDoesNotBecomeZero(int milliseconds)
    {
        var declared = TimeSpan.FromMilliseconds(milliseconds);

        var refusal = Assert.Throws<ScheduledTaskIntervalFormatException>(
            () => ScheduledTaskDto.FromStatus(CreateStatus("SubSecondWorker", declared)));

        Assert.Equal("SubSecondWorker", refusal.TaskName);
        Assert.Equal(declared, refusal.Interval);

        // The message has to carry the field and the accepted form, because it is the
        // whole of what the caller gets: an operator reading it should not have to open
        // the source to find out which value the server would not state.
        Assert.Contains("intervalSeconds", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("whole, non-negative seconds", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("SubSecondWorker", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIntervalOfExactlyZero_IsPublishedAsZero_AndIsNotARefusal()
    {
        // The control for the refusals above, and the distinction they depend on. A worker
        // that really declares no gap between cycles is described, with 0, and answered
        // 200. A worker whose interval could not be stated is answered 400 and never
        // reaches a number at all, so the two can never be confused for one another.
        var registry = CreateRegistryReturning(
            CreateStatus("ZeroIntervalWorker", TimeSpan.Zero));
        var controller = new ScheduledTasksController(registry);

        var response = Assert.IsType<OkObjectResult>(controller.GetByName("ZeroIntervalWorker").Result);
        var row = Assert.IsType<ScheduledTaskDto>(response.Value);

        Assert.Equal(0L, row.IntervalSeconds);
    }

    [Fact]
    public void GetByName_AWorkerWhoseIntervalCannotBeStated_Answers400NamingIt()
    {
        var registry = CreateRegistryReturning(
            CreateStatus("SubSecondWorker", TimeSpan.FromMilliseconds(500)));
        var controller = new ScheduledTasksController(registry);

        var response = Assert.IsType<BadRequestObjectResult>(controller.GetByName("SubSecondWorker").Result);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        var body = JsonSerializer.Serialize(response.Value);
        Assert.Contains("intervalSeconds", body, StringComparison.Ordinal);
        Assert.Contains("SubSecondWorker", body, StringComparison.Ordinal);

        // 400 rather than a 5xx because ServerErrorProblemDetailsFilter replaces the body
        // of anything 500 and above with a generic document and drops the detail outside
        // Development (Filters/ServerErrorProblemDetailsFilter.cs:25-33), which would
        // take every word of the message that makes this refusal readable with it.
        Assert.DoesNotContain("\"intervalSeconds\":0", body, StringComparison.Ordinal);
    }

    [Fact]
    public void GetAll_AWorkerWhoseIntervalCannotBeStated_Answers400RatherThanARowSaying0()
    {
        var registry = new Mock<IScheduledTaskRegistry>(MockBehavior.Strict);
        registry.Setup(candidate => candidate.GetAll()).Returns(new[]
        {
            CreateStatus("HealthyWorker", TimeSpan.FromSeconds(10)),
            CreateStatus("SubSecondWorker", TimeSpan.FromMilliseconds(500))
        });
        var controller = new ScheduledTasksController(registry.Object);

        var response = Assert.IsType<BadRequestObjectResult>(controller.GetAll().Result);

        Assert.Contains("SubSecondWorker", JsonSerializer.Serialize(response.Value), StringComparison.Ordinal);
    }

    [Fact]
    public void GetAll_WorkersWhoseIntervalsCanBeStated_Answer200()
    {
        // The control for the test above. Same call, same controller, one row changed, and
        // the list comes back, so the 400 is the refusal rather than a rig that cannot
        // produce a list at all.
        var registry = new Mock<IScheduledTaskRegistry>(MockBehavior.Strict);
        registry.Setup(candidate => candidate.GetAll()).Returns(new[]
        {
            CreateStatus("HealthyWorker", TimeSpan.FromSeconds(10)),
            CreateStatus("AnotherHealthyWorker", TimeSpan.FromMilliseconds(1000))
        });
        var controller = new ScheduledTasksController(registry.Object);

        var response = Assert.IsType<OkObjectResult>(controller.GetAll().Result);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<ScheduledTaskDto>>(response.Value);

        Assert.Equal(new[] { 10L, 1L }, rows.Select(row => row.IntervalSeconds));
    }

    [Fact]
    public void OnTheWire_IntervalSecondsIsAnInteger()
    {
        // The contract is what a client parses, not what the CLR holds, and "10.0" is a
        // different contract from "10": it tells a generated client the field is
        // fractional and invites it to round on its own side.
        var row = ScheduledTaskDto.FromStatus(CreateStatus("SomeWorker", TimeSpan.FromSeconds(10)));

        var json = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"intervalSeconds\":10", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"intervalSeconds\":10.0", json, StringComparison.Ordinal);
    }

    private static IScheduledTaskRegistry CreateRegistryReturning(ScheduledTaskStatus status)
    {
        var registry = new Mock<IScheduledTaskRegistry>(MockBehavior.Strict);
        registry.Setup(candidate => candidate.Find(status.TaskName)).Returns(status);
        return registry.Object;
    }

    private static ScheduledTaskStatus CreateStatus(string taskName, TimeSpan interval)
    {
        var endedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        return new ScheduledTaskStatus
        {
            TaskName = taskName,
            Interval = interval,
            RegisteredAt = endedAt.AddHours(-1),
            IsRegistered = true,
            IsRunning = false,
            ManualTrigger = ScheduledTaskManualTrigger.Allowed,
            LastStartedAt = endedAt.AddSeconds(-30),
            LastEndedAt = endedAt,
            LastDuration = TimeSpan.FromSeconds(30),
            LastOutcome = ScheduledTaskOutcome.Succeeded,
            LastTrigger = ScheduledTaskTrigger.Scheduled,
            NextExecution = endedAt + interval
        };
    }
}
