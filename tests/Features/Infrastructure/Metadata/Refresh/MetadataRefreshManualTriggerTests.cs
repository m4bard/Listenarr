/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Application.Common.Scheduling;
using Listenarr.Infrastructure.HostedServices;
using Listenarr.Infrastructure.HostedServices.Scheduling;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Metadata.Refresh;

/// <summary>
/// Item 279, gap 1: <c>MetadataRefreshBackgroundService</c> registered itself with no
/// <c>manualTrigger:</c> argument, so it inherited <see cref="ScheduledTaskManualTrigger.Denied"/>
/// while six sibling background services (author/series monitoring, the automatic search
/// processor, the download monitor, the identity repair worker, the metadata rescan job) all
/// pass <see cref="ScheduledTaskManualTrigger.Allowed"/>. The task scheduler UI shipped by item
/// 183 offers a Run button for any Allowed task, so the worker was silently the one row on that
/// screen an operator could see but never press.
/// </summary>
/// <remarks>
/// Modelled on <c>ScheduledTaskAllowlistWiringTests</c> (item 183): the real registry and the
/// real cycle runner, not a stand-in, because what matters is whether a request that reaches
/// the task surface's <c>Run</c> endpoint for this worker's name actually reaches its cycle
/// body, not that the enum reads a particular way in isolation.
/// </remarks>
[Trait("Area", "Metadata")]
[Trait("Name", "MetadataRefreshManualTriggerTests")]
[Trait("Category", "BackgroundWorkers")]
public sealed class MetadataRefreshManualTriggerTests : BaseTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    [Trait("Scenario", "RegistersAllowed")]
    public async Task ExecuteAsync_RegistersWithTheCycleRunner_AsManualTriggerAllowed()
    {
        // Fast, precise pin on the fix line itself: whatever ExecuteAsync hands the cycle
        // runner as manualTrigger. Before the fix this captures Denied, the parameter's own
        // default, because the call site passed nothing.
        ScheduledTaskManualTrigger? captured = null;
        var cycleRunner = new Mock<IWorkerCycleRunner>();
        cycleRunner
            .Setup(runner => runner.RunPeriodicAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<Func<TimeSpan>>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<ScheduledTaskManualTrigger>()))
            .Callback<string, TimeSpan?, Func<TimeSpan>, Func<CancellationToken, Task>, CancellationToken, ScheduledTaskManualTrigger>(
                (_, _, _, _, _, manualTrigger) => captured = manualTrigger)
            .Returns(Task.CompletedTask);

        var service = new MetadataRefreshBackgroundService(
            Mock.Of<ILogger<MetadataRefreshBackgroundService>>(),
            Mock.Of<IMetadataRefreshProcessor>(),
            cycleRunner.Object,
            new MetadataRefreshOptionsHolder(),
            ScopeFactoryFor(new ApplicationSettings { MetadataRefreshEnabled = true }));

        await service.StartAsync(CancellationToken.None);
        Assert.NotNull(service.ExecuteTask);
        await service.ExecuteTask!.WaitAsync(Patience);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(ScheduledTaskManualTrigger.Allowed, captured);
    }

    [Fact]
    [Trait("Scenario", "OperatorVisibleReachability")]
    public async Task MetadataRefresh_IsListedAsAllowed_AndAManualTriggerReachesItsProcessor()
    {
        var registry = CreateRegistry();
        var cycles = new CycleCounter();
        var processor = new Mock<IMetadataRefreshProcessor>();
        processor.SetupGet(candidate => candidate.LastCycleElapsed).Returns((TimeSpan?)null);
        processor
            .Setup(candidate => candidate.RunCycleAsync(It.IsAny<CancellationToken>()))
            .Returns(cycles.RecordAsync);

        var service = new MetadataRefreshBackgroundService(
            Mock.Of<ILogger<MetadataRefreshBackgroundService>>(),
            processor.Object,
            CreateRunner(registry),
            new MetadataRefreshOptionsHolder(),
            ScopeFactoryFor(new ApplicationSettings { MetadataRefreshEnabled = true }));

        using var cancellation = new CancellationTokenSource();
        await service.StartAsync(cancellation.Token);
        try
        {
            var status = await WaitForRegistrationAsync(registry, nameof(MetadataRefreshBackgroundService));

            // Known-good, and the whole point of the fix: before it this is Denied and the
            // assertion below never gets a chance to run.
            Assert.Equal(ScheduledTaskManualTrigger.Allowed, status.ManualTrigger);

            // The service's own initial delay is ten real-time minutes, so nothing scheduled
            // has run by the time this fires. A cycle landing here is unambiguously the manual
            // path, not a race against the first scheduled one.
            var triggered = registry.Trigger(nameof(MetadataRefreshBackgroundService));
            Assert.Equal(ScheduledTaskTriggerResult.Accepted, triggered.Result);

            await cycles.WaitForAsync(1);
            var afterRun = await WaitForIdleAsync(registry, nameof(MetadataRefreshBackgroundService));
            Assert.Equal(ScheduledTaskTrigger.Manual, afterRun.LastTrigger);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    [Trait("Scenario", "EdgeUnregisteredName")]
    public void Trigger_BeforeTheServiceEverStarts_IsNotFound()
    {
        // Edge case distinct from the Denied/NotAllowed answer the fix concerns: nothing has
        // registered under this name at all yet. Guards against a harness bug that would make
        // any name look triggerable.
        var registry = CreateRegistry();

        var triggered = registry.Trigger(nameof(MetadataRefreshBackgroundService));

        Assert.Equal(ScheduledTaskTriggerResult.NotFound, triggered.Result);
    }

    private static ScheduledTaskRegistry CreateRegistry() =>
        new(TimeProvider.System, Mock.Of<ILogger<ScheduledTaskRegistry>>());

    private static WorkerCycleRunner CreateRunner(IScheduledTaskRegistry registry) =>
        new(
            TimeProvider.System,
            Mock.Of<IAppMetricsService>(),
            registry,
            Mock.Of<ILogger<WorkerCycleRunner>>());

    private static IServiceScopeFactory ScopeFactoryFor(ApplicationSettings settings)
    {
        var configuration = new Mock<IConfigurationService>();
        configuration.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(settings);
        var services = new ServiceCollection();
        services.AddScoped(_ => configuration.Object);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
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

            await Task.Delay(10);
        }
        while (DateTimeOffset.UtcNow < deadline);

        Assert.Fail($"'{taskName}' never reached the registry.");
        throw new InvalidOperationException("unreachable");
    }

    private static async Task<ScheduledTaskStatus> WaitForIdleAsync(
        IScheduledTaskRegistry registry,
        string taskName)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;

        do
        {
            if (registry.Find(taskName) is { IsRunning: false, LastEndedAt: not null } status)
            {
                return status;
            }

            await Task.Delay(10);
        }
        while (DateTimeOffset.UtcNow < deadline);

        Assert.Fail($"'{taskName}' never came to rest.");
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
