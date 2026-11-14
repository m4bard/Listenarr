using Listenarr.Application.Search.Discovery;
using Listenarr.Domain.Search.Discovery;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// A poll's releases, checkpoint and gap are committed together or not at all, and a release
/// seen again is stored once. Runs on a SQLite file database migrated by the real chain.
/// </summary>
[Trait("Area", "Persistence")]
[Trait("Name", "EfDiscoveryStoreCommitTests")]
[Trait("Category", "Infrastructure")]
public sealed class EfDiscoveryStoreCommitTests : BaseTests
{
    private static readonly DateTime FirstPollUtc = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SecondPollUtc = FirstPollUtc.AddMinutes(15);

    public static TheoryData<string, int> FailurePoints => new()
    {
        { "INSERT INTO \"DiscoveredReleases\"", 50 },
        { "INSERT INTO \"IndexerDiscoveryCheckpoints\"", 1 },
        { "INSERT INTO \"IndexerCoverageGaps\"", 1 }
    };

    [Theory]
    [MemberData(nameof(FailurePoints))]
    public async Task CommitPollAsync_StorageFailureDuringThePoll_LeavesCheckpointAndRowsUnchanged(
        string failingStatement,
        int occurrence)
    {
        var database = await DiscoveryStoreTestDatabase.CreateAsync(FileService.GetTempPath());
        var indexerId = database.IndexerIds[0];
        await database.CreateStore().CommitPollAsync(
            indexerId,
            DiscoveryStoreTestDatabase.Releases(0, 10, FirstPollUtc),
            new DiscoveryCheckpointAdvance(FirstPollUtc, "release-0000"),
            gap: null,
            futureDatedDelta: 1,
            undatedDelta: 2,
            FirstPollUtc,
            CancellationToken.None);
        var before = await ReadCheckpointAsync(database, indexerId);
        var secondPoll = DiscoveryStoreTestDatabase.Releases(10, 100, SecondPollUtc);
        var secondAdvance = new DiscoveryCheckpointAdvance(SecondPollUtc, "release-0010");
        var secondGap = new DiscoveryCoverageGapSpan(FirstPollUtc, SecondPollUtc.AddHours(-1));

        await Assert.ThrowsAsync<InjectedStorageFailureException>(() =>
            database.CreateStore(new FailingWriteInterceptor(failingStatement, occurrence)).CommitPollAsync(
                indexerId,
                secondPoll,
                secondAdvance,
                secondGap,
                futureDatedDelta: 4,
                undatedDelta: 8,
                SecondPollUtc,
                CancellationToken.None));

        Assert.Equivalent(before, await ReadCheckpointAsync(database, indexerId), strict: true);
        await using (var db = database.CreateContext())
        {
            Assert.Equal(10, await db.DiscoveredReleases.CountAsync());
            Assert.Equal(0, await db.IndexerCoverageGaps.CountAsync());
        }

        // The same poll retried once storage recovers stores everything it failed to.
        var inserted = await database.CreateStore().CommitPollAsync(
            indexerId,
            secondPoll,
            secondAdvance,
            secondGap,
            futureDatedDelta: 4,
            undatedDelta: 8,
            SecondPollUtc,
            CancellationToken.None);

        Assert.Equal(100, inserted);
        var after = await ReadCheckpointAsync(database, indexerId);
        Assert.Equal(SecondPollUtc, after.NewestPublishedUtc);
        Assert.Equal("release-0010", after.NewestReleaseId);
        Assert.Equal(SecondPollUtc, after.LastSuccessUtc);
        Assert.Equal(5, after.FutureDatedItemCount);
        Assert.Equal(10, after.UndatedItemCount);
        await using (var db = database.CreateContext())
        {
            Assert.Equal(110, await db.DiscoveredReleases.CountAsync());
            Assert.Equal(1, await db.IndexerCoverageGaps.CountAsync());
        }
    }

    [Fact]
    public async Task CommitPollAsync_OverlappingPolls_StoreEachReleaseIdOncePerIndexer()
    {
        var database = await DiscoveryStoreTestDatabase.CreateAsync(FileService.GetTempPath(), indexerCount: 2);
        var indexerId = database.IndexerIds[0];
        var store = database.CreateStore();

        var firstInserted = await store.CommitPollAsync(
            indexerId,
            DiscoveryStoreTestDatabase.Releases(0, 100, FirstPollUtc),
            new DiscoveryCheckpointAdvance(FirstPollUtc, "release-0000"),
            gap: null,
            futureDatedDelta: 0,
            undatedDelta: 0,
            FirstPollUtc,
            CancellationToken.None);
        var repeats = DiscoveryStoreTestDatabase.Releases(30, 70, FirstPollUtc)
            .Select(release => release with { Title = "Changed on a later poll", Payload = "{}" });
        var secondInserted = await store.CommitPollAsync(
            indexerId,
            DiscoveryStoreTestDatabase.Releases(100, 30, SecondPollUtc).Concat(repeats).ToList(),
            new DiscoveryCheckpointAdvance(SecondPollUtc, "release-0100"),
            gap: null,
            futureDatedDelta: 0,
            undatedDelta: 0,
            SecondPollUtc,
            CancellationToken.None);
        var otherIndexerInserted = await store.CommitPollAsync(
            database.IndexerIds[1],
            DiscoveryStoreTestDatabase.Releases(0, 1, FirstPollUtc),
            advance: null,
            gap: null,
            futureDatedDelta: 0,
            undatedDelta: 0,
            SecondPollUtc,
            CancellationToken.None);

        Assert.Equal(100, firstInserted);
        Assert.Equal(30, secondInserted);
        Assert.Equal(1, otherIndexerInserted);
        await using var db = database.CreateContext();
        var stored = await db.DiscoveredReleases.AsNoTracking()
            .Where(release => release.IndexerId == indexerId)
            .ToListAsync();
        Assert.Equal(130, stored.Count);
        Assert.Equal(130, stored.Select(release => release.ReleaseId).Distinct(StringComparer.Ordinal).Count());
        var repeated = stored.Single(release => release.ReleaseId == "release-0050");
        Assert.Equal("Release 0050", repeated.Title);
        Assert.Equal(FirstPollUtc, repeated.FirstSeenUtc);
    }

