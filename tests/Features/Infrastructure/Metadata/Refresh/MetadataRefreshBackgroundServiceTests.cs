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
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>A processor whose last cycle duration the test sets by hand.</summary>
    private sealed class StubProcessor : IMetadataRefreshProcessor
    {
        public TimeSpan? LastCycleElapsed { get; set; }

        public Task RunCycleAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

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
        int requests,
        string status = "Completed") => new(
        Guid.NewGuid(),
        "Scheduled",
        status,
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
                new MetadataRefreshOptionsHolder(),
                ScopeFactoryFor(EnabledSettings()))
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
                new MetadataRefreshOptionsHolder(),
                ScopeFactoryFor(EnabledSettings()))
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
                new MetadataRefreshOptionsHolder(),
                ScopeFactoryFor(EnabledSettings()))
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
                new MetadataRefreshOptionsHolder(),
                ScopeFactoryFor(new ApplicationSettings { MetadataRefreshEnabled = false }))
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
        var holder = new MetadataRefreshOptionsHolder();

        var service = new MetadataRefreshBackgroundService(
            logger,
            Mock.Of<IMetadataRefreshProcessor>(),
            NoOpCycleRunner().Object,
            holder,
            ScopeFactoryFor(EnabledSettings()));
        await service.StartAsync(CancellationToken.None);
        Assert.NotNull(service.ExecuteTask);
        await service.ExecuteTask!.WaitAsync(TestTimeout);
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
    [Trait("Scenario", "StartLineReportsTheOperatorsInterval")]
    public async Task ExecuteAsync_AnnouncesTheIntervalFromSettings_NotTheRecordDefault()
    {
        var logger = new CapturingLogger<MetadataRefreshBackgroundService>();
        var holder = new MetadataRefreshOptionsHolder();

        var service = new MetadataRefreshBackgroundService(
            logger,
            Mock.Of<IMetadataRefreshProcessor>(),
            NoOpCycleRunner().Object,
            holder,
            ScopeFactoryFor(new ApplicationSettings { MetadataRefreshIntervalHours = 6 }));
        await service.StartAsync(CancellationToken.None);
        Assert.NotNull(service.ExecuteTask);
        await service.ExecuteTask!.WaitAsync(TestTimeout);
        await service.StopAsync(CancellationToken.None);

        // The holder is at the record defaults until something reads settings, and the first
        // cycle that would have done so is ten minutes away. Announcing 24 hours to an operator
        // who set 6 is the one line they have to go on.
        Assert.Contains(
            logger.Entries,
            entry => entry.Message ==
                "MetadataRefreshBackgroundService started. Library metadata will be refreshed every 6 hours");
    }

    [Fact]
    [Trait("Scenario", "AnOverrunningCycleDoesNotDoubleThePeriod")]
    public async Task ExecuteAsync_SubtractsTheLastCyclesElapsedTime_FromTheNextDelay()
    {
        Func<TimeSpan>? intervalProvider = null;
        var cycleRunner = CapturingCycleRunner(provider => intervalProvider = provider);
        var processor = new StubProcessor();

        var service = new MetadataRefreshBackgroundService(
            new CapturingLogger<MetadataRefreshBackgroundService>(),
            processor,
            cycleRunner.Object,
            new MetadataRefreshOptionsHolder(),
            ScopeFactoryFor(EnabledSettings()));
        await service.StartAsync(CancellationToken.None);
        Assert.NotNull(service.ExecuteTask);
        await service.ExecuteTask!.WaitAsync(TestTimeout);
        await service.StopAsync(CancellationToken.None);

        Assert.NotNull(intervalProvider);
        Assert.Equal(TimeSpan.FromHours(24), intervalProvider());

        // The cycle runner delays after the cycle rather than between starts, so a cycle that
        // spent three hours inside its window used to push the next one out to twenty-seven.
        processor.LastCycleElapsed = TimeSpan.FromHours(3);
        Assert.Equal(TimeSpan.FromHours(21), intervalProvider());

        // A cycle that spends the whole window still yields a minute before the next one.
        processor.LastCycleElapsed = TimeSpan.FromHours(24);
        Assert.Equal(TimeSpan.FromMinutes(1), intervalProvider());
    }

    [Fact]
    [Trait("Scenario", "IntervalIsReReadEveryPass")]
    public async Task ExecuteAsync_ReReadsTheInterval_SoASettingsChangeNeedsNoRestart()
    {
        Func<TimeSpan>? intervalProvider = null;
        var cycleRunner = CapturingCycleRunner(provider => intervalProvider = provider);
        var holder = new MetadataRefreshOptionsHolder();

        var service = new MetadataRefreshBackgroundService(
            new CapturingLogger<MetadataRefreshBackgroundService>(),
            Mock.Of<IMetadataRefreshProcessor>(),
            cycleRunner.Object,
            holder,
            ScopeFactoryFor(EnabledSettings()));
        await service.StartAsync(CancellationToken.None);
        Assert.NotNull(service.ExecuteTask);
        await service.ExecuteTask!.WaitAsync(TestTimeout);
        await service.StopAsync(CancellationToken.None);

        Assert.NotNull(intervalProvider);
        Assert.Equal(TimeSpan.FromHours(24), intervalProvider());
        holder.Current = new MetadataRefreshOptions(IntervalHours: 6);
        Assert.Equal(TimeSpan.FromHours(6), intervalProvider());
    }

    private static IServiceScopeFactory ScopeFactoryFor(ApplicationSettings settings)
    {
        var configuration = new Mock<IConfigurationService>();
        configuration.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(settings);
        var services = new ServiceCollection();
        services.AddScoped(_ => configuration.Object);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static Mock<IWorkerCycleRunner> NoOpCycleRunner()
    {
        var cycleRunner = new Mock<IWorkerCycleRunner>();
        cycleRunner
            .Setup(runner => runner.RunPeriodicAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<Func<TimeSpan>>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return cycleRunner;
    }

    private static Mock<IWorkerCycleRunner> CapturingCycleRunner(Action<Func<TimeSpan>> capture)
    {
        var cycleRunner = new Mock<IWorkerCycleRunner>();
        cycleRunner
            .Setup(runner => runner.RunPeriodicAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<Func<TimeSpan>>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, TimeSpan?, Func<TimeSpan>, Func<CancellationToken, Task>, CancellationToken>(
                (_, _, provider, _, _) => capture(provider))
            .Returns(Task.CompletedTask);
        return cycleRunner;
    }

    [Fact]
    [Trait("Scenario", "SettingsAreReadEachCycle")]
    public async Task RunCycleAsync_RewritesTheOptionsHolder_FromApplicationSettings()
    {
        var coordinator = new Mock<IMetadataRefreshCoordinator>();
        coordinator
            .Setup(c => c.RunToCompletionAsync(
                It.IsAny<MetadataRefreshScopeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(0, 0, 0, 0));
        var holder = new MetadataRefreshOptionsHolder();

        await new MetadataRefreshProcessor(
                new CapturingLogger<MetadataRefreshProcessor>(),
                coordinator.Object,
                holder,
                ScopeFactoryFor(new ApplicationSettings
                {
                    MetadataRefreshEnabled = true,
                    MetadataRefreshIntervalHours = 6,
                    MetadataRefreshStaleAfterDays = 14,
                    MetadataRefreshRequestsPerHour = 120,
                    MetadataRefreshMinimumSpacingMs = 250
                }))
            .RunCycleAsync(CancellationToken.None);

        Assert.Equal(6, holder.Current.IntervalHours);
        Assert.Equal(14, holder.Current.StaleAfterDays);
        Assert.Equal(120, holder.Current.RequestsPerHour);
        Assert.Equal(250, holder.Current.MinimumSpacingMs);
    }

    [Fact]
    [Trait("Scenario", "DisablingInSettingsStopsTheNextCycle")]
    public async Task RunCycleAsync_StopsStartingRuns_OnceSettingsTurnItOff()
    {
        var coordinator = new Mock<IMetadataRefreshCoordinator>();
        coordinator
            .Setup(c => c.RunToCompletionAsync(
                It.IsAny<MetadataRefreshScopeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(0, 0, 0, 0));
        var holder = new MetadataRefreshOptionsHolder();

        await new MetadataRefreshProcessor(
                new CapturingLogger<MetadataRefreshProcessor>(),
                coordinator.Object,
                holder,
                ScopeFactoryFor(new ApplicationSettings { MetadataRefreshEnabled = false }))
            .RunCycleAsync(CancellationToken.None);

        Assert.False(holder.Current.Enabled);
        coordinator.Verify(
            c => c.RunToCompletionAsync(
                It.IsAny<MetadataRefreshScopeRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    [Trait("Scenario", "ATruncatedCycleStillReportsItself")]
    public async Task RunCycleAsync_LogsTheCycleLine_ForARunTheWindowTruncated()
    {
        var coordinator = new Mock<IMetadataRefreshCoordinator>();
        coordinator
            .Setup(c => c.RunToCompletionAsync(
                It.IsAny<MetadataRefreshScopeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(processed: 4, updated: 4, deferred: 0, requests: 4, status: "Truncated"));
        var logger = new CapturingLogger<MetadataRefreshProcessor>();

        await new MetadataRefreshProcessor(
                logger,
                coordinator.Object,
                new MetadataRefreshOptionsHolder(),
                ScopeFactoryFor(EnabledSettings()))
            .RunCycleAsync(CancellationToken.None);

        // A window that closed early is a cycle that finished, not one that failed, and the
        // operator's only view of the walk is this line.
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Information && entry.Message ==
                "MetadataRefreshBackgroundService completed refresh cycle. Updated 4 of 4 audiobook(s), 0 deferred, 4 provider request(s) spent");
    }

    [Fact]
    [Trait("Scenario", "AbsurdSettingsAreClamped")]
    public async Task RunCycleAsync_ClampsSettings_ToBoundsTheWalkCanWorkIn()
    {
        var coordinator = new Mock<IMetadataRefreshCoordinator>();
        coordinator
            .Setup(c => c.RunToCompletionAsync(
                It.IsAny<MetadataRefreshScopeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(0, 0, 0, 0));
        var holder = new MetadataRefreshOptionsHolder();

        await new MetadataRefreshProcessor(
                new CapturingLogger<MetadataRefreshProcessor>(),
                coordinator.Object,
                holder,
                ScopeFactoryFor(new ApplicationSettings
                {
                    MetadataRefreshEnabled = true,
                    MetadataRefreshIntervalHours = 100000,
                    MetadataRefreshStaleAfterDays = -5,
                    MetadataRefreshRequestsPerHour = 0,
                    MetadataRefreshMinimumSpacingMs = 86400000
                }))
            .RunCycleAsync(CancellationToken.None);

        // Nothing else validates these. A spacing of a day does not slow the walk down, it
        // stops it, and it does so without saying anything.
        Assert.Equal(168, holder.Current.IntervalHours);
        Assert.Equal(0, holder.Current.StaleAfterDays);
        Assert.Equal(1, holder.Current.RequestsPerHour);
        Assert.Equal(60000, holder.Current.MinimumSpacingMs);
    }

    /// <summary>
    /// The shipped settings with the feature turned on. It ships off, so a test about what a
    /// cycle does has to say so, and a test about the default asserts the default instead.
    /// </summary>
    private static ApplicationSettings EnabledSettings() =>
        new() { MetadataRefreshEnabled = true };

    [Fact]
    [Trait("Scenario", "ShippedDefaults")]
    public void ApplicationSettings_ShipTheTimidBudget_AndShipItOff()
    {
        // 60 an hour with a one-second floor is deliberately conservative: the provider
        // publishes no limit, and off by default because every existing book is due the moment
        // this is on and the walk overwrites local metadata from the provider.
        var settings = new ApplicationSettings();

        Assert.False(settings.MetadataRefreshEnabled);
        Assert.Equal(24, settings.MetadataRefreshIntervalHours);
        Assert.Equal(30, settings.MetadataRefreshStaleAfterDays);
        Assert.Equal(60, settings.MetadataRefreshRequestsPerHour);
        Assert.Equal(1000, settings.MetadataRefreshMinimumSpacingMs);
    }
}
