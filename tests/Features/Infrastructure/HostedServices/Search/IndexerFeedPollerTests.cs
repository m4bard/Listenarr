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
using Listenarr.Tests.Common;
using Listenarr.Tests.Features.Infrastructure.Persistence;
using static Listenarr.Tests.Features.Infrastructure.HostedServices.Search.IndexerFeedPollerTestKit;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search;

/// <summary>
/// The feed poller's paging, overlap and failure handling: which pages it asks for, what it
/// stores, and when the checkpoint stays put. Scripted feeds over the real store on SQLite.
/// </summary>
[Trait("Name", "IndexerFeedPollerTests")]
[Trait("Category", "Infrastructure")]
public sealed class IndexerFeedPollerTests : BaseTests
{
    // The baseline poll's newest item, X, is dated C; the baseline feed has 100 items a minute apart.
    private static readonly DateTime CheckpointUtc = BaselineUtc.AddMinutes(-5);
    private static readonly DateTime SecondPollUtc = BaselineUtc.AddMinutes(15);

    [Fact]
    public async Task FirstPoll_ReadsPageZeroOnly_AndSetsTheCheckpointWithoutAGap()
    {
        var kit = await CreateAsync(FileService.GetTempPath());
        kit.Feed.Items = Dated(0, 250, CheckpointUtc, TimeSpan.FromMinutes(1));

        var result = await kit.PollAsync(BaselineUtc);

        Assert.Equal([0], kit.Feed.Offsets);
        Assert.Null(result.Gap);
        var checkpoint = await kit.ReadCheckpointAsync();
        Assert.Equal(CheckpointUtc, checkpoint!.NewestPublishedUtc);
        Assert.Equal(ReleaseId(0), checkpoint.NewestReleaseId);
        Assert.Equal(BaselineUtc, checkpoint.LastSuccessUtc);
        Assert.Equal(100, (await kit.ReadReleaseIdsAsync()).Count);
        Assert.Empty(await kit.ReadGapsAsync());
    }

    [Fact]
    public async Task EachPage_IsAFeedRequestFromDiscovery_AtTheItemsReceivedSoFar()
    {
        var kit = await BaselineAsync();
        kit.Feed.Items = [.. Dated(1000, 150, SecondPollUtc.AddMinutes(-1), TimeSpan.FromSeconds(5)), .. kit.Feed.Items];
        // Three items the adapter leaves out still count toward the next offset.
        kit.Feed.LeftOut.UnionWith([ReleaseId(1001), ReleaseId(1002), ReleaseId(1003)]);

        await kit.PollAsync(SecondPollUtc);

        Assert.Equal([0, 100, 200], kit.Feed.Offsets);
        Assert.Equal(
            [IndexerRequestKind.FeedPoll, IndexerRequestKind.FeedPage, IndexerRequestKind.FeedPage],
            kit.Feed.Requests.Select(request => request.Context.QueryKind));
        Assert.All(kit.Feed.Requests, request =>
        {
            Assert.Equal(SearchOrigin.Discovery, request.Context.Origin);
            Assert.Null(request.Context.CycleAllowance);
        });
    }

    [Fact]
    public async Task StoreFailureAfterTheFetch_LeavesCheckpointAndRows_AndTheNextPollRefetchesTheSameOffsets()
    {
        // D-T2 (poller half).
        var kit = await BaselineAsync();
        var before = await kit.ReadCheckpointAsync();
        var baselineIds = await kit.ReadReleaseIdsAsync();
        kit.Feed.Items = [.. Dated(1000, 150, SecondPollUtc.AddMinutes(-1), TimeSpan.FromSeconds(5)), .. kit.Feed.Items];

        await Assert.ThrowsAsync<InjectedStorageFailureException>(() =>
            kit.PollAsync(SecondPollUtc, new FailingWriteInterceptor("INSERT INTO \"DiscoveredReleases\"", 50)));
        var failedOffsets = kit.Feed.Offsets;

        Assert.Equivalent(before, await kit.ReadCheckpointAsync(), strict: true);
        Assert.Equal(baselineIds, await kit.ReadReleaseIdsAsync());
        Assert.Empty(await kit.ReadGapsAsync());

        var retry = await kit.PollAsync(SecondPollUtc.AddMinutes(15));

        Assert.Equal([0, 100, 200], failedOffsets);
        Assert.Equal(failedOffsets, kit.Feed.Offsets);
        Assert.Equal(150, retry.ReleasesStored);
        Assert.Equal(ReleaseId(1000), (await kit.ReadCheckpointAsync())!.NewestReleaseId);
    }

