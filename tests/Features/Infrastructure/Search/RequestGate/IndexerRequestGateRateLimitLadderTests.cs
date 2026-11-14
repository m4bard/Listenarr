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
using System.Net.Http.Headers;
using Listenarr.Tests.Common;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestGate.IndexerRequestGateHarness;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestGate;

/// <summary>
/// A 429 without a usable Retry-After: the cooldown starts at the 1 hour ladder step and moves
/// one step for each rate limit seen while no rate-limit cooldown is in force. Responses to
/// requests that were already in flight when the cooldown was set are the same event and do not
/// move it.
/// </summary>
[Trait("Name", "IndexerRequestGateRateLimitLadderTests")]
[Trait("Category", "Infrastructure")]
public sealed class IndexerRequestGateRateLimitLadderTests : BaseTests
{
    private const int IndexerId = 7;
    private static readonly TimeSpan BoundedWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);
    private static readonly TimeSpan ThreeHours = TimeSpan.FromHours(3);

    [Fact]
    public async Task SendAsync_RateLimitedWithoutRetryAfter_CoolsDownForOneHour()
    {
        // Given
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            Sequence(() => RateLimited(null), () => Status(HttpStatusCode.OK)));

        // When
        var rateLimited = await harness.SendAsync(IndexerId);
        var next = await harness.SendAsync(IndexerId);

        // Then
        var expected = RateLimitedUntil(Start + OneHour);
        Assert.Equal(expected, Assert.IsType<IndexerRequestResult.Deferred>(rateLimited).Deferral);
        Assert.Equal(expected, Assert.IsType<IndexerRequestResult.Deferred>(next).Deferral);
        Assert.Equal(1, harness.Handler.Hits);
    }

    [Fact]
    public async Task SendAsync_RateLimitedAgainAfterCooldownEnds_EscalatesToThreeHours()
    {
        // Given
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            Sequence(() => RateLimited(null), () => RateLimited(null)));

        // When
        var first = await harness.SendAsync(IndexerId);
        harness.Clock.Advance(OneHour);
        var second = await harness.SendAsync(IndexerId);

        // Then
        Assert.Equal(RateLimitedUntil(Start + OneHour), Assert.IsType<IndexerRequestResult.Deferred>(first).Deferral);
        Assert.Equal(
            RateLimitedUntil(Start + OneHour + ThreeHours),
            Assert.IsType<IndexerRequestResult.Deferred>(second).Deferral);
        Assert.Equal(2, harness.Handler.Hits);
    }

    [Fact]
    public async Task SendAsync_SuccessBetweenRateLimits_ReturnsToOneHour()
    {
        // Given
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            Sequence(() => RateLimited(null), () => Status(HttpStatusCode.OK), () => RateLimited(null)));

        // When
        await harness.SendAsync(IndexerId);
        harness.Clock.Advance(OneHour);
        using var sent = Assert.IsType<IndexerRequestResult.Sent>(await harness.SendAsync(IndexerId)).Response;
        harness.Clock.Advance(Interval);
        var again = await harness.SendAsync(IndexerId);

        // Then
        Assert.Equal(
            RateLimitedUntil(Start + OneHour + Interval + OneHour),
            Assert.IsType<IndexerRequestResult.Deferred>(again).Deferral);
        Assert.Equal(3, harness.Handler.Hits);
    }

    /// <summary>
    /// Cap 2: both requests are on the wire when the first 429 arrives, the second a pacing
    /// interval after the first, and both are answered at that time.
    /// </summary>
    [Fact]
    public async Task SendAsync_TwoConcurrentRateLimitsWithoutRetryAfter_CoolDownForOneHourAndCountOnce()
    {
        // Given
        var inFlight = new InFlightResponses(() => RateLimited(null), () => RateLimited(null));
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            inFlight.RespondAsync,
            perIndexerCap: 2);

        // When
        var first = harness.SendAsync(IndexerId);
        var second = harness.SendAsync(IndexerId);
        await inFlight.BothEntered.Task.WaitAsync(BoundedWait);
        inFlight.ReleaseFirst.SetResult();
        inFlight.ReleaseSecond.SetResult();
        var firstResult = await first.WaitAsync(BoundedWait);
        var secondResult = await second.WaitAsync(BoundedWait);
        var peeked = await harness.Gate.GetDeferralAsync(IndexerId, SearchOrigin.Sweep, CancellationToken.None);
        harness.Clock.Advance(OneHour);
        var afterExpiry = await harness.SendAsync(IndexerId);

        // Then: one event, so one hour; the next rate limit after it ends is the second step.
        var answeredUtc = Start + Interval;
        var expected = RateLimitedUntil(answeredUtc + OneHour);
        Assert.Equal(expected, Assert.IsType<IndexerRequestResult.Deferred>(firstResult).Deferral);
        Assert.Equal(expected, Assert.IsType<IndexerRequestResult.Deferred>(secondResult).Deferral);
        Assert.Equal(expected, peeked);
        Assert.Equal(
            RateLimitedUntil(answeredUtc + OneHour + ThreeHours),
            Assert.IsType<IndexerRequestResult.Deferred>(afterExpiry).Deferral);
        Assert.Equal(3, harness.Handler.Hits);
    }

    /// <summary>
    /// Cap 2: the Retry-After answer is recorded first; the 429 without one, already in flight,
    /// is answered while its cooldown is in force.
    /// </summary>
    [Fact]
    public async Task SendAsync_RateLimitWithoutRetryAfterDuringRetryAfterCooldown_CoolsDownForOneHour()
    {
        // Given
        var inFlight = new InFlightResponses(
            () => RateLimited(new RetryConditionHeaderValue(TimeSpan.FromSeconds(60))),
            () => RateLimited(null));
        using var harness = new IndexerRequestGateHarness(
            new RecordingStateStore(),
            inFlight.RespondAsync,
            perIndexerCap: 2);

        // When
        var first = harness.SendAsync(IndexerId);
        var second = harness.SendAsync(IndexerId);
        await inFlight.BothEntered.Task.WaitAsync(BoundedWait);
        inFlight.ReleaseFirst.SetResult();
        var firstResult = await first.WaitAsync(BoundedWait);
        inFlight.ReleaseSecond.SetResult();
        var secondResult = await second.WaitAsync(BoundedWait);

        // Then
        var answeredUtc = Start + Interval;
        Assert.Equal(
            RateLimitedUntil(answeredUtc + TimeSpan.FromSeconds(60)),
            Assert.IsType<IndexerRequestResult.Deferred>(firstResult).Deferral);
        var expected = RateLimitedUntil(answeredUtc + OneHour);
        Assert.Equal(expected, Assert.IsType<IndexerRequestResult.Deferred>(secondResult).Deferral);
        Assert.Equal(expected, await harness.Gate.GetDeferralAsync(IndexerId, SearchOrigin.Sweep, CancellationToken.None));
        Assert.Equal(2, harness.Handler.Hits);
    }

    private static RequestDeferral RateLimitedUntil(DateTimeOffset untilUtc)
        => new(untilUtc.UtcDateTime, RequestDeferralReason.RateLimited);

    /// <summary>
    /// Holds the first two requests on the wire until each is released, then answers them with
    /// the given responses in arrival order.
    /// </summary>
    private sealed class InFlightResponses(Func<HttpResponseMessage> firstResponse, Func<HttpResponseMessage> secondResponse)
    {
        private int _calls;

        public TaskCompletionSource BothEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSecond { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            switch (Interlocked.Increment(ref _calls))
            {
                case 1:
                    await ReleaseFirst.Task;
                    return firstResponse();
                case 2:
                    BothEntered.SetResult();
                    await ReleaseSecond.Task;
                    return secondResponse();
                default:
                    return RateLimited(null);
            }
        }
    }
}
