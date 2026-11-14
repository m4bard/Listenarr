using Listenarr.Application.Search.Discovery;
using Listenarr.Domain.Search.Discovery;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// Reading, transitioning, pruning and reporting on the discovery inbox. Runs on a SQLite file
/// database migrated by the real chain.
/// </summary>
[Trait("Area", "Persistence")]
[Trait("Name", "EfDiscoveryStoreInboxTests")]
[Trait("Category", "Infrastructure")]
public sealed class EfDiscoveryStoreInboxTests : BaseTests
{
    private static readonly DateTime NowUtc = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetBatchAsync_ReturnsOneStateOldestFirstThenByIdWithinTheLimit()
    {
        var database = await DiscoveryStoreTestDatabase.CreateAsync(FileService.GetTempPath());
        var store = database.CreateStore();
        // The later poll is committed first, so first-seen order and id order disagree.
        await CommitAsync(store, database.IndexerIds[0], NowUtc, "later-a", "later-b", "later-matched");
        await CommitAsync(store, database.IndexerIds[0], NowUtc.AddHours(-1), "earlier");
        var matched = await FindAsync(database, "later-matched");
        Assert.True(await store.TryTransitionAsync(
            matched.Id, DiscoveredReleaseState.Pending, DiscoveredReleaseState.Matched, null, null, NowUtc, CancellationToken.None));

        var firstTwo = await store.GetBatchAsync(DiscoveredReleaseState.Pending, 2, CancellationToken.None);
        var allPending = await store.GetBatchAsync(DiscoveredReleaseState.Pending, 10, CancellationToken.None);
        var allMatched = await store.GetBatchAsync(DiscoveredReleaseState.Matched, 10, CancellationToken.None);

        Assert.Equal(["earlier", "later-a"], firstTwo.Select(release => release.ReleaseId));
        Assert.Equal(["earlier", "later-a", "later-b"], allPending.Select(release => release.ReleaseId));
        Assert.Equal(["later-matched"], allMatched.Select(release => release.ReleaseId));
    }

    [Fact]
    public async Task TryTransitionAsync_StaleExpectedState_ChangesNothing()
    {
        var database = await DiscoveryStoreTestDatabase.CreateAsync(FileService.GetTempPath());
        var store = database.CreateStore();
        var audiobookId = await AddAudiobookAsync(database);
        await CommitAsync(store, database.IndexerIds[0], NowUtc, "release-x");
        var release = await FindAsync(database, "release-x");
        var matchedUtc = NowUtc.AddMinutes(1);

        var matched = await store.TryTransitionAsync(
            release.Id, DiscoveredReleaseState.Pending, DiscoveredReleaseState.Matched, null, audiobookId, matchedUtc, CancellationToken.None);
        var stale = await store.TryTransitionAsync(
            release.Id, DiscoveredReleaseState.Pending, DiscoveredReleaseState.Ignored, "NoMatch", null, NowUtc.AddMinutes(2), CancellationToken.None);
        var missing = await store.TryTransitionAsync(
            release.Id + 1000, DiscoveredReleaseState.Pending, DiscoveredReleaseState.Ignored, "NoMatch", null, NowUtc, CancellationToken.None);
        var afterStale = await FindAsync(database, "release-x");
        var handled = await store.TryTransitionAsync(
            release.Id, DiscoveredReleaseState.Matched, DiscoveredReleaseState.Handled, "Queued", audiobookId, NowUtc.AddMinutes(3), CancellationToken.None);
        var afterHandled = await FindAsync(database, "release-x");

        Assert.True(matched);
        Assert.False(stale);
        Assert.False(missing);
        Assert.Equal(DiscoveredReleaseState.Matched, afterStale.State);
        Assert.Equal(audiobookId, afterStale.MatchedAudiobookId);
        Assert.Null(afterStale.Outcome);
        Assert.Equal(matchedUtc, afterStale.HandledUtc);
        Assert.True(handled);
        Assert.Equal(
            (DiscoveredReleaseState.Handled, "Queued", (int?)audiobookId, (DateTime?)NowUtc.AddMinutes(3)),
            (afterHandled.State, afterHandled.Outcome, afterHandled.MatchedAudiobookId, afterHandled.HandledUtc));
    }

