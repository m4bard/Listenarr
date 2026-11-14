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

using Listenarr.Infrastructure.Search.RequestGate;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestGate;

[Trait("Name", "IndexerConcurrencyLimiterTests")]
[Trait("Category", "Infrastructure")]
public sealed class IndexerConcurrencyLimiterTests : BaseTests
{
    private const int IndexerId = 7;
    private const int OtherIndexerId = 8;
    private const int ParallelRequests = 20;
    private static readonly TimeSpan BoundedWait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task AcquireAsync_TwentyParallelRequests_NeverExceedPerIndexerCap()
    {
        // Given
        const int perIndexerCap = 2;
        var limiter = new IndexerConcurrencyLimiter(perIndexerCap, globalCapacity: 8);
        var probe = new InFlightProbe();

        // When
        var requests = Enumerable.Range(0, ParallelRequests)
            .Select(_ => Task.Run(() => probe.RunAsync(limiter, IndexerId, SearchOrigin.Sweep)))
            .ToArray();
        var settled = probe.WaitUntilSettled(ParallelRequests, BoundedWait);
        var inFlightWhileQueued = probe.InFlight;
        probe.ReleaseHolders();
        await Task.WhenAll(requests).WaitAsync(BoundedWait);

        // Then
        Assert.True(settled, "Not every request held a lease or queued in time.");
        Assert.Equal(perIndexerCap, inFlightWhileQueued);
        Assert.Equal(perIndexerCap, probe.MaxInFlight);
        Assert.Equal(ParallelRequests, probe.Completed);
    }

    [Fact]
    public async Task AcquireAsync_TwentyParallelRequestsAcrossIndexers_NeverExceedGlobalCap()
    {
        // Given
        const int globalCap = 3;
        var limiter = new IndexerConcurrencyLimiter(perIndexerCapacity: 4, globalCap);
        var probe = new InFlightProbe();

        // When
        var requests = Enumerable.Range(0, ParallelRequests)
            .Select(i => Task.Run(() => probe.RunAsync(limiter, 1 + (i % 5), SearchOrigin.Sweep)))
            .ToArray();
        var settled = probe.WaitUntilSettled(ParallelRequests, BoundedWait);
        var inFlightWhileQueued = probe.InFlight;
        probe.ReleaseHolders();
        await Task.WhenAll(requests).WaitAsync(BoundedWait);

        // Then
        Assert.True(settled, "Not every request held a lease or queued in time.");
        Assert.Equal(globalCap, inFlightWhileQueued);
        Assert.Equal(globalCap, probe.MaxInFlight);
        Assert.Equal(ParallelRequests, probe.Completed);
    }

    [Fact]
    public async Task AcquireAsync_InteractiveQueuedBehindSweep_IsServedFirst()
    {
        // Given
        var limiter = new IndexerConcurrencyLimiter(perIndexerCapacity: 1, globalCapacity: 8);
        var holder = await limiter.AcquireAsync(IndexerId, SearchOrigin.Sweep, CancellationToken.None);
        var firstSweep = limiter.AcquireAsync(IndexerId, SearchOrigin.Sweep, CancellationToken.None);
        var secondSweep = limiter.AcquireAsync(IndexerId, SearchOrigin.Retry, CancellationToken.None);
        var interactive = limiter.AcquireAsync(IndexerId, SearchOrigin.Interactive, CancellationToken.None);

        // When
        holder.Dispose();
        var interactiveLease = await interactive.WaitAsync(BoundedWait);

        // Then
        Assert.False(firstSweep.IsCompleted);
        Assert.False(secondSweep.IsCompleted);

        interactiveLease.Dispose();
        var firstSweepLease = await firstSweep.WaitAsync(BoundedWait);
        Assert.False(secondSweep.IsCompleted);

        firstSweepLease.Dispose();
        (await secondSweep.WaitAsync(BoundedWait)).Dispose();
    }

