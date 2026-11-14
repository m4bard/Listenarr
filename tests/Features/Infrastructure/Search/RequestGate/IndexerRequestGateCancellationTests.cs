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

using System.Net;
using Listenarr.Tests.Common;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestGate.IndexerRequestGateHarness;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestGate;

[Trait("Name", "IndexerRequestGateCancellationTests")]
[Trait("Category", "Infrastructure")]
public sealed class IndexerRequestGateCancellationTests : BaseTests
{
    private const int IndexerId = 7;
    private static readonly TimeSpan BoundedWait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task SendAsync_CallerCancelsWhileInFlight_RecordsNothing()
    {
        // Given: more caller cancellations than the failure threshold, then an answering stub.
        var cancellations = IndexerRequestPolicy.FailureThreshold + 1;
        using var entered = new SemaphoreSlim(0);
        var calls = 0;
        var store = new RecordingStateStore();
        using var harness = new IndexerRequestGateHarness(
            store,
            async (_, cancellationToken) =>
            {
                if (Interlocked.Increment(ref calls) > cancellations)
                {
                    return Status(HttpStatusCode.OK);
                }

                entered.Release();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return Status(HttpStatusCode.OK);
            });

        // When
        for (var i = 0; i < cancellations; i++)
        {
            using var caller = new CancellationTokenSource();
            var request = harness.SendAsync(IndexerId, caller.Token);
            Assert.True(await entered.WaitAsync(BoundedWait));
            await caller.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(BoundedWait));
        }

        var afterCancellations = await harness.SendAsync(IndexerId);

        // Then
        Assert.Equal(0, store.Writes);
        Assert.Null(store.Row(IndexerId));
        using var response = Assert.IsType<IndexerRequestResult.Sent>(afterCancellations).Response;
        Assert.Equal(cancellations + 1, harness.Handler.Hits);
        Assert.Null(await harness.Gate.GetDeferralAsync(IndexerId, SearchOrigin.Sweep, CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_CallerCancelsWhileWaitingForSlot_RecordsNothingAndSendsNothing()
    {
        // Given
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingStateStore();
        using var harness = new IndexerRequestGateHarness(
            store,
            async (_, _) =>
            {
                firstEntered.TrySetResult();
                await releaseFirst.Task;
                return Status(HttpStatusCode.OK);
            },
            perIndexerCap: 1);
        using var waitingCaller = new CancellationTokenSource();

        // When
        var first = harness.SendAsync(IndexerId);
        await firstEntered.Task.WaitAsync(BoundedWait);
        var waiting = harness.SendAsync(IndexerId, waitingCaller.Token);
        var waitingBeforeCancel = waiting.IsCompleted;
        await waitingCaller.CancelAsync();
        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(BoundedWait));
        var writesAfterCancel = store.Writes;
        releaseFirst.SetResult();
        using var firstResponse = Assert.IsType<IndexerRequestResult.Sent>(await first.WaitAsync(BoundedWait)).Response;

        // Then
        Assert.False(waitingBeforeCancel);
        Assert.Equal(waitingCaller.Token, thrown.CancellationToken);
        Assert.Equal(0, writesAfterCancel);
        Assert.Equal(1, harness.Handler.Hits);
        Assert.Null(store.Row(IndexerId));
    }

    [Fact]
    public async Task SendAsync_RepeatedTimeoutsWithLiveCallerToken_EscalateToFailureLadderCooldown()
    {
        // Given: the client's own timeout fires while the caller's token stays live.
        var store = new RecordingStateStore();
        using var harness = new IndexerRequestGateHarness(
            store,
            async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return Status(HttpStatusCode.OK);
            },
            clientTimeout: TimeSpan.FromMilliseconds(100));

        // When
        for (var i = 0; i < IndexerRequestPolicy.FailureThreshold; i++)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.SendAsync(IndexerId, CancellationToken.None));
        }

        var afterTimeouts = await harness.SendAsync(IndexerId, CancellationToken.None);

        // Then: the third timeout was observed after two pacing intervals.
        var expectedUntil = (Start + (2 * Interval) + IndexerRequestPolicy.EscalationLadder[0]).UtcDateTime;
        Assert.Equal(
            new RequestDeferral(expectedUntil, RequestDeferralReason.FailureLadder),
            Assert.IsType<IndexerRequestResult.Deferred>(afterTimeouts).Deferral);
        Assert.Equal(IndexerRequestPolicy.FailureThreshold, harness.Handler.Hits);
        var row = Assert.IsType<IndexerRequestState>(store.Row(IndexerId));
        Assert.Equal(IndexerRequestPolicy.FailureThreshold, row.ConsecutiveFailures);
        Assert.Equal(expectedUntil, row.CooldownUntilUtc);
        Assert.Equal(nameof(RequestDeferralReason.FailureLadder), row.CooldownReason);
        Assert.Equal(IndexerRequestPolicy.FailureThreshold, store.Writes);
    }
}
