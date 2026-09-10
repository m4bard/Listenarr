/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Metadata.Refresh;

/// <summary>
/// The budget is the only thing standing between a background walk and a rate-limited host,
/// so it is pinned against a clock the test owns rather than against wall time.
/// </summary>
[Trait("Area", "Metadata")]
[Trait("Name", "MetadataRefreshBudgetTests")]
[Trait("Category", "Application")]
public class MetadataRefreshBudgetTests : BaseTests
{
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan amount) => _now += amount;
    }

    private static (MetadataRefreshBudget Budget, ManualClock Clock) Create(
        MetadataRefreshBudgetOptions options)
    {
        var clock = new ManualClock();
        var budget = new MetadataRefreshBudget(
            clock,
            options,
            Mock.Of<ILogger<MetadataRefreshBudget>>(),
            (delay, _) =>
            {
                clock.Advance(delay);
                return Task.CompletedTask;
            });
        return (budget, clock);
    }

    [Fact]
    [Trait("Scenario", "SpacingFloorPreventsABurst")]
    public async Task ChargeAsync_KeepsTheSpacingFloor_EvenWithAFullBucket()
    {
        var (budget, clock) = Create(new MetadataRefreshBudgetOptions(
            RequestsPerHour: 60,
            MinimumSpacingMs: 1000));
        var grants = new List<DateTimeOffset>();

        for (var i = 0; i < 5; i++)
        {
            Assert.True(await budget.ChargeAsync(CancellationToken.None));
            grants.Add(clock.GetUtcNow());
        }

        Assert.Equal(5, budget.RequestsSpent);
        for (var i = 1; i < grants.Count; i++)
        {
            Assert.True(
                grants[i] - grants[i - 1] >= TimeSpan.FromSeconds(1),
                $"grant {i} came {grants[i] - grants[i - 1]} after the previous one");
        }
    }

    [Fact]
    [Trait("Scenario", "EmptyBucketWaitsForTheRefill")]
    public async Task ChargeAsync_WaitsForTheHourlyRefill_WhenTheBucketIsEmpty()
    {
        var (budget, clock) = Create(new MetadataRefreshBudgetOptions(
            RequestsPerHour: 60,
            MinimumSpacingMs: 0));

        for (var i = 0; i < 60; i++)
        {
            Assert.True(await budget.ChargeAsync(CancellationToken.None));
        }

        Assert.Equal(0, budget.RemainingThisHour);
        var beforeTheWait = clock.GetUtcNow();
        Assert.True(await budget.ChargeAsync(CancellationToken.None));

        // 60 per hour is one per minute once the bucket is dry.
        Assert.True(clock.GetUtcNow() - beforeTheWait >= TimeSpan.FromSeconds(59));
        Assert.Equal(61, budget.RequestsSpent);
    }

    [Fact]
    [Trait("Scenario", "RunWindowEndsTheRun")]
    public async Task ChargeAsync_ReturnsFalse_WhenTheNextSlotFallsOutsideTheRunWindow()
    {
        var (budget, _) = Create(new MetadataRefreshBudgetOptions(
            RequestsPerHour: 60,
            MinimumSpacingMs: 1000,
            RunWindow: TimeSpan.FromSeconds(5)));
        var granted = 0;

        while (await budget.ChargeAsync(CancellationToken.None))
        {
            granted++;
            Assert.True(granted < 100, "the run window never closed");
        }

        Assert.InRange(granted, 5, 7);
        Assert.True(budget.WindowClosed);
    }

    [Fact]
    [Trait("Scenario", "PushbackHalvesTheRefillRate")]
    public async Task ApplyThrottleSignal_HalvesWhatRemainsOfTheRun()
    {
        var (budget, clock) = Create(new MetadataRefreshBudgetOptions(
            RequestsPerHour: 60,
            MinimumSpacingMs: 0));

        for (var i = 0; i < 60; i++)
        {
            Assert.True(await budget.ChargeAsync(CancellationToken.None));
        }

        budget.ApplyThrottleSignal(retryAfter: null);

        var beforeTheWait = clock.GetUtcNow();
        Assert.True(await budget.ChargeAsync(CancellationToken.None));

        // Halved to 30 per hour, so a token now takes two minutes rather than one.
        Assert.True(clock.GetUtcNow() - beforeTheWait >= TimeSpan.FromSeconds(119));
    }

    [Fact]
    [Trait("Scenario", "RetryAfterIsHonoured")]
    public async Task ApplyThrottleSignal_WaitsOutRetryAfter_BeforeTheNextGrant()
    {
        var (budget, clock) = Create(new MetadataRefreshBudgetOptions(
            RequestsPerHour: 3600,
            MinimumSpacingMs: 0));

        Assert.True(await budget.ChargeAsync(CancellationToken.None));
        budget.ApplyThrottleSignal(TimeSpan.FromSeconds(30));

        var beforeTheWait = clock.GetUtcNow();
        Assert.True(await budget.ChargeAsync(CancellationToken.None));

        Assert.True(clock.GetUtcNow() - beforeTheWait >= TimeSpan.FromSeconds(30));
    }

    [Fact]
    [Trait("Scenario", "CancellationInterruptsTheWait")]
    public async Task ChargeAsync_Throws_WhenTheRunIsCancelledWhileWaiting()
    {
        var (budget, _) = Create(new MetadataRefreshBudgetOptions(
            RequestsPerHour: 1,
            MinimumSpacingMs: 0));
        Assert.True(await budget.ChargeAsync(CancellationToken.None));

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => budget.ChargeAsync(cancellation.Token));
    }
}
