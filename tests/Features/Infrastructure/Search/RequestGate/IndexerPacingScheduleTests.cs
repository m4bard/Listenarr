/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System.Collections.Concurrent;
using Listenarr.Infrastructure.Search.RequestGate;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestGate;

[Trait("Name", "IndexerPacingScheduleTests")]
[Trait("Category", "Infrastructure")]
public sealed class IndexerPacingScheduleTests : BaseTests
{
    private const int IndexerId = 7;
    private const int OtherIndexerId = 8;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private static readonly DateTimeOffset Now = new(2026, 6, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WaitForStartAsync_ConsecutiveDispatches_StartAtLeastIntervalApart()
    {
        // Given
        var clock = new ManualClock(Now);
        var schedule = new IndexerPacingSchedule(Interval, clock, clock.AdvanceDelayAsync);
        var starts = new List<DateTimeOffset>();

        // When
        for (var i = 0; i < 5; i++)
        {
            await schedule.WaitForStartAsync(IndexerId, CancellationToken.None);
            starts.Add(clock.GetUtcNow());
            clock.Advance(TimeSpan.FromMilliseconds(300));
        }

        // Then
        Assert.Equal(Now, starts[0]);
        AssertAtLeastIntervalApart(starts);
    }

    [Fact]
    public async Task WaitForStartAsync_ParallelCallers_ReserveDistinctStartsIntervalApart()
    {
        // Given
        const int callers = 20;
        var clock = new ManualClock(Now);
        var waits = new ConcurrentBag<TimeSpan>();
        var schedule = new IndexerPacingSchedule(
            Interval,
            clock,
            (wait, _) =>
            {
                waits.Add(wait);
                return Task.CompletedTask;
            });
        using var start = new ManualResetEventSlim(false);

        // When
        var reservations = Enumerable.Range(0, callers)
            .Select(_ => Task.Run(async () =>
            {
                start.Wait();
                await schedule.WaitForStartAsync(IndexerId, CancellationToken.None);
            }))
            .ToArray();
        start.Set();
        await Task.WhenAll(reservations).WaitAsync(TimeSpan.FromSeconds(10));

        // Then
        // The clock never moves, so each caller's start is now plus the wait it was given; the
        // one caller that was not delayed starts now.
        var starts = waits.Select(wait => Now + wait).Append(Now).Order().ToList();
        Assert.Equal(callers, starts.Count);
        AssertAtLeastIntervalApart(starts);
    }

    [Fact]
    public async Task WaitForStartAsync_IntervalAlreadyElapsed_StartsWithoutWaiting()
    {
        // Given
        var clock = new ManualClock(Now);
        var schedule = new IndexerPacingSchedule(Interval, clock, clock.AdvanceDelayAsync);
        await schedule.WaitForStartAsync(IndexerId, CancellationToken.None);
        clock.Advance(Interval + TimeSpan.FromSeconds(1));

        // When
        await schedule.WaitForStartAsync(IndexerId, CancellationToken.None);

        // Then
        Assert.Empty(clock.Delays);
    }

    [Fact]
    public async Task WaitForStartAsync_DifferentIndexers_ArePacedIndependently()
    {
        // Given
        var clock = new ManualClock(Now);
        var schedule = new IndexerPacingSchedule(Interval, clock, clock.AdvanceDelayAsync);

        // When
        await schedule.WaitForStartAsync(IndexerId, CancellationToken.None);
        await schedule.WaitForStartAsync(OtherIndexerId, CancellationToken.None);

        // Then
        Assert.Empty(clock.Delays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task WaitForStartAsync_UnattributedIndexer_IsNotPaced(int unattributedId)
    {
        // Given
        var clock = new ManualClock(Now);
        var schedule = new IndexerPacingSchedule(Interval, clock, clock.AdvanceDelayAsync);

        // When
        await schedule.WaitForStartAsync(unattributedId, CancellationToken.None);
        await schedule.WaitForStartAsync(unattributedId, CancellationToken.None);
        await schedule.WaitForStartAsync(unattributedId, CancellationToken.None);

        // Then
        Assert.Empty(clock.Delays);
    }

    [Fact]
    public async Task WaitForStartAsync_CancelledWhileWaiting_ThrowsWithCallerToken()
    {
        // Given
        var clock = new ManualClock(Now);
        var schedule = new IndexerPacingSchedule(
            Interval,
            clock,
            (_, cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
        await schedule.WaitForStartAsync(IndexerId, CancellationToken.None);
        using var callerCancellation = new CancellationTokenSource();
        var waiter = schedule.WaitForStartAsync(IndexerId, callerCancellation.Token);

        // When
        callerCancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => waiter.WaitAsync(TimeSpan.FromSeconds(10)));

        // Then
        Assert.Equal(callerCancellation.Token, exception.CancellationToken);
        Assert.True(exception.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void Constructor_NegativeInterval_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new IndexerPacingSchedule(TimeSpan.FromSeconds(-1), TimeProvider.System));
    }

    private static void AssertAtLeastIntervalApart(IReadOnlyList<DateTimeOffset> starts)
    {
        var gaps = starts.Zip(starts.Skip(1), (earlier, later) => later - earlier).ToList();
        Assert.All(gaps, gap => Assert.True(gap >= Interval, $"Dispatches started {gap} apart, under {Interval}."));
    }

    /// <summary>
    /// A clock that moves only when told to. Its delay records the wait and moves the clock by
    /// it, so a paced caller resumes exactly at its reserved start.
    /// </summary>
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualClock(DateTimeOffset now)
        {
            _now = now;
        }

        public List<TimeSpan> Delays { get; } = new();

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;

        public Task AdvanceDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            Advance(delay);
            return Task.CompletedTask;
        }
    }
}
