using System.Threading.Channels;
using Listenarr.Application.Common.Scheduling;
using Listenarr.Infrastructure.Downloads.DirectDownload;
using Listenarr.Infrastructure.HostedServices;
using Listenarr.Infrastructure.HostedServices.Scheduling;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Scheduling;

/// <summary>
/// The four shipped workers that run faster than once a minute, against the real cycle
/// runner and the real registry, read through the row the API actually returns.
/// </summary>
/// <remarks>
/// This is the regression the wire unit exists for. Four of the workers on this surface
/// cycle in seconds, and a surface that measured intervals in whole minutes would publish
/// every one of them as 0. Zero is not a small number here: a task with a zero interval is
/// read as one that is never due, so the page would have reported four working workers as
/// dead, on a surface whose entire job is telling an operator whether its workers are
/// alive.
/// <para>
/// The services are the shipped ones rather than stand-ins, because what is being tested
/// is the interval each one really declares. A fake that declares 10 seconds proves only
/// that the harness can count to ten.
/// </para>
/// </remarks>
[Trait("Name", "SubMinuteWorkerIntervalWiringTests")]
[Trait("Category", "BackgroundWorkers")]
public sealed class SubMinuteWorkerIntervalWiringTests : BaseTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task DirectDownloadService_PublishesItsTenSecondInterval()
    {
        var registry = CreateRegistry();
        var service = new DirectDownloadService(
            Mock.Of<IDirectDownloadProcessor>(),
            // NullLogger rather than a mock: the service is internal, so Castle cannot
            // build a proxy for a logger closed over it, and nothing here reads the log.
            NullLogger<DirectDownloadService>.Instance,
            CreateRunner(registry));

        await AssertPublishedIntervalAsync(service, registry, nameof(DirectDownloadService), 10);
    }

    [Fact]
    public async Task MoveScanHandoffRecovery_PublishesItsThirtySecondInterval()
    {
        var registry = CreateRegistry();
        var queue = new Mock<IScanQueueService>();
        queue.SetupGet(service => service.Reader)
            .Returns(Channel.CreateUnbounded<ScanJob>().Reader);
        var handoffStore = new Mock<IMoveScanHandoffStore>();
        handoffStore
            .Setup(store => store.GetClaimableIdsAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        await using var provider = new ServiceCollection().BuildServiceProvider();

        var service = new ScanBackgroundService(
            queue.Object,
            Mock.Of<IScanJobProcessor>(),
            new MoveScanHandoffRecoveryService(
                queue.Object,
                handoffStore.Object,
                provider.GetRequiredService<IServiceScopeFactory>(),
                TimeProvider.System,
                Mock.Of<ILogger<MoveScanHandoffRecoveryService>>()),
            CreateRunner(registry),
            TestLibraryFilesystemReadiness.Ready(),
            Mock.Of<ILogger<ScanBackgroundService>>());

        await AssertPublishedIntervalAsync(service, registry, "move.scan.handoff.recovery", 30);
    }

    [Theory]
    [InlineData(0, 10)]   // settings set nothing, so the service's own default stands
    [InlineData(15, 15)]  // a configured value in seconds reaches the row in seconds
    public async Task MovedDownloadCleanupService_PublishesItsIntervalInSeconds(
        int configuredSeconds,
        long expectedSeconds)
    {
        var registry = CreateRegistry();
        var service = new MovedDownloadCleanupService(
            Mock.Of<IMovedDownloadCleanupProcessor>(),
            Mock.Of<ILogger<MovedDownloadCleanupService>>(),
            CreateRunner(registry),
            CreateScopeFactoryReturningPollingInterval(configuredSeconds));

        await AssertPublishedIntervalAsync(
            service,
            registry,
            nameof(MovedDownloadCleanupService),
            expectedSeconds);
    }

    [Theory]
    [InlineData(0, 30)]   // settings set nothing, so the service's own default stands
    [InlineData(15, 15)]  // a configured value in seconds reaches the row in seconds
    public async Task DownloadMonitorService_PublishesItsIntervalInSeconds(
        int configuredSeconds,
        long expectedSeconds)
    {
        var registry = CreateRegistry();
        var service = new DownloadMonitorService(
            Mock.Of<IDownloadMonitorProcessor>(),
            Mock.Of<ILogger<DownloadMonitorService>>(),
            CreateRunner(registry),
            CreateScopeFactoryReturningPollingInterval(configuredSeconds));

        await AssertPublishedIntervalAsync(
            service,
            registry,
            nameof(DownloadMonitorService),
            expectedSeconds);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    public void WholeMinutesCannotHoldASubMinuteInterval_WhereWholeSecondsCan(int seconds)
    {
        // Not a control on the four tests above, and it should not be read as one: the
        // values are literals rather than anything the shipped services declare. It states
        // the arithmetic those tests are defending against, which is that whole minutes is
        // not a coarser answer for an interval under a minute, it is the answer reserved
        // for a task that never runs.
        var interval = TimeSpan.FromSeconds(seconds);

        Assert.Equal(0, (int)interval.TotalMinutes);
        Assert.Equal((long?)seconds, ScheduledTaskDto.ToWholeSeconds(interval));
    }

    private static async Task AssertPublishedIntervalAsync(
        IHostedService service,
        IScheduledTaskRegistry registry,
        string taskName,
        long expectedSeconds)
    {
        using var cancellation = new CancellationTokenSource();
        await service.StartAsync(cancellation.Token);
        try
        {
            var status = await WaitForRegistrationAsync(registry, taskName);
            var row = ScheduledTaskDto.FromStatus(status);

            Assert.Equal((long?)expectedSeconds, row.IntervalSeconds);
            Assert.Null(row.IntervalError);

            // Stated separately from the equality above so that a failure says which of
            // the two things went wrong. Reporting 0 is the poisoned outcome: it does not
            // read as a rounding error, it reads as a worker that never runs.
            Assert.NotEqual((long?)0, row.IntervalSeconds);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static ScheduledTaskRegistry CreateRegistry() =>
        new(TimeProvider.System, Mock.Of<ILogger<ScheduledTaskRegistry>>());

    private static WorkerCycleRunner CreateRunner(IScheduledTaskRegistry registry) =>
        new(
            TimeProvider.System,
            Mock.Of<IAppMetricsService>(),
            registry,
            Mock.Of<ILogger<WorkerCycleRunner>>());

    /// <summary>
    /// Both configuration-driven workers read <c>PollingIntervalSeconds</c> out of a
    /// scope at startup, and both ignore it when it is not positive.
    /// </summary>
    /// <remarks>
    /// Passing 0 is what makes the pair of cases worth having. It leaves each service on
    /// its own hardcoded default, so the row is asserted against the value the service
    /// ships with rather than against a number this test just handed it.
    /// </remarks>
    private static IServiceScopeFactory CreateScopeFactoryReturningPollingInterval(int seconds)
    {
        var configuration = new Mock<IConfigurationService>();
        configuration
            .Setup(service => service.GetApplicationSettingsAsync())
            .ReturnsAsync(new ApplicationSettings { PollingIntervalSeconds = seconds });

        return new ServiceCollection()
            .AddSingleton(configuration.Object)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
    }

    private static async Task<ScheduledTaskStatus> WaitForRegistrationAsync(
        IScheduledTaskRegistry registry,
        string taskName)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;

        do
        {
            if (registry.Find(taskName) is { } status)
            {
                return status;
            }

            await Task.Delay(25);
        }
        while (DateTimeOffset.UtcNow < deadline);

        Assert.Fail($"'{taskName}' never reached the registry.");
        throw new InvalidOperationException("unreachable");
    }
}