    [Fact]
    public async Task PruneAsync_DeletesOnlyOldHandledAndIgnoredRowsOldestFirstWithinTheBound()
    {
        var database = await DiscoveryStoreTestDatabase.CreateAsync(FileService.GetTempPath());
        var store = database.CreateStore();
        var indexerId = database.IndexerIds[0];
        var cutoff = NowUtc.AddDays(-30);
        // The oldest rows are the ones that must survive, so pruning by age alone would take them first.
        await CommitAsync(store, indexerId, cutoff.AddDays(-30), "old-pending", "old-matched");
        await CommitAsync(store, indexerId, cutoff.AddDays(-20), "handled-0");
        await CommitAsync(store, indexerId, cutoff.AddDays(-10), "handled-1", "handled-2", "handled-3", "ignored-1");
        await CommitAsync(store, indexerId, cutoff.AddDays(1), "recent-handled", "recent-ignored");
        await TransitionAsync(store, database, "old-matched", DiscoveredReleaseState.Matched);
        foreach (var releaseId in new[] { "handled-0", "handled-1", "handled-2", "handled-3", "recent-handled" })
        {
            await TransitionAsync(store, database, releaseId, DiscoveredReleaseState.Handled);
        }

        foreach (var releaseId in new[] { "ignored-1", "recent-ignored" })
        {
            await TransitionAsync(store, database, releaseId, DiscoveredReleaseState.Ignored);
        }

        var firstPrune = await store.PruneAsync(cutoff, limitPerState: 2, CancellationToken.None);
        var afterFirst = await ReleaseIdsAsync(database);
        var secondPrune = await store.PruneAsync(cutoff, limitPerState: 2, CancellationToken.None);
        var thirdPrune = await store.PruneAsync(cutoff, limitPerState: 2, CancellationToken.None);
        var afterAll = await ReleaseIdsAsync(database);

        Assert.Equal(3, firstPrune);
        Assert.Equal(
            ["handled-2", "handled-3", "old-matched", "old-pending", "recent-handled", "recent-ignored"],
            afterFirst);
        Assert.Equal(2, secondPrune);
        Assert.Equal(0, thirdPrune);
        Assert.Equal(["old-matched", "old-pending", "recent-handled", "recent-ignored"], afterAll);
    }

