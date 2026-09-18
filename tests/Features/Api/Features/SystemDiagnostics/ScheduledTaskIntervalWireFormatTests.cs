using System.Text.Json;
using System.Text.Json.Serialization;
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
/// second is refused rather than truncated.
/// <para>
/// The refusal travels on the row, as an explicit null beside a message, rather than in
/// the status code, so that one worker nobody can describe does not withhold the nine
/// that can be. Every refusal test below is paired with a value one step away that is
/// accepted, so that "it refused" cannot also be what a rig that refuses everything looks
/// like.
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

        Assert.Equal((long?)seconds, row.IntervalSeconds);
        Assert.Null(row.IntervalError);
        Assert.Equal(declared, TimeSpan.FromSeconds(row.IntervalSeconds!.Value));
    }

    [Theory]
    [InlineData(1500)]   // 1.5s: truncation would report 1, a schedule the worker never declared
    [InlineData(500)]    // 0.5s: truncation would report 0, which reads as never due
    [InlineData(1)]      // a millisecond, the smallest thing a sub-second worker could declare
    [InlineData(-500)]   // negative also truncates to 0, so it is refused the same way
    public void IntervalFinerThanASecond_IsRefused_AndDoesNotBecomeZero(int milliseconds)
    {
        var declared = TimeSpan.FromMilliseconds(milliseconds);

        var row = ScheduledTaskDto.FromStatus(CreateStatus("SubSecondWorker", declared));

        Assert.Null(row.IntervalSeconds);
        Assert.NotEqual((long?)0, row.IntervalSeconds);

        // The message is the whole of what a reader gets, so it has to carry the field and
        // the accepted form: an operator should not have to open the source to find out
        // which value the server would not state.
        Assert.NotNull(row.IntervalError);
        Assert.Contains("intervalSeconds", row.IntervalError, StringComparison.Ordinal);
        Assert.Contains("whole, non-negative seconds", row.IntervalError, StringComparison.Ordinal);
        Assert.Contains("SubSecondWorker", row.IntervalError, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIntervalOfExactlyZero_IsPublishedAsZero_AndCarriesNoRefusal()
    {
        // The control for the refusals above, and the distinction they depend on. A worker
        // that really declares no gap between cycles is described, with 0 and no message.
        // A worker whose interval could not be stated carries null and a message. The two
        // can never be read as one another.
        var row = ScheduledTaskDto.FromStatus(CreateStatus("ZeroIntervalWorker", TimeSpan.Zero));

        Assert.Equal((long?)0, row.IntervalSeconds);
        Assert.Null(row.IntervalError);
    }

    [Fact]
    public void OnTheWire_ARefusedIntervalIsAnExplicitNull_RatherThanAMissingKey()
    {
        // This is the test the whole shape rests on. Controllers serialize with
        // WhenWritingNull, which drops a null property, and a missing numeric field is
        // read as 0 by any client with a non-nullable field: the silent zero, arriving by
        // a different door. JsonIgnore(Never) on the property overrides that.
        var refused = ScheduledTaskDto.FromStatus(
            CreateStatus("SubSecondWorker", TimeSpan.FromMilliseconds(500), omitLastStartedAt: true));

        var json = JsonSerializer.Serialize(refused, ApiSerializerOptions());

        Assert.Contains("\"intervalSeconds\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"intervalError\":", json, StringComparison.Ordinal);

        // The control, and it has to come out differently or the assertion above proves
        // nothing: lastStartedAt is null on this same row and carries no attribute, so if
        // WhenWritingNull were not in force it would be present too, and the explicit null
        // above would be the default rather than the attribute doing its job.
        Assert.DoesNotContain("\"lastStartedAt\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void OnTheWire_AStatableIntervalIsAnIntegerToken_AndARefusedOneIsANullToken()
    {
        var options = ApiSerializerOptions();

        using var statable = JsonDocument.Parse(JsonSerializer.Serialize(
            ScheduledTaskDto.FromStatus(CreateStatus("SomeWorker", TimeSpan.FromSeconds(10))),
            options));
        using var refused = JsonDocument.Parse(JsonSerializer.Serialize(
            ScheduledTaskDto.FromStatus(CreateStatus("SubSecondWorker", TimeSpan.FromMilliseconds(500))),
            options));

        var statableValue = statable.RootElement.GetProperty("intervalSeconds");
        Assert.Equal(JsonValueKind.Number, statableValue.ValueKind);
        Assert.True(statableValue.TryGetInt64(out var seconds));
        Assert.Equal(10L, seconds);

        // Asserted on the parsed token rather than on the text, because a whole-valued
        // double serializes as "10" too: searching the string for "10.0" would pass
        // against the double this field replaced and so would prove nothing.
        Assert.Equal(JsonValueKind.Null, refused.RootElement.GetProperty("intervalSeconds").ValueKind);
    }

    [Fact]
    public void GetAll_AWorkerWhoseIntervalCannotBeStated_DoesNotWithholdTheRowsThatCan()
    {
        // The reason the refusal is on the row and not in the status code. Answering 400
        // for the list would withhold every healthy worker because of one broken one, on a
        // surface whose whole job is telling an operator which workers are alive, and the
        // registry one layer down already refuses to make that trade.
        var registry = new Mock<IScheduledTaskRegistry>(MockBehavior.Strict);
        registry.Setup(candidate => candidate.GetAll()).Returns(new[]
        {
            CreateStatus("HealthyWorker", TimeSpan.FromSeconds(10)),
            CreateStatus("SubSecondWorker", TimeSpan.FromMilliseconds(500))
        });
        var controller = new ScheduledTasksController(registry.Object);

        var response = Assert.IsType<OkObjectResult>(controller.GetAll().Result);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<ScheduledTaskDto>>(response.Value);

        Assert.Equal(new long?[] { 10L, null }, rows.Select(row => row.IntervalSeconds));
        Assert.Null(rows[0].IntervalError);
        Assert.NotNull(rows[1].IntervalError);
    }

    [Fact]
    public void GetByName_AWorkerWhoseIntervalCannotBeStated_StillDescribesTheRest()
    {
        var registry = new Mock<IScheduledTaskRegistry>(MockBehavior.Strict);
        registry.Setup(candidate => candidate.Find("SubSecondWorker"))
            .Returns(CreateStatus("SubSecondWorker", TimeSpan.FromMilliseconds(500)));
        var controller = new ScheduledTasksController(registry.Object);

        var response = Assert.IsType<OkObjectResult>(controller.GetByName("SubSecondWorker").Result);
        var row = Assert.IsType<ScheduledTaskDto>(response.Value);

        Assert.Null(row.IntervalSeconds);
        Assert.NotNull(row.IntervalError);

        // Everything else about the row is still true and still useful, which is the
        // argument for describing it rather than refusing the request.
        Assert.Equal("SubSecondWorker", row.Name);
        Assert.True(row.IsRegistered);
        Assert.Equal(nameof(ScheduledTaskOutcome.Succeeded), row.LastOutcome);
    }

    [Fact]
    public void Run_AWorkerWhoseIntervalCannotBeStated_StillAnswers202()
    {
        // The cycle has already been dispatched by the time the row is built, so anything
        // other than 202 would deny a side effect that happened. The refusal rides along
        // on the row instead.
        var registry = new Mock<IScheduledTaskRegistry>(MockBehavior.Strict);
        registry.Setup(candidate => candidate.Trigger("SubSecondWorker"))
            .Returns(ScheduledTaskTriggerOutcome.For(
                ScheduledTaskTriggerResult.Accepted,
                CreateStatus("SubSecondWorker", TimeSpan.FromMilliseconds(500), isRunning: true)));
        var controller = new ScheduledTasksController(registry.Object);

        var response = Assert.IsType<AcceptedResult>(controller.Run("SubSecondWorker").Result);
        var run = Assert.IsType<ScheduledTaskRunDto>(response.Value);

        Assert.Equal(StatusCodes.Status202Accepted, response.StatusCode);
        Assert.Equal(ScheduledTaskRunDto.Started, run.Triggered);
        Assert.True(run.Task.IsRunning);
        Assert.Null(run.Task.IntervalSeconds);
        Assert.NotNull(run.Task.IntervalError);
    }

    /// <summary>
    /// The options the controllers are actually registered with
    /// (<c>listenarr.api/Startup/ListenarrServiceRegistration.cs:41-44</c>). Copied rather
    /// than resolved because the registration builds a whole host, and the only part that
    /// bears on this contract is the ignore condition.
    /// </summary>
    private static JsonSerializerOptions ApiSerializerOptions() =>
        new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

    private static ScheduledTaskStatus CreateStatus(
        string taskName,
        TimeSpan interval,
        bool isRunning = false,
        bool omitLastStartedAt = false)
    {
        var endedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        return new ScheduledTaskStatus
        {
            TaskName = taskName,
            Interval = interval,
            RegisteredAt = endedAt.AddHours(-1),
            IsRegistered = true,
            IsRunning = isRunning,
            ManualTrigger = ScheduledTaskManualTrigger.Allowed,
            LastStartedAt = omitLastStartedAt ? null : endedAt.AddSeconds(-30),
            LastEndedAt = endedAt,
            LastDuration = TimeSpan.FromSeconds(30),
            LastOutcome = ScheduledTaskOutcome.Succeeded,
            LastTrigger = isRunning ? ScheduledTaskTrigger.Manual : ScheduledTaskTrigger.Scheduled,
            NextExecution = endedAt + (interval > TimeSpan.Zero ? interval : TimeSpan.Zero)
        };
    }
}