    [Fact]
    public async Task LateItemOnPageOne_WhilePageZeroEndsInsideTheOverlap_IsStored()
    {
        // D-T3(b): page 0's oldest item is C minus 49 minutes, inside (C - Overlap, C].
        var kit = await BaselineAsync();
        var baseline = kit.Feed.Items;
        var late = Item(5000, CheckpointUtc.AddMinutes(-30));
        kit.Feed.Items =
        [
            .. Dated(1000, 50, SecondPollUtc.AddMinutes(-1), TimeSpan.FromSeconds(20)),
            .. baseline.Take(50),
            late,
            .. baseline.Skip(50)
        ];

        var result = await kit.PollAsync(SecondPollUtc);

        Assert.Contains(late.ReleaseId, await kit.ReadReleaseIdsAsync());
        Assert.Equal([0, 100], kit.Feed.Offsets);
        Assert.Null(result.Gap);
    }

    [Fact]
    public async Task CheckpointItemLastOnPageZero_DoesNotStopThePoll_SoTheLateItemOnPageOneIsStored()
    {
        // D-T3(c): on a dated page only the dates decide, so X on page 0 does not close the window.
        var kit = await BaselineAsync();
        var baseline = kit.Feed.Items;
        var late = Item(5000, CheckpointUtc.AddMinutes(-30));
        kit.Feed.Items =
        [
            .. Dated(1000, 99, SecondPollUtc.AddMinutes(-1), TimeSpan.FromSeconds(10)),
            baseline[0],
            late,
            .. baseline.Skip(1)
        ];

        var result = await kit.PollAsync(SecondPollUtc);

        Assert.Contains(late.ReleaseId, await kit.ReadReleaseIdsAsync());
        Assert.Equal([0, 100], kit.Feed.Offsets);
        Assert.Null(result.Gap);
    }

    [Fact]
    public async Task CallerCancelDuringThePoll_WritesNothing()
    {
        // D-T11(a): page 0 was read, but a cancelled poll stores none of it and keeps LastPollUtc.
        var kit = await BaselineAsync();
        var before = await kit.ReadCheckpointAsync();
        var baselineIds = await kit.ReadReleaseIdsAsync();
        kit.Feed.Items = [.. Dated(1000, 150, SecondPollUtc.AddMinutes(-1), TimeSpan.FromSeconds(5)), .. kit.Feed.Items];
        using var cancellation = new CancellationTokenSource();
        kit.Feed.ScriptPage(1, () =>
        {
            cancellation.Cancel();
            return ScriptedFeed.Cancelled(kit.Indexer.Id);
        });

        var result = await kit.PollAsync(SecondPollUtc, cancellation.Token);

        Assert.Equal(IndexerSearchStatus.Cancelled, result.Status);
        Assert.Equivalent(before, await kit.ReadCheckpointAsync(), strict: true);
        Assert.Equal(baselineIds, await kit.ReadReleaseIdsAsync());
    }

