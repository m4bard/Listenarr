using Listenarr.Application.Common.Scheduling;
using Listenarr.Infrastructure.HostedServices;
using Listenarr.Infrastructure.HostedServices.Scheduling;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Scheduling;

/// <summary>
/// The allowlist against the real hosted services rather than a stand-in, because what
/// matters is which of the shipped workers can be started from the API, not that a fake
/// named "DestructiveWorker" behaves.
/// </summary>
/// <remarks>
/// The two tests here are each other's control. Same registry, same cycle runner, two real
/// services, opposite outcomes. Without the pair, "the processor was never called" would
/// also be what a broken harness looks like.
/// </remarks>
[Trait("Name", "ScheduledTaskAllowlistWiringTests")]
[Trait("Category", "BackgroundWorkers")]
public sealed class ScheduledTaskAllowlistWiringTests : BaseTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RetentionCleanup_IsRefused_AndItsProcessorIsNeverReached()
    {
        // The defect this exists for: the retention cleaner prunes terminal job rows, and
        // the run endpoint used to hand that to anyone who could name it.
        var registry = CreateRegistry();
        var processor = new Mock<IDownloadProcessingJobCleanupProcessor>(MockBehavior.Strict);
        var service = new DownloadProcessingJobCleanupService(
            processor.Object,
            CreateRunner(registry),
            Mock.Of<ILogger<DownloadProcessingJobCleanupService>>());

        using var cancellation = new CancellationTokenSource();
        await service.StartAsync(cancellation.Token);
        try
        {
            var status = await WaitForRegistrationAsync(registry, nameof(DownloadProcessingJobCleanupService));
            Assert.Equal(ScheduledTaskManualTrigger.Denied, status.ManualTrigger);

            var refused = registry.Trigger(nameof(DownloadProcessingJobCleanupService));

            Assert.Equal(ScheduledTaskTriggerResult.NotAllowed, refused.Result);

            // A strict mock with no setup throws on any call, so an accepted run would
            // surface as a failure rather than as a silently deleted row. Give the pool
            // dispatch room first, or the assertion races the thing it is testing.
            await Task.Delay(200);
            processor.Verify(
                candidate => candidate.RunCycleAsync(It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task MetadataRescan_IsAllowed_AndAManualTriggerReachesItsProcessor()
    {
        // The control. Same harness, a service that did opt in, and the cycle body is
        // provably reached, so the refusal above is the allowlist and not a dead rig.
        var registry = CreateRegistry();
        var cycles = new CycleCounter();
        var processor = new Mock<IMetadataRescanProcessor>();
        processor
            .Setup(candidate => candidate.RunCycleAsync(It.IsAny<CancellationToken>()))
            .Returns(cycles.RecordAsync);

        var readiness = new Mock<ILibraryFilesystemReadiness>();
        readiness
            .Setup(candidate => candidate.WaitUntilReadyAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new MetadataRescanService(
            Mock.Of<ILogger<MetadataRescanService>>(),
            processor.Object,
            CreateRunner(registry),
            readiness.Object);

        using var cancellation = new CancellationTokenSource();
        await service.StartAsync(cancellation.Token);
        try
        {
            var status = await WaitForRegistrationAsync(registry, nameof(MetadataRescanService));
            Assert.Equal(ScheduledTaskManualTrigger.Allowed, status.ManualTrigger);

            // Let the first scheduled cycle land so the manual one is unambiguously extra.
            await cycles.WaitForAsync(1);

            Assert.Equal(
                ScheduledTaskTriggerResult.Accepted,
                registry.Trigger(nameof(MetadataRescanService)).Result);

            await cycles.WaitForAsync(2);
            Assert.Equal(
                ScheduledTaskTrigger.Manual,
                (await WaitForRegistrationAsync(registry, nameof(MetadataRescanService))).LastTrigger);
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

            await Task.Delay(10);
        }
        while (DateTimeOffset.UtcNow < deadline);

        Assert.Fail($"'{taskName}' never reached the registry.");
        throw new InvalidOperationException("unreachable");
    }

    private sealed class CycleCounter
    {
        private readonly object _lock = new();
        private readonly List<TaskCompletionSource> _waiters = [];
        private int _count;

        public Task RecordAsync()
        {
            TaskCompletionSource[] waiters;
            lock (_lock)
            {
                _count++;
                waiters = [.. _waiters];
                _waiters.Clear();
            }

            foreach (var waiter in waiters)
            {
                waiter.TrySetResult();
            }

            return Task.CompletedTask;
        }

        public Task WaitForAsync(int count)
        {
            lock (_lock)
            {
                if (_count >= count)
                {
                    return Task.CompletedTask;
                }

                var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add(waiter);
                return waiter.Task.WaitAsync(Patience);
            }
        }
    }
}