    [Fact]
    public async Task AcquireAsync_InteractiveQueuedForGlobalSlot_IsServedBeforeOtherIndexersSweep()
    {
        // Given
        var limiter = new IndexerConcurrencyLimiter(perIndexerCapacity: 2, globalCapacity: 1);
        var holder = await limiter.AcquireAsync(IndexerId, SearchOrigin.Sweep, CancellationToken.None);
        var sweep = limiter.AcquireAsync(OtherIndexerId, SearchOrigin.Bulk, CancellationToken.None);
        var interactive = limiter.AcquireAsync(IndexerId, SearchOrigin.Interactive, CancellationToken.None);

        // When
        holder.Dispose();
        var interactiveLease = await interactive.WaitAsync(BoundedWait);

        // Then
        Assert.False(sweep.IsCompleted);
        interactiveLease.Dispose();
        (await sweep.WaitAsync(BoundedWait)).Dispose();
    }

    [Fact]
    public async Task AcquireAsync_CancelledWhileWaiting_ThrowsWithCallerTokenAndLeaksNoSlot()
    {
        // Given
        var limiter = new IndexerConcurrencyLimiter(perIndexerCapacity: 1, globalCapacity: 8);
        var holder = await limiter.AcquireAsync(IndexerId, SearchOrigin.Sweep, CancellationToken.None);
        using var callerCancellation = new CancellationTokenSource();
        var waiter = limiter.AcquireAsync(IndexerId, SearchOrigin.Sweep, callerCancellation.Token);

        // When
        callerCancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter.WaitAsync(BoundedWait));
        holder.Dispose();
        var next = limiter.AcquireAsync(IndexerId, SearchOrigin.Sweep, CancellationToken.None);

