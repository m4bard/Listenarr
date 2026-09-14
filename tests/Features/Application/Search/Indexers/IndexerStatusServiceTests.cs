/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Search.Indexers;

/// <summary>
/// The escalation ladder, rung by rung. The rung values are asserted explicitly rather than only
/// for monotonicity: a ladder that climbs to the wrong rung passes every "it went up" check there
/// is, and the cooldown an operator actually experiences is the rung value, not its direction.
/// </summary>
[Trait("Area", "Search")]
[Trait("Name", "IndexerStatusServiceTests")]
[Trait("Category", "IndexerStatusService")]
public sealed class IndexerStatusServiceTests : BaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "FirstFailure")]
    public async Task RecordAsync_FirstFailure_LandsOnRungOneNotRungTwo()
    {
        // Given
        var harness = new Harness();
        var indexer = Indexer();

        // When
        var state = await harness.Service.RecordAsync(indexer, Timeout());

        // Then: one strike sets rung 1 and a sixty-second block. Landing on rung 2 here is the
        // off-by-one that doubles every subsequent cooldown for the life of the failure run.
        Assert.Equal(1, state.EscalationLevel);
        AssertBlockedFor(state, TimeSpan.FromSeconds(60));
        Assert.Equal(Now.UtcDateTime, state.InitialFailure);
        Assert.Equal(Now.UtcDateTime, state.MostRecentFailure);
        Assert.Equal(nameof(IndexerQueryReason.Timeout), state.LastFailureReason);
    }

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "RepeatedFailure")]
    public async Task RecordAsync_ConsecutiveFailures_ClimbTheLadderOneRungAtATime()
    {
        // Given
        var harness = new Harness();
        var indexer = Indexer();
        TimeSpan[] expected =
        [
            TimeSpan.FromSeconds(60),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(3),
            TimeSpan.FromHours(6),
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(24)
        ];

        // When / Then
        for (var rung = 1; rung <= expected.Length; rung++)
        {
            // Past the block each time, so the fan-out would genuinely have asked again.
            harness.Advance(TimeSpan.FromHours(30));
            var state = await harness.Service.RecordAsync(indexer, Timeout());
            Apply(indexer, state);

            Assert.Equal(rung, state.EscalationLevel);
            AssertBlockedFor(state, expected[rung - 1], harness.UtcNow);
        }
    }

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "Ceiling")]
    public async Task RecordAsync_FailuresBeyondTheTopRung_StayAtTwentyFourHours()
    {
        // Given
        var harness = new Harness();
        var indexer = Indexer(level: IndexerStatusService.MaxLevel);

        // When: well past the end of the array, which is where an unguarded increment throws
        for (var i = 0; i < 5; i++)
        {
            harness.Advance(TimeSpan.FromDays(2));
            Apply(indexer, await harness.Service.RecordAsync(indexer, Timeout()));
        }

        // Then
        Assert.Equal(IndexerStatusService.MaxLevel, indexer.EscalationLevel);
        AssertBlockedFor(IndexerBackoffState.From(indexer), TimeSpan.FromHours(24), harness.UtcNow);
    }

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "SuccessDecrements")]
    public async Task RecordAsync_Success_DecrementsOneRungRatherThanResetting()
    {
        // Given: rung 6, the three-hour cooldown
        var harness = new Harness();
        var indexer = Indexer(level: 6, disabledTill: Now.UtcDateTime.AddHours(3));

        // When
        var state = await harness.Service.RecordAsync(indexer, Hit());

        // Then: if this ever becomes a reset to zero, this is the test that says so, and the change
        // should then be deliberate. A flapping indexer never accumulates under a reset.
        Assert.Equal(5, state.EscalationLevel);
        Assert.Null(state.DisabledTill);
    }

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "SuccessAtRungOne")]
    public async Task RecordAsync_SuccessFromRungOne_ClearsTheRunEntirely()
    {
        // Given
        var harness = new Harness();
        var indexer = Indexer(level: 1, initialFailure: Now.UtcDateTime.AddMinutes(-5), disabledTill: Now.UtcDateTime.AddSeconds(30));

        // When
        var state = await harness.Service.RecordAsync(indexer, Hit());

        // Then
        Assert.Equal(0, state.EscalationLevel);
        Assert.Null(state.DisabledTill);
        Assert.Null(state.InitialFailure);
        Assert.Null(state.LastFailureReason);
    }

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "HealthySuccess")]
    public async Task RecordAsync_SuccessOnAHealthyIndexer_WritesNothing()
    {
        // Given: the overwhelmingly common case. A sweep searches every monitored book in turn, so
        // a write per healthy answer would be a write per book per indexer.
        var harness = new Harness();

        // When
        var state = await harness.Service.RecordAsync(Indexer(), Hit());

        // Then
        Assert.Equal(IndexerBackoffState.Healthy, state);
        Assert.Empty(harness.Writes);
    }

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "NoMatchIsSuccess")]
    public async Task RecordAsync_IndexerAnsweredWithNothing_IsTreatedAsSuccess()
    {
        // Given: the control for the whole feature. An indexer saying it does not have the book is
        // the indexer working, and a breaker that trips on that mutes a healthy install.
        var harness = new Harness();
        var indexer = Indexer(level: 4, disabledTill: Now.UtcDateTime.AddMinutes(30));

        // When
        var state = await harness.Service.RecordAsync(
            indexer,
            IndexerQueryObservation.FromResults([], "Alice"));

        // Then
        Assert.Equal(3, state.EscalationLevel);
        Assert.Null(state.DisabledTill);
    }

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "RateLimitedWithRetryAfter")]
    public async Task RecordAsync_FirstRateLimitWithRetryAfter_JumpsStraightToTheMatchingRung()
    {
        // Given: a healthy indexer answering its very first 429
        var harness = new Harness();
        var indexer = Indexer();

        // When
        var state = await harness.Service.RecordAsync(indexer, RateLimited(TimeSpan.FromHours(1)));

        // Then: the one-hour rung on the first strike. Climbing one rung at a time here would mean
        // the header was parsed and then ignored, and we would come back five times inside the
        // window the remote just asked us to stay out of.
        Assert.Equal(5, state.EscalationLevel);
        AssertBlockedFor(state, TimeSpan.FromHours(1));
    }

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "RateLimitedWithoutRetryAfter")]
    public async Task RecordAsync_RateLimitWithNoRetryAfter_EscalatesOneRung()
    {
        // Given
        var harness = new Harness();
        var indexer = Indexer(level: 2);

        // When
        var state = await harness.Service.RecordAsync(indexer, RateLimited(null));

        // Then: no crash, no zero-length block
        Assert.Equal(3, state.EscalationLevel);
        AssertBlockedFor(state, TimeSpan.FromMinutes(15));
    }

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "RateLimitedBelowCurrentRung")]
    public async Task RecordAsync_RateLimitShorterThanTheCurrentRung_DoesNotWalkBackDown()
    {
        // Given: rung 7, a six-hour cooldown, and a remote asking for one minute
        var harness = new Harness();
        var indexer = Indexer(level: 7);

        // When
        var state = await harness.Service.RecordAsync(indexer, RateLimited(TimeSpan.FromMinutes(1)));

        // Then: the jump loop only ever climbs. A stated delay shorter than where we already stand
        // is not evidence that the indexer recovered.
        Assert.Equal(8, state.EscalationLevel);
        AssertBlockedFor(state, TimeSpan.FromHours(12));
    }

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "TransportFailure")]
    public async Task RecordAsync_NetworkError_ReblocksAtTheSameRungWithoutClimbing()
    {
        // Given: rung 3
        var harness = new Harness();
        var indexer = Indexer(level: 3);

        // When
        var state = await harness.Service.RecordAsync(indexer, NetworkError());

        // Then: rung 3, not rung 4. The opposite polarity from the rate-limit case above, and the
        // pair is what pins it: either test alone passes on an implementation that treats every
        // failure identically.
        Assert.Equal(3, state.EscalationLevel);
        AssertBlockedFor(state, TimeSpan.FromMinutes(15));
    }

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "TransportFailureFromHealthy")]
    public async Task RecordAsync_NetworkErrorOnAHealthyIndexer_StillBlocksAtRungOne()
    {
        // Given: holding at rung 0 would mean never blocking a host that cannot be resolved at all
        var harness = new Harness();

        // When
        var state = await harness.Service.RecordAsync(Indexer(), NetworkError());

        // Then
        Assert.Equal(1, state.EscalationLevel);
        AssertBlockedFor(state, TimeSpan.FromSeconds(60));
    }

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "ProxyOutage")]
    public async Task RecordAsync_EveryIndexerBehindOneProxyFails_EachBlocksIndependentlyAndNoneClimbs()
    {
        // Given: the shape of a dropped VPN or a dead Jackett. Several indexers, one cause.
        var harness = new Harness();
        var first = Indexer(id: 1, level: 5);
        var second = Indexer(id: 2, level: 2);

        // When
        var firstState = await harness.Service.RecordAsync(first, NetworkError());
        var secondState = await harness.Service.RecordAsync(second, NetworkError());

        // Then
        Assert.Equal(5, firstState.EscalationLevel);
        Assert.Equal(2, secondState.EscalationLevel);
        Assert.Equal([1, 2], harness.Writes.Select(w => w.IndexerId).Order());
    }

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "NoCrossIndexerEscalation")]
    public async Task RecordAsync_OneIndexerFailing_LeavesAHealthySiblingAlone()
    {
        // Given
        var harness = new Harness();
        var failing = Indexer(id: 1);
        var healthy = Indexer(id: 2);

        // When
        for (var i = 0; i < 4; i++)
        {
            harness.Advance(TimeSpan.FromHours(1));
            Apply(failing, await harness.Service.RecordAsync(failing, Timeout()));
            Apply(healthy, await harness.Service.RecordAsync(healthy, Hit()));
        }

        // Then
        Assert.Equal(4, failing.EscalationLevel);
        Assert.Equal(0, healthy.EscalationLevel);
        Assert.Null(healthy.DisabledTill);
        Assert.DoesNotContain(harness.Writes, w => w.IndexerId == 2);
    }

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "StartupGrace")]
    public async Task RecordAsync_WithinTheStartupWindow_CapsTheBlockAtTheFiveMinuteRung()
    {
        // Given: a restart into a network that is not up yet, on an indexer already high on the
        // ladder. Without the cap, one unlucky restart buries the install for hours, and the state
        // is persisted so the next restart does not clear it either.
        var harness = new Harness(startedAgo: TimeSpan.FromMinutes(1));
        var indexer = Indexer(level: 7);

        // When
        var state = await harness.Service.RecordAsync(indexer, Timeout());

        // Then
        Assert.Equal(8, state.EscalationLevel);
        AssertBlockedFor(state, TimeSpan.FromMinutes(5));
    }

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "StartupGraceExpired")]
    public async Task RecordAsync_AfterTheStartupWindow_AppliesTheFullRung()
    {
        // Given: the control for the test above. Without it, a cap that never lifted would pass.
        var harness = new Harness(startedAgo: IndexerBackoffStartupWindow.Duration + TimeSpan.FromMinutes(1));
        var indexer = Indexer(level: 7);

        // When
        var state = await harness.Service.RecordAsync(indexer, Timeout());

        // Then
        Assert.Equal(8, state.EscalationLevel);
        AssertBlockedFor(state, TimeSpan.FromHours(12));
    }

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "StartupGraceVersusStatedDelay")]
    public async Task RecordAsync_WithinTheStartupWindow_StillHonoursAStatedRetryAfter()
    {
        // Given
        var harness = new Harness(startedAgo: TimeSpan.FromMinutes(1));
        var indexer = Indexer();

        // When
        var state = await harness.Service.RecordAsync(indexer, RateLimited(TimeSpan.FromHours(3)));

        // Then: the cap exists to stop us punishing an indexer for our own cold start. It is not a
        // licence to come back inside a window the remote explicitly asked us to stay out of.
        Assert.Equal(6, state.EscalationLevel);
        AssertBlockedFor(state, TimeSpan.FromHours(3));
    }

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "InitialFailureSetOnce")]
    public async Task RecordAsync_RepeatedFailures_KeepTheFirstTimestampAndMoveTheLatest()
    {
        // Given
        var harness = new Harness();
        var indexer = Indexer();

        // When
        Apply(indexer, await harness.Service.RecordAsync(indexer, Timeout()));
        harness.Advance(TimeSpan.FromHours(1));
        Apply(indexer, await harness.Service.RecordAsync(indexer, Timeout()));

        // Then: getting these two backwards is the single most likely silent defect in the feature.
        // "Has been failing for more than six hours" is a different and louder condition than
        // "is failing now", and it is uncomputable if both fields track the latest failure.
        Assert.Equal(Now.UtcDateTime, indexer.InitialFailure);
        Assert.Equal(Now.UtcDateTime.AddHours(1), indexer.MostRecentFailure);
    }

    [Theory]
    [InlineData(IndexerQueryOutcome.NotConfigured, IndexerQueryReason.NoProviderForImplementation)]
    [InlineData(IndexerQueryOutcome.Unavailable, IndexerQueryReason.Cancelled)]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "NothingObserved")]
    public async Task RecordAsync_NoRequestReachedTheIndexer_LeavesTheRungAlone(
        IndexerQueryOutcome outcome,
        IndexerQueryReason reason)
    {
        // Given
        var harness = new Harness();
        var indexer = Indexer(level: 4, disabledTill: Now.UtcDateTime.AddMinutes(30));

        // When
        var state = await harness.Service.RecordAsync(
            indexer,
            new IndexerQueryObservation(outcome, reason, [], 1, "Alice"));

        // Then
        Assert.Equal(4, state.EscalationLevel);
        Assert.Empty(harness.Writes);
    }

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "BroadcastOnTransition")]
    public async Task RecordAsync_BlockingAnIndexer_NudgesTheSettingsViewToRefresh()
    {
        // Given
        var harness = new Harness();

        // When
        await harness.Service.RecordAsync(Indexer(), Timeout());

        // Then: a mechanism that silently mutes indexers reproduces the defect that let the
        // original incident run a full day with nobody watching.
        harness.Broadcaster.Verify(
            b => b.BroadcastAsync(
                RealtimeHubTarget.Settings,
                "IndexersUpdated",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "NoBroadcastWithoutATransition")]
    public async Task RecordAsync_HealthyIndexerAnswering_BroadcastsNothing()
    {
        // Given: the control. A broadcast per healthy answer would be one per book per indexer
        // across a whole sweep.
        var harness = new Harness();

        // When
        await harness.Service.RecordAsync(Indexer(), Hit());

        // Then
        harness.Broadcaster.Verify(
            b => b.BroadcastAsync(
                It.IsAny<RealtimeHubTarget>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    [Trait("Method", "RecordAsync")]
    [Trait("Scenario", "BroadcastFails")]
    public async Task RecordAsync_BroadcastThrows_StillReturnsTheNewState()
    {
        // Given
        var harness = new Harness();
        harness.Broadcaster
            .Setup(b => b.BroadcastAsync(
                It.IsAny<RealtimeHubTarget>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no clients"));

        // When
        var state = await harness.Service.RecordAsync(Indexer(), Timeout());

        // Then
        Assert.Equal(1, state.EscalationLevel);
    }

    [Fact]
    [Trait("Method", "GetBlockedIndexerIdsAsync")]
    [Trait("Scenario", "ExpiryBoundary")]
    public async Task GetBlockedIndexerIdsAsync_ReturnsOnlyIndexersWhoseCooldownHasNotExpired()
    {
        // Given
        var harness = new Harness();
        harness.Indexers.Add(Indexer(id: 1, level: 3, disabledTill: Now.UtcDateTime.AddMinutes(10)));
        harness.Indexers.Add(Indexer(id: 2, level: 3, disabledTill: Now.UtcDateTime.AddMinutes(-1)));
        harness.Indexers.Add(Indexer(id: 3));

        // When
        var blocked = await harness.Service.GetBlockedIndexerIdsAsync();

        // Then
        Assert.Equal([1], blocked.Order());
    }

    [Fact]
    [Trait("Method", "GetBlockedIndexerIdsAsync")]
    [Trait("Scenario", "HealthyInstall")]
    public async Task GetBlockedIndexerIdsAsync_NothingBlocked_IsEmpty()
    {
        var harness = new Harness();
        harness.Indexers.Add(Indexer(id: 1));
        harness.Indexers.Add(Indexer(id: 2));

        Assert.Empty(await harness.Service.GetBlockedIndexerIdsAsync());
    }

    private static void AssertBlockedFor(IndexerBackoffState state, TimeSpan rung) =>
        AssertBlockedFor(state, rung, Now);

    private static void AssertBlockedFor(IndexerBackoffState state, TimeSpan rung, DateTimeOffset asOf)
    {
        Assert.NotNull(state.DisabledTill);
        var actual = state.DisabledTill!.Value - asOf.UtcDateTime;

        // The jitter is one-sided, so the block is never shorter than the rung. That matters most
        // for a cooldown derived from a stated Retry-After, where coming back early is the one thing
        // the remote asked us not to do.
        Assert.InRange(actual, rung, rung + IndexerStatusService.MaxJitter);
    }

    private static void Apply(Indexer indexer, IndexerBackoffState state)
    {
        indexer.InitialFailure = state.InitialFailure;
        indexer.MostRecentFailure = state.MostRecentFailure;
        indexer.EscalationLevel = state.EscalationLevel;
        indexer.DisabledTill = state.DisabledTill;
        indexer.LastFailureReason = state.LastFailureReason;
    }

    private static IndexerQueryObservation Hit() =>
        IndexerQueryObservation.FromResults([new IndexerSearchResult { Title = "Alice" }], "Alice");

    private static IndexerQueryObservation Timeout() =>
        IndexerQueryObservation.Unavailable(IndexerQueryReason.Timeout, "Alice", "TaskCanceledException");

    private static IndexerQueryObservation NetworkError() =>
        IndexerQueryObservation.Unavailable(IndexerQueryReason.NetworkError, "Alice", "HttpRequestException");

    private static IndexerQueryObservation RateLimited(TimeSpan? retryAfter) =>
        IndexerQueryObservation.Unavailable(IndexerQueryReason.RateLimited, "Alice", "429", retryAfter: retryAfter);

    private static Indexer Indexer(
        int id = 1,
        int level = 0,
        DateTime? initialFailure = null,
        DateTime? disabledTill = null)
    {
        var indexer = new IndexerBuilder()
            .WithId(id)
            .WithName($"Indexer {id}")
            .WithImplementation("Torznab")
            .Build();

        indexer.EscalationLevel = level;
        indexer.InitialFailure = initialFailure ?? (level > 0 ? Now.UtcDateTime.AddDays(-1) : null);
        indexer.MostRecentFailure = level > 0 ? Now.UtcDateTime.AddMinutes(-1) : null;
        indexer.DisabledTill = disabledTill;
        indexer.LastFailureReason = level > 0 ? nameof(IndexerQueryReason.Timeout) : null;
        return indexer;
    }

    private sealed class Harness
    {
        /// <param name="startedAgo">
        /// How long before the test's "now" the process is taken to have started. Defaults to past
        /// the startup window, because the cap on that window is otherwise silently in force for
        /// every test that reaches a rung longer than five minutes.
        /// </param>
        public Harness(TimeSpan? startedAgo = null)
        {
            Clock = new MutableTimeProvider(Now);
            Repository = new RecordingIndexerRepository(Indexers, Writes);
            var startedAt = Now - (startedAgo ?? IndexerBackoffStartupWindow.Duration + TimeSpan.FromMinutes(1));
            Service = new IndexerStatusService(
                Repository,
                new IndexerBackoffStartupWindow(new MutableTimeProvider(startedAt)),
                Clock,
                NullLogger<IndexerStatusService>.Instance,
                Broadcaster.Object);
        }

        public Mock<IHubBroadcaster> Broadcaster { get; } = new();

        public MutableTimeProvider Clock { get; }

        public List<Indexer> Indexers { get; } = [];

        public List<(int IndexerId, IndexerBackoffState State)> Writes { get; } = [];

        public RecordingIndexerRepository Repository { get; }

        public IndexerStatusService Service { get; }

        public DateTimeOffset UtcNow => Clock.GetUtcNow();

        public void Advance(TimeSpan duration) => Clock.Advance(duration);
    }

    private sealed class RecordingIndexerRepository(
        List<Indexer> indexers,
        List<(int IndexerId, IndexerBackoffState State)> writes) : IIndexerRepository
    {
        public Task<Indexer?> GetByIdAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(indexers.FirstOrDefault(i => i.Id == id));

        public Task<Indexer?> GetByNameAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(indexers.FirstOrDefault(i => i.Name == name));

        public Task<List<Indexer>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult(indexers.ToList());

        public Task<List<Indexer>> GetEnabledAsync(bool isAutomaticSearch, CancellationToken ct = default) =>
            Task.FromResult(indexers.Where(i => i.IsEnabled).ToList());

        public Task<Indexer> AddAsync(Indexer indexer, CancellationToken ct = default)
        {
            indexers.Add(indexer);
            return Task.FromResult(indexer);
        }

        public Task UpdateAsync(Indexer indexer, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteAsync(int id, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateBackoffStateAsync(int indexerId, IndexerBackoffState state, CancellationToken ct = default)
        {
            writes.Add((indexerId, state));
            return Task.CompletedTask;
        }
    }
}
