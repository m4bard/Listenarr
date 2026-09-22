/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Tests.Common;
using Listenarr.Tests.Features.Infrastructure.HostedServices.Scheduling;

namespace Listenarr.Tests.Features.Infrastructure.Maintenance.Housekeeping;

/// <summary>
/// The sweep frame, exercised with stand-in housekeepers so it is pinned independently of any
/// table. What is being tested here is the part every housekeeper inherits: the window, the
/// preview switch, the ceiling, the per-table floor, and the promise that one failure costs one
/// table.
/// </summary>
[Trait("Area", "Housekeeping")]
[Trait("Name", "HousekeepingProcessorTests")]
[Trait("Category", "BackgroundWorkers")]
public sealed class HousekeepingProcessorTests : BaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OneHousekeeperThrowing_DoesNotStopTheOthers()
    {
        var first = new RecordingHousekeeper("first");
        var exploding = new ThrowingHousekeeper("exploding");
        var last = new RecordingHousekeeper("last");

        await RunAsync(Settings(retentionDays: 30), first, exploding, last);

        Assert.Equal(1, first.Runs);
        Assert.Equal(1, exploding.Runs);
        Assert.Equal(1, last.Runs);
    }

    /// <summary>
    /// The control for the test above. Without it, "the last housekeeper ran" would also be what
    /// a processor that silently skipped the middle one looks like, and a sweep that never calls
    /// anybody would pass neither.
    /// </summary>
    [Fact]
    public async Task WithNothingThrowing_EveryHousekeeperStillRunsExactlyOnce()
    {
        var first = new RecordingHousekeeper("first");
        var middle = new RecordingHousekeeper("middle");
        var last = new RecordingHousekeeper("last");

        await RunAsync(Settings(retentionDays: 30), first, middle, last);

        Assert.Equal(1, first.Runs);
        Assert.Equal(1, middle.Runs);
        Assert.Equal(1, last.Runs);
    }

    [Fact]
    public async Task ZeroRetention_AsksNoHousekeeperAnything()
    {
        var housekeeper = new RecordingHousekeeper("cache");

        await RunAsync(Settings(retentionDays: 0), housekeeper);

        Assert.Equal(0, housekeeper.Runs);
    }

    /// <summary>The control for zero. The same fixture at thirty reaches the housekeeper.</summary>
    [Fact]
    public async Task ThirtyDayRetention_ReachesTheHousekeeper()
    {
        var housekeeper = new RecordingHousekeeper("cache");

        await RunAsync(Settings(retentionDays: 30), housekeeper);

        Assert.Equal(1, housekeeper.Runs);
        Assert.Equal(Now.UtcDateTime.AddDays(-30), housekeeper.LastCycle!.CutoffUtc);
    }

    [Fact]
    public async Task ANegativeRetention_IsReadAsUnlimitedRatherThanAsAWindowInTheFuture()
    {
        var housekeeper = new RecordingHousekeeper("cache");

        await RunAsync(Settings(retentionDays: -5), housekeeper);

        Assert.Equal(0, housekeeper.Runs);
    }

    [Fact]
    public async Task TheDryRunSwitch_ReachesTheHousekeeperUnchanged()
    {
        var previewing = new RecordingHousekeeper("cache");
        await RunAsync(Settings(retentionDays: 30, dryRun: true), previewing);
        Assert.True(previewing.LastCycle!.DryRun);

        // The control: the identical fixture with the switch off.
        var deleting = new RecordingHousekeeper("cache");
        await RunAsync(Settings(retentionDays: 30, dryRun: false), deleting);
        Assert.False(deleting.LastCycle!.DryRun);
    }

    [Fact]
    public async Task AHousekeeperWithItsOwnFloor_KeepsRowsTheConfiguredWindowWouldHaveDeleted()
    {
        var floored = new RecordingHousekeeper("journal", minimumRetentionDays: 90);
        var unfloored = new RecordingHousekeeper("cache");

        await RunAsync(Settings(retentionDays: 30), floored, unfloored);

        Assert.Equal(90, floored.LastCycle!.EffectiveRetentionDays);
        Assert.Equal(Now.UtcDateTime.AddDays(-90), floored.LastCycle.CutoffUtc);

        // The control on the same run. A row sixty days old sits inside the floored
        // housekeeper's window and outside the unfloored one's, so the floor is doing
        // something rather than being applied to everybody.
        var sixtyDaysOld = Now.UtcDateTime.AddDays(-60);
        Assert.True(sixtyDaysOld > floored.LastCycle.CutoffUtc);
        Assert.True(sixtyDaysOld < unfloored.LastCycle!.CutoffUtc);
        Assert.Equal(30, unfloored.LastCycle.EffectiveRetentionDays);
    }

    [Fact]
    public async Task AConfiguredWindowLongerThanTheFloor_Wins()
    {
        var floored = new RecordingHousekeeper("journal", minimumRetentionDays: 90);

        await RunAsync(Settings(retentionDays: 365), floored);

        Assert.Equal(365, floored.LastCycle!.EffectiveRetentionDays);
    }

    [Fact]
    public async Task TheCeiling_ReachesTheHousekeeper()
    {
        var housekeeper = new RecordingHousekeeper("cache");

        await RunAsync(Settings(retentionDays: 30), housekeeper);

        Assert.Equal(
            HousekeepingProcessor.MaxRowsPerTaskPerCycle,
            housekeeper.LastCycle!.MaxRowsPerTask);
    }

    [Fact]
    public async Task TheSettingsAreRereadEveryCycle()
    {
        var housekeeper = new RecordingHousekeeper("cache");
        var settings = Settings(retentionDays: 30, dryRun: true);
        var configuration = new Mock<IConfigurationService>();
        configuration.Setup(service => service.GetApplicationSettingsAsync())
            .ReturnsAsync(() => settings);
        var processor = CreateProcessor(configuration.Object, housekeeper);

        await processor.RunCycleAsync(CancellationToken.None);
        Assert.True(housekeeper.LastCycle!.DryRun);

        // No restart, no new processor. An operator turning the preview off has to take effect
        // on the next cycle or the switch is not usable.
        settings = Settings(retentionDays: 30, dryRun: false);
        await processor.RunCycleAsync(CancellationToken.None);
        Assert.False(housekeeper.LastCycle!.DryRun);
    }

    [Fact]
    public async Task ASettingsReadThatThrows_KeepsTheOptionsAlreadyInUseRatherThanDeleting()
    {
        var housekeeper = new RecordingHousekeeper("cache");
        var configuration = new Mock<IConfigurationService>();
        configuration.Setup(service => service.GetApplicationSettingsAsync())
            .ThrowsAsync(new InvalidOperationException("the settings table is unreadable"));

        var processor = CreateProcessor(configuration.Object, housekeeper);
        await processor.RunCycleAsync(CancellationToken.None);

        // The shipped options, which preview rather than delete.
        Assert.Equal(1, housekeeper.Runs);
        Assert.True(housekeeper.LastCycle!.DryRun);
        Assert.Equal(HousekeepingOptions.Shipped.RetentionDays, housekeeper.LastCycle.EffectiveRetentionDays);
    }

    [Fact]
    public async Task AnAbsurdlyLongWindow_IsClampedAtUse()
    {
        var housekeeper = new RecordingHousekeeper("cache");

        await RunAsync(Settings(retentionDays: 9_999_999), housekeeper);

        // Ten years. Clamping at use rather than at save is deliberate: GET /settings echoes
        // back whatever was stored, so clamping on the way in would show the operator a number
        // they did not type. The control is the run above at 365, which is inside the bound and
        // comes through untouched.
        Assert.Equal(3650, housekeeper.LastCycle!.EffectiveRetentionDays);
    }

    private static ApplicationSettings Settings(int retentionDays, bool dryRun = true) =>
        new() { HousekeepingRetentionDays = retentionDays, HousekeepingDryRun = dryRun };

    private static Task RunAsync(ApplicationSettings settings, params IHousekeepingTask[] housekeepers)
    {
        var configuration = new Mock<IConfigurationService>();
        configuration.Setup(service => service.GetApplicationSettingsAsync()).ReturnsAsync(settings);
        return CreateProcessor(configuration.Object, housekeepers).RunCycleAsync(CancellationToken.None);
    }

    private static HousekeepingProcessor CreateProcessor(
        IConfigurationService configuration,
        params IHousekeepingTask[] housekeepers)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        var provider = services.BuildServiceProvider();

        return new HousekeepingProcessor(
            housekeepers,
            new HousekeepingOptionsHolder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedClock(Now),
            Mock.Of<ILogger<HousekeepingProcessor>>());
    }

    private sealed class RecordingHousekeeper(string name, int minimumRetentionDays = 0)
        : IHousekeepingTask
    {
        public string Name { get; } = name;

        public int MinimumRetentionDays { get; } = minimumRetentionDays;

        public int Runs { get; private set; }

        public HousekeepingCycle? LastCycle { get; private set; }

        public Task<HousekeepingTaskOutcome> RunAsync(
            HousekeepingCycle cycle,
            CancellationToken cancellationToken)
        {
            Runs++;
            LastCycle = cycle;
            return Task.FromResult(HousekeepingTaskOutcome.Nothing);
        }
    }

    private sealed class ThrowingHousekeeper(string name) : IHousekeepingTask
    {
        public string Name { get; } = name;

        public int MinimumRetentionDays => 0;

        public int Runs { get; private set; }

        public Task<HousekeepingTaskOutcome> RunAsync(
            HousekeepingCycle cycle,
            CancellationToken cancellationToken)
        {
            Runs++;
            throw new InvalidOperationException("this housekeeper cannot reach its table");
        }
    }
}
