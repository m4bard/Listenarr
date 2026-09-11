/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Metadata.Refresh;

/// <summary>
/// The cycle log line is the only thing an operator sees from the scheduled walk, so its shape
/// is pinned here rather than left to drift.
/// </summary>
[Trait("Area", "Metadata")]
[Trait("Name", "MetadataRefreshBackgroundServiceTests")]
[Trait("Category", "Infrastructure")]
public class MetadataRefreshBackgroundServiceTests : BaseTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static MetadataRefreshRunSnapshot Snapshot(
        int processed,
        int updated,
        int deferred,
        int requests) => new(
        Guid.NewGuid(),
        "Scheduled",
        "Completed",
        processed,
        processed,
        updated,
        processed - updated - deferred,
        deferred,
        0,
        requests,
        new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 9, 10, 13, 0, 0, DateTimeKind.Utc));

    [Fact]
    [Trait("Scenario", "CycleLineCarriesTheCounters")]
    public async Task RunCycleAsync_LogsTheCycleLine_WithUpdatedProcessedDeferredAndRequests()
    {
        var coordinator = new Mock<IMetadataRefreshCoordinator>();
        coordinator
            .Setup(c => c.RunToCompletionAsync(
                It.IsAny<MetadataRefreshScopeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(processed: 12, updated: 9, deferred: 2, requests: 31));
        var logger = new CapturingLogger<MetadataRefreshProcessor>();

        await new MetadataRefreshProcessor(
                logger,
                coordinator.Object,
                new MetadataRefreshOptionsHolder())
            .RunCycleAsync(CancellationToken.None);

        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Information && entry.Message ==
                "MetadataRefreshBackgroundService completed refresh cycle. Updated 9 of 12 audiobook(s), 2 deferred, 31 provider request(s) spent");
    }

    [Fact]
    [Trait("Scenario", "ScheduledScopeIsWhatTheWalkAsksFor")]
    public async Task RunCycleAsync_AsksForTheScheduledScope_NotAForcedLibraryRun()
    {
        var coordinator = new Mock<IMetadataRefreshCoordinator>();
        coordinator
            .Setup(c => c.RunToCompletionAsync(
                It.IsAny<MetadataRefreshScopeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(0, 0, 0, 0));

        await new MetadataRefreshProcessor(
                new CapturingLogger<MetadataRefreshProcessor>(),
                coordinator.Object,
                new MetadataRefreshOptionsHolder())
            .RunCycleAsync(CancellationToken.None);

        coordinator.Verify(
            c => c.RunToCompletionAsync(
                It.Is<MetadataRefreshScopeRequest>(request =>
                    request.Scope == MetadataRefreshRunScope.Scheduled
                    && request.MonitoredAuthorId == null
                    && !request.Force),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Scenario", "GateHeldIsSilentAtInformation")]
    public async Task RunCycleAsync_LogsNoCycleLine_WhenAnotherRunHoldsTheGate()
    {
        var coordinator = new Mock<IMetadataRefreshCoordinator>();
        coordinator
            .Setup(c => c.RunToCompletionAsync(
                It.IsAny<MetadataRefreshScopeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetadataRefreshRunSnapshot?)null);
        var logger = new CapturingLogger<MetadataRefreshProcessor>();

        await new MetadataRefreshProcessor(
                logger,
                coordinator.Object,
                new MetadataRefreshOptionsHolder())
            .RunCycleAsync(CancellationToken.None);

        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Information);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Debug);
    }

    [Fact]
    [Trait("Scenario", "DisabledDoesNothing")]
    public async Task RunCycleAsync_DoesNotStartARun_WhenTheFeatureIsTurnedOff()
    {
        var coordinator = new Mock<IMetadataRefreshCoordinator>();

        await new MetadataRefreshProcessor(
                new CapturingLogger<MetadataRefreshProcessor>(),
                coordinator.Object,
                new MetadataRefreshOptionsHolder
                {
                    Current = new MetadataRefreshOptions(Enabled: false)
                })
            .RunCycleAsync(CancellationToken.None);

        coordinator.Verify(
            c => c.RunToCompletionAsync(
                It.IsAny<MetadataRefreshScopeRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    [Trait("Scenario", "StartAndStopLines")]
    public async Task ExecuteAsync_AnnouncesTheIntervalOnStart_AndSaysSoOnStop()
    {
        var logger = new CapturingLogger<MetadataRefreshBackgroundService>();
        var cycleRunner = new Mock<IWorkerCycleRunner>();
        cycleRunner
            .Setup(runner => runner.RunPeriodicAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<Func<TimeSpan>>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var holder = new MetadataRefreshOptionsHolder();

        var service = new MetadataRefreshBackgroundService(
            logger,
            Mock.Of<IMetadataRefreshProcessor>(),
            cycleRunner.Object,
            holder);
        await service.StartAsync(CancellationToken.None);
        Assert.NotNull(service.ExecuteTask);
        await service.ExecuteTask!;
        await service.StopAsync(CancellationToken.None);

        Assert.Contains(
            logger.Entries,
            entry => entry.Message ==
                "MetadataRefreshBackgroundService started. Library metadata will be refreshed every 24 hours");
        Assert.Contains(
            logger.Entries,
            entry => entry.Message == "MetadataRefreshBackgroundService stopped");
    }

    [Fact]
    [Trait("Scenario", "IntervalIsReReadEveryPass")]
    public async Task ExecuteAsync_ReReadsTheInterval_SoASettingsChangeNeedsNoRestart()
    {
        Func<TimeSpan>? intervalProvider = null;
        var cycleRunner = new Mock<IWorkerCycleRunner>();
        cycleRunner
            .Setup(runner => runner.RunPeriodicAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<Func<TimeSpan>>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, TimeSpan?, Func<TimeSpan>, Func<CancellationToken, Task>, CancellationToken>(
                (_, _, provider, _, _) => intervalProvider = provider)
            .Returns(Task.CompletedTask);
        var holder = new MetadataRefreshOptionsHolder();

        var service = new MetadataRefreshBackgroundService(
            new CapturingLogger<MetadataRefreshBackgroundService>(),
            Mock.Of<IMetadataRefreshProcessor>(),
            cycleRunner.Object,
            holder);
        await service.StartAsync(CancellationToken.None);
        Assert.NotNull(service.ExecuteTask);
        await service.ExecuteTask!;
        await service.StopAsync(CancellationToken.None);

        Assert.NotNull(intervalProvider);
        Assert.Equal(TimeSpan.FromHours(24), intervalProvider());
        holder.Current = new MetadataRefreshOptions(IntervalHours: 6);
        Assert.Equal(TimeSpan.FromHours(6), intervalProvider());
    }
}