    [Fact]
    public async Task CommitPollAsync_Checkpoint_AlwaysRecordsThePollAndMovesOnlyOnAdvance()
    {
        var database = await DiscoveryStoreTestDatabase.CreateAsync(FileService.GetTempPath());
        var indexerId = database.IndexerIds[0];
        var store = database.CreateStore();
        var thirdPollUtc = SecondPollUtc.AddMinutes(15);

        await store.CommitPollAsync(
            indexerId, [], advance: null, gap: null, futureDatedDelta: 2, undatedDelta: 3, FirstPollUtc, CancellationToken.None);
        var notYetAdvanced = await ReadCheckpointAsync(database, indexerId);
        await store.CommitPollAsync(
            indexerId,
            [new DiscoveredReleaseCandidate("release-dated", FirstPollUtc.AddMinutes(-5), "Release dated", "{}")],
            new DiscoveryCheckpointAdvance(FirstPollUtc.AddMinutes(-5), "release-dated"),
            gap: null,
            futureDatedDelta: 1,
            undatedDelta: 0,
            SecondPollUtc,
            CancellationToken.None);
        var advanced = await ReadCheckpointAsync(database, indexerId);
        await store.CommitPollAsync(
            indexerId,
            [new DiscoveredReleaseCandidate("release-undated", null, "Release undated", "{}")],
            advance: null,
            new DiscoveryCoverageGapSpan(FirstPollUtc, SecondPollUtc),
            futureDatedDelta: 0,
            undatedDelta: 1,
            thirdPollUtc,
            CancellationToken.None);
        var kept = await ReadCheckpointAsync(database, indexerId);

        Assert.Equal(FirstPollUtc, notYetAdvanced.LastPollUtc);
        Assert.Null(notYetAdvanced.NewestPublishedUtc);
        Assert.Null(notYetAdvanced.NewestReleaseId);
        Assert.Null(notYetAdvanced.LastSuccessUtc);
        Assert.Equal((2, 3), (notYetAdvanced.FutureDatedItemCount, notYetAdvanced.UndatedItemCount));

        Assert.Equal(SecondPollUtc, advanced.LastPollUtc);
        Assert.Equal(SecondPollUtc, advanced.LastSuccessUtc);
        Assert.Equal(FirstPollUtc.AddMinutes(-5), advanced.NewestPublishedUtc);
        Assert.Equal("release-dated", advanced.NewestReleaseId);
        Assert.Equal((3, 3), (advanced.FutureDatedItemCount, advanced.UndatedItemCount));

        Assert.Equal(thirdPollUtc, kept.LastPollUtc);
        Assert.Equal(SecondPollUtc, kept.LastSuccessUtc);
        Assert.Equal(FirstPollUtc.AddMinutes(-5), kept.NewestPublishedUtc);
        Assert.Equal("release-dated", kept.NewestReleaseId);
        Assert.Equal((3, 4), (kept.FutureDatedItemCount, kept.UndatedItemCount));

        await using var db = database.CreateContext();
        var gap = await db.IndexerCoverageGaps.AsNoTracking().SingleAsync();
        Assert.Equal((indexerId, FirstPollUtc, SecondPollUtc, thirdPollUtc), (gap.IndexerId, gap.GapStartUtc, gap.GapEndUtc, gap.DetectedUtc));
        Assert.Null(gap.AcknowledgedUtc);
        // An EF equality filter on the raw-inserted times finds the rows, so both write paths use one text format.
        var undated = await db.DiscoveredReleases.AsNoTracking()
            .SingleAsync(release => release.FirstSeenUtc == thirdPollUtc);
        Assert.Equal(("release-undated", DiscoveredReleaseState.Pending), (undated.ReleaseId, undated.State));
        Assert.Null(undated.PublishedUtc);
        Assert.Null(undated.HandledUtc);
        Assert.Null(undated.MatchedAudiobookId);
        Assert.Null(undated.Outcome);
        var dated = await db.DiscoveredReleases.AsNoTracking()
            .SingleAsync(release => release.PublishedUtc == FirstPollUtc.AddMinutes(-5));
        Assert.Equal(("release-dated", "Release dated", "{}"), (dated.ReleaseId, dated.Title, dated.Payload));
    }

    private static async Task<IndexerDiscoveryCheckpoint> ReadCheckpointAsync(
        DiscoveryStoreTestDatabase database,
        int indexerId)
    {
        await using var db = database.CreateContext();
        return await db.IndexerDiscoveryCheckpoints.AsNoTracking()
            .SingleAsync(checkpoint => checkpoint.IndexerId == indexerId);
    }
}