    [Fact]
    public async Task TimeoutWithoutCallerCancel_IsFailed_SetsLastPoll_AndKeepsTheCheckpoint()
    {
        // D-T11(b).
        var kit = await BaselineAsync();
        kit.Feed.Items = [.. Dated(1000, 30, SecondPollUtc.AddMinutes(-1), TimeSpan.FromSeconds(5)), .. kit.Feed.Items];
        kit.Feed.ScriptPage(0, () => ScriptedFeed.Failed(kit.Indexer.Id, "timeout"));

        var result = await kit.PollAsync(SecondPollUtc);

        Assert.Equal(IndexerSearchStatus.Failed, result.Status);
        Assert.False(result.Advanced);
        var checkpoint = await kit.ReadCheckpointAsync();
        Assert.Equal(SecondPollUtc, checkpoint!.LastPollUtc);
        Assert.Equal(BaselineUtc, checkpoint.LastSuccessUtc);
        Assert.Equal(CheckpointUtc, checkpoint.NewestPublishedUtc);
        Assert.Equal(ReleaseId(0), checkpoint.NewestReleaseId);
    }

    [Fact]
    public async Task SecondPageDeferred_StoresPageZero_AndKeepsTheCheckpoint()
    {
        // D-T11(c), and the poller half of D-T5(a).
        var kit = await BaselineAsync();
        kit.Feed.Items = [.. Dated(1000, 150, SecondPollUtc.AddMinutes(-1), TimeSpan.FromSeconds(5)), .. kit.Feed.Items];
        kit.Feed.ScriptPage(1, () => ScriptedFeed.Deferred(kit.Indexer.Id, SecondPollUtc.AddMinutes(10)));

        var result = await kit.PollAsync(SecondPollUtc);

        Assert.Equal(IndexerSearchStatus.Deferred, result.Status);
        Assert.Equal(100, result.ReleasesStored);
        Assert.Contains(ReleaseId(1099), await kit.ReadReleaseIdsAsync());
        var checkpoint = await kit.ReadCheckpointAsync();
        Assert.Equal(CheckpointUtc, checkpoint!.NewestPublishedUtc);
        Assert.Equal(ReleaseId(0), checkpoint.NewestReleaseId);
        Assert.Equal(BaselineUtc, checkpoint.LastSuccessUtc);
        Assert.Equal(SecondPollUtc, checkpoint.LastPollUtc);
    }

    [Fact]
    public async Task UndatedFirstPage_TakesTheFirstItemNotDatedInTheFuture_AsTheCheckpointItem()
    {
        // E5: the future-dated item heads the page, so it is not the checkpoint item.
        var kit = await CreateAsync(FileService.GetTempPath());
        kit.Feed.Items = [Item(9000, BaselineUtc.AddDays(3)), .. Undated(0, 99)];

        await kit.PollAsync(BaselineUtc);

        var checkpoint = await kit.ReadCheckpointAsync();
        Assert.Null(checkpoint!.NewestPublishedUtc);
        Assert.Equal(ReleaseId(0), checkpoint.NewestReleaseId);
        Assert.Equal(1, checkpoint.FutureDatedItemCount);
        Assert.Equal(100, checkpoint.UndatedItemCount);
    }

    [Fact]
    public async Task FirstPageAllDatedInTheFuture_TakesItsFirstItem_AsTheCheckpointItem()
    {
        var kit = await CreateAsync(FileService.GetTempPath());
        kit.Feed.Items = Dated(0, 3, BaselineUtc.AddDays(3), TimeSpan.FromMinutes(1));

        await kit.PollAsync(BaselineUtc);

        var checkpoint = await kit.ReadCheckpointAsync();
        Assert.Null(checkpoint!.NewestPublishedUtc);
        Assert.Equal(ReleaseId(0), checkpoint.NewestReleaseId);
        Assert.Equal(3, checkpoint.FutureDatedItemCount);
    }

    /// <summary>A kit whose indexer has had its baseline poll: 100 items, X = item 0 dated C.</summary>
    private async Task<IndexerFeedPollerTestKit> BaselineAsync()
    {
        var kit = await CreateAsync(FileService.GetTempPath());
        kit.Feed.Items = Dated(0, 100, CheckpointUtc, TimeSpan.FromMinutes(1));
        await kit.PollAsync(BaselineUtc);
        return kit;
    }
}
