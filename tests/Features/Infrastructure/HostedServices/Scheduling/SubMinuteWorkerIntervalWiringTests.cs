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

    [Fact]
    public async Task MovedDownloadCleanupService_PublishesTheConfiguredIntervalInSeconds()
    {
        var registry = CreateRegistry();
        var service = new MovedDownloadCleanupService(
            Mock.Of<IMovedDownloadCleanupProcessor>(),
            Mock.Of<ILogger<MovedDownloadCleanupService>>(),
            CreateRunner(registry),
            CreateScopeFactoryReturningPollingInterval(10));

        await AssertPublishedIntervalAsync(service, registry, nameof(MovedDownloadCleanupService), 10);
    }

    [Fact]
    public async Task DownloadMonitorService_PublishesTheConfiguredIntervalInSeconds()
    {
        var registry = CreateRegistry();
        var service = new DownloadMonitorService(
            Mock.Of<IDownloadMonitorProcessor>(),
            Mock.Of<ILogger<DownloadMonitorService>>(),
            CreateRunner(registry),
            CreateScopeFactoryReturningPollingInterval(30));

        await AssertPublishedIntervalAsync(service, registry, nameof(DownloadMonitorService), 30);
    }

    [Fact]
    public void AMinuteGranularSurfaceWouldHavePublishedAllFourAsNeverDue()
    {
        // The control, and the reason the other four tests are worth their runtime. It
        // states the arithmetic they are defending against: every interval on this list
        // is under a minute, so whole minutes is not a coarser answer for them, it is the
        // answer reserved for a task that never runs.
        var shipped = new[]
        {
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30)
        };

        Assert.All(shipped, interval => Assert.Equal(0, (int)interval.TotalMinutes));
        Assert.All(
            shipped,
            interval => Assert.NotEqual(0L, ScheduledTaskDto.ToWholeSeconds("SomeWorker", interval)));
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

            Assert.Equal(expectedSeconds, row.IntervalSeconds);

            // Stated separately from the equality above so that a failure says which of
            // the two things went wrong. Reporting 0 is the poisoned outcome: it does not
            // read as a rounding error, it reads as a worker that never runs.
            Assert.NotEqual(0L, row.IntervalSeconds);
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
    /// scope at startup, so the interval under test is the one a real install would give
    /// them rather than a field default the settings would have overwritten.
    /// </summary>
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