    [Fact]
    public async Task GetStatusAsync_ReadsTheCheckpointByKeyAndNewestOpenGapsWithoutTouchingReleases()
    {
        var database = await DiscoveryStoreTestDatabase.CreateAsync(FileService.GetTempPath(), indexerCount: 2);
        var indexerId = database.IndexerIds[0];
        var store = database.CreateStore();
        for (var poll = 0; poll < 3; poll++)
        {
            await store.CommitPollAsync(
                indexerId,
                DiscoveryStoreTestDatabase.Releases(poll * 10, 10, NowUtc.AddHours(poll)),
                new DiscoveryCheckpointAdvance(NowUtc.AddHours(poll), $"release-{poll * 10:D4}"),
                new DiscoveryCoverageGapSpan(NowUtc.AddHours(poll - 1), NowUtc.AddHours(poll)),
                futureDatedDelta: 0,
                undatedDelta: 1,
                NowUtc.AddHours(poll),
                CancellationToken.None);
        }

        await using (var db = database.CreateContext())
        {
            var newest = await db.IndexerCoverageGaps.SingleAsync(gap => gap.DetectedUtc == NowUtc.AddHours(2));
            newest.AcknowledgedUtc = NowUtc.AddHours(3);
            await db.SaveChangesAsync();
        }

        var recorder = new CommandTextRecorder();
        var observed = database.CreateStore(recorder);
        var status = await observed.GetStatusAsync(indexerId, gapLimit: 10, CancellationToken.None);
        var limited = await observed.GetStatusAsync(indexerId, gapLimit: 1, CancellationToken.None);
        var checkpointOnly = await observed.GetStatusAsync(indexerId, gapLimit: 0, CancellationToken.None);
        var neverPolled = await observed.GetStatusAsync(database.IndexerIds[1], gapLimit: 10, CancellationToken.None);

        Assert.NotNull(status.Checkpoint);
        Assert.Equal(("release-0020", (DateTime?)NowUtc.AddHours(2), 3), (status.Checkpoint.NewestReleaseId, status.Checkpoint.LastSuccessUtc, status.Checkpoint.UndatedItemCount));
        Assert.Equal([NowUtc.AddHours(1), NowUtc], status.OpenGaps.Select(gap => gap.DetectedUtc));
        Assert.Equal([NowUtc.AddHours(1)], limited.OpenGaps.Select(gap => gap.DetectedUtc));
        Assert.NotNull(checkpointOnly.Checkpoint);
        Assert.Empty(checkpointOnly.OpenGaps);
        Assert.Null(neverPolled.Checkpoint);
        Assert.Empty(neverPolled.OpenGaps);
        Assert.NotEmpty(recorder.Commands);
        Assert.DoesNotContain(recorder.Commands, command => command.Contains("DiscoveredReleases", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EveryMethod_OnANonRelationalContext_ThrowsInvalidOperationException()
    {
        var options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseInMemoryDatabase($"discovery-store-{Guid.NewGuid():N}")
            .Options;
        var store = new EfDiscoveryStore(new InMemoryDbContextFactory(options));
        var calls = new Func<Task>[]
        {
            () => store.CommitPollAsync(1, [], null, null, 0, 0, NowUtc, CancellationToken.None),
            () => store.GetBatchAsync(DiscoveredReleaseState.Pending, 1, CancellationToken.None),
            () => store.TryTransitionAsync(1, DiscoveredReleaseState.Pending, DiscoveredReleaseState.Matched, null, null, NowUtc, CancellationToken.None),
            () => store.PruneAsync(NowUtc, 1, CancellationToken.None),
            () => store.GetStatusAsync(1, 1, CancellationToken.None)
        };

        foreach (var call in calls)
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(call);
            Assert.Contains("relational database", exception.Message, StringComparison.Ordinal);
        }
    }

    private static Task<int> CommitAsync(IDiscoveryStore store, int indexerId, DateTime pollUtc, params string[] releaseIds) =>
        store.CommitPollAsync(
            indexerId,
            releaseIds.Select(releaseId => new DiscoveredReleaseCandidate(releaseId, pollUtc, $"Title of {releaseId}", "{}")).ToList(),
            advance: null,
            gap: null,
            futureDatedDelta: 0,
            undatedDelta: 0,
            pollUtc,
            CancellationToken.None);

    private static async Task TransitionAsync(
        IDiscoveryStore store,
        DiscoveryStoreTestDatabase database,
        string releaseId,
        DiscoveredReleaseState next)
    {
        var release = await FindAsync(database, releaseId);
        Assert.True(await store.TryTransitionAsync(
            release.Id, DiscoveredReleaseState.Pending, next, "Test", null, NowUtc, CancellationToken.None));
    }

    private static async Task<DiscoveredRelease> FindAsync(DiscoveryStoreTestDatabase database, string releaseId)
    {
        await using var db = database.CreateContext();
        return await db.DiscoveredReleases.AsNoTracking().SingleAsync(release => release.ReleaseId == releaseId);
    }

    private static async Task<IReadOnlyList<string>> ReleaseIdsAsync(DiscoveryStoreTestDatabase database)
    {
        await using var db = database.CreateContext();
        var releaseIds = await db.DiscoveredReleases.AsNoTracking().Select(release => release.ReleaseId).ToListAsync();
        return releaseIds.Order(StringComparer.Ordinal).ToList();
    }

    private static async Task<int> AddAudiobookAsync(DiscoveryStoreTestDatabase database)
    {
        await using var db = database.CreateContext();
        var audiobook = new Audiobook { Title = "A Princess of Mars", Asin = "B008DFUGCQ" };
        db.Audiobooks.Add(audiobook);
        await db.SaveChangesAsync();
        return audiobook.Id;
    }

    private sealed class InMemoryDbContextFactory(DbContextOptions<ListenArrDbContext> options)
        : IDbContextFactory<ListenArrDbContext>
    {
        public ListenArrDbContext CreateDbContext() => new(options);
    }
}