        // Then
        Assert.Equal(callerCancellation.Token, exception.CancellationToken);
        Assert.True(next.IsCompletedSuccessfully, "The slot given back after the cancelled wait was not free.");
        (await next).Dispose();
    }

    [Fact]
    public async Task AcquireAsync_CancelledWhileWaitingForGlobalSlot_ReturnsIndexerSlot()
    {
        // Given
        var limiter = new IndexerConcurrencyLimiter(perIndexerCapacity: 1, globalCapacity: 1);
        var otherIndexerHolder = await limiter.AcquireAsync(OtherIndexerId, SearchOrigin.Sweep, CancellationToken.None);
        using var callerCancellation = new CancellationTokenSource();
        var waiter = limiter.AcquireAsync(IndexerId, SearchOrigin.Sweep, callerCancellation.Token);

        // When
        callerCancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter.WaitAsync(BoundedWait));
        otherIndexerHolder.Dispose();
        var next = limiter.AcquireAsync(IndexerId, SearchOrigin.Sweep, CancellationToken.None);

        // Then
        Assert.Equal(callerCancellation.Token, exception.CancellationToken);
        Assert.True(next.IsCompletedSuccessfully, "The indexer slot taken before the cancelled global wait leaked.");
        (await next).Dispose();
    }

    [Fact]
    public async Task AcquireAsync_CancelledAfterSlotTaken_FollowingRequestTakesSlotWithoutWaiting()
    {
        // Given
        var limiter = new IndexerConcurrencyLimiter(perIndexerCapacity: 1, globalCapacity: 1);
        using var callerCancellation = new CancellationTokenSource();
        var lease = await limiter.AcquireAsync(IndexerId, SearchOrigin.Interactive, callerCancellation.Token);

        // When
        try
        {
            callerCancellation.Cancel();
        }
        finally
        {
            lease.Dispose();
            lease.Dispose();
        }

        var next = limiter.AcquireAsync(IndexerId, SearchOrigin.Sweep, CancellationToken.None);
        var afterNext = limiter.AcquireAsync(IndexerId, SearchOrigin.Sweep, CancellationToken.None);

        // Then
        Assert.True(next.IsCompletedSuccessfully, "The slot was not free after the lease was disposed.");
        Assert.False(afterNext.IsCompleted, "Disposing the lease twice gave back two slots.");
        (await next).Dispose();
        (await afterNext.WaitAsync(BoundedWait)).Dispose();
    }

    [Fact]
    public async Task AcquireAsync_AlreadyCancelledToken_TakesNoSlot()
    {
        // Given
        var limiter = new IndexerConcurrencyLimiter(perIndexerCapacity: 1, globalCapacity: 1);
        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();

        // When
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => limiter.AcquireAsync(IndexerId, SearchOrigin.Sweep, callerCancellation.Token));
        var next = limiter.AcquireAsync(IndexerId, SearchOrigin.Sweep, CancellationToken.None);

        // Then
        Assert.Equal(callerCancellation.Token, exception.CancellationToken);
        Assert.True(next.IsCompletedSuccessfully);
        (await next).Dispose();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AcquireAsync_UnattributedIndexer_TakesGlobalSlotOnly(int unattributedId)
    {
        // Given
        const int globalCap = 3;
        var limiter = new IndexerConcurrencyLimiter(perIndexerCapacity: 1, globalCap);

        // When
        var withinGlobalCap = Enumerable.Range(0, globalCap)
            .Select(_ => limiter.AcquireAsync(unattributedId, SearchOrigin.Interactive, CancellationToken.None))
            .ToArray();
        var overGlobalCap = limiter.AcquireAsync(unattributedId, SearchOrigin.Interactive, CancellationToken.None);

        // Then
        Assert.All(withinGlobalCap, request => Assert.True(request.IsCompletedSuccessfully));
        Assert.False(overGlobalCap.IsCompleted, "Unattributed requests were not held to the global cap.");

        (await withinGlobalCap[0]).Dispose();
        (await overGlobalCap.WaitAsync(BoundedWait)).Dispose();
        foreach (var request in withinGlobalCap.Skip(1))
        {
            (await request).Dispose();
        }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void Constructor_CapacityBelowOne_Throws(int perIndexerCapacity, int globalCapacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IndexerConcurrencyLimiter(perIndexerCapacity, globalCapacity));
    }

    /// <summary>
    /// Runs requests that hold their lease until released, counting how many hold one at once.
    /// A request is settled once it either holds its lease or found its acquire still pending,
    /// so when every request is settled the in-flight count is exactly what the limiter let in.
    /// </summary>
    private sealed class InFlightProbe
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _settled;
        private int _inFlight;
        private int _maxInFlight;
        private int _completed;

        public int InFlight => Volatile.Read(ref _inFlight);

        public int MaxInFlight => Volatile.Read(ref _maxInFlight);

        public int Completed => Volatile.Read(ref _completed);

        public async Task RunAsync(IndexerConcurrencyLimiter limiter, int indexerId, SearchOrigin origin)
        {
            var acquire = limiter.AcquireAsync(indexerId, origin, CancellationToken.None);
            var queued = !acquire.IsCompleted;
            if (queued)
            {
                Interlocked.Increment(ref _settled);
            }

            using (await acquire)
            {
                var inFlight = Interlocked.Increment(ref _inFlight);
                UpdateMax(inFlight);
                if (!queued)
                {
                    Interlocked.Increment(ref _settled);
                }

                await _release.Task;
                Interlocked.Decrement(ref _inFlight);
            }

            Interlocked.Increment(ref _completed);
        }

        public bool WaitUntilSettled(int requests, TimeSpan timeout)
            => SpinWait.SpinUntil(() => Volatile.Read(ref _settled) == requests, timeout);

        public void ReleaseHolders() => _release.TrySetResult();

        private void UpdateMax(int inFlight)
        {
            int current;
            while (inFlight > (current = Volatile.Read(ref _maxInFlight)))
            {
                Interlocked.CompareExchange(ref _maxInFlight, inFlight, current);
            }
        }
    }
}
