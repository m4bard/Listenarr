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
using Listenarr.Application.Search.Discovery;
using Listenarr.Tests.Common;
using static Listenarr.Tests.Features.Infrastructure.HostedServices.Search.IndexerFeedPollerTestKit;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search;

/// <summary>
/// When the feed poller records a coverage gap and where it leaves the checkpoint: outages,
/// short feeds, future-dated items, undated feeds and indexers with small pages.
/// </summary>
[Trait("Name", "IndexerFeedPollerGapTests")]
[Trait("Category", "Infrastructure")]
public sealed class IndexerFeedPollerGapTests : BaseTests
{
    private static readonly DateTime CheckpointUtc = BaselineUtc.AddMinutes(-5);
    private static readonly DateTime SecondPollUtc = BaselineUtc.AddMinutes(15);

    [Fact]
    public async Task TenFullPagesAllNewerThanTheCheckpoint_RecordAGapFromTheCheckpointToTheOldestFetched()
    {
        // D-T4(a): a 6 h outage with 1200 new items.
        var kit = await BaselineAsync();
        var pollUtc = BaselineUtc.AddHours(6);
        var fresh = Dated(1000, 1200, pollUtc.AddMinutes(-1), TimeSpan.FromSeconds(10));
        kit.Feed.Items = [.. fresh, .. kit.Feed.Items];

        var result = await kit.PollAsync(pollUtc);

        Assert.Equal(Enumerable.Range(0, 10).Select(page => page * 100), kit.Feed.Offsets);
        var gap = Assert.Single(await kit.ReadGapsAsync());
        Assert.Equal(CheckpointUtc, gap.GapStartUtc);
        Assert.Equal(fresh[999].PublishedUtc, gap.GapEndUtc);
        Assert.Equal(new DiscoveryCoverageGapSpan(gap.GapStartUtc, gap.GapEndUtc), result.Gap);
        var checkpoint = await kit.ReadCheckpointAsync();
        Assert.Equal(fresh[0].PublishedUtc, checkpoint!.NewestPublishedUtc);
        Assert.Equal(fresh[0].ReleaseId, checkpoint.NewestReleaseId);
        Assert.Contains(kit.Log.Warnings, warning => warning.Contains("didn't cover the period", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, new[] { 0, 100, 150 })]
    [InlineData(true, new[] { 0, 100 })]
    public async Task FeedEndsWhileStillNewerThanTheCheckpoint_RecordsAGap(bool statesTotal, int[] offsets)
    {
        // D-T4(b): the indexer no longer holds anything as old as C. The feed ends at an empty
        // page, or when the stated total is reached.
        var kit = await BaselineAsync();
        var fresh = Dated(1000, 150, SecondPollUtc.AddMinutes(-1), TimeSpan.FromSeconds(1));
        kit.Feed.Items = fresh;
        kit.Feed.StatesTotal = statesTotal;

        await kit.PollAsync(SecondPollUtc);

        Assert.Equal(offsets, kit.Feed.Offsets);
        var gap = Assert.Single(await kit.ReadGapsAsync());
        Assert.Equal(CheckpointUtc, gap.GapStartUtc);
        Assert.Equal(fresh[^1].PublishedUtc, gap.GapEndUtc);
    }

    [Fact]
    public async Task PollThatReachesTheCheckpoint_RecordsNoGap_AndAdvancesToTheNewest()
    {
        // D-T4(c).
        var kit = await BaselineAsync();
        kit.Feed.Items = [.. Dated(1000, 30, SecondPollUtc.AddMinutes(-1), TimeSpan.FromSeconds(5)), .. kit.Feed.Items];

        var result = await kit.PollAsync(SecondPollUtc);

        Assert.Equal([0], kit.Feed.Offsets);
        Assert.Null(result.Gap);
        Assert.Empty(await kit.ReadGapsAsync());
        Assert.Empty(kit.Log.Warnings);
        Assert.Equal(ReleaseId(1000), (await kit.ReadCheckpointAsync())!.NewestReleaseId);
    }

    [Fact]
    public async Task ItemDatedThreeDaysAhead_IsStoredAndCounted_ButDoesNotMoveTheCheckpoint()
    {
        // D-T4(d).
        var kit = await BaselineAsync();
        var future = Item(9000, SecondPollUtc.AddDays(3));
        kit.Feed.Items = [future, .. kit.Feed.Items];

        await kit.PollAsync(SecondPollUtc);

        Assert.Contains(future.ReleaseId, await kit.ReadReleaseIdsAsync());
        var checkpoint = await kit.ReadCheckpointAsync();
        Assert.Equal(CheckpointUtc, checkpoint!.NewestPublishedUtc);
        Assert.Equal(ReleaseId(0), checkpoint.NewestReleaseId);
        Assert.Equal(SecondPollUtc, checkpoint.LastSuccessUtc);
        Assert.Equal(1, checkpoint.FutureDatedItemCount);
    }

    [Fact]
    public async Task UndatedFeedInSteadyState_SendsOneRequestPerPoll_AndRecordsNoGap()
    {
        // D-T4(f): the checkpoint item is found again by its release id.
        var kit = await CreateAsync(FileService.GetTempPath());
        kit.Feed.Items = Undated(0, 100);
        await kit.PollAsync(BaselineUtc);

        for (var poll = 1; poll <= 3; poll++)
        {
            kit.Feed.Items = [.. Undated(poll * 1000, 5), .. kit.Feed.Items];

            var result = await kit.PollAsync(BaselineUtc.AddMinutes(15 * poll));

            Assert.Single(kit.Feed.Requests);
            Assert.Null(result.Gap);
            Assert.Equal(ReleaseId(poll * 1000), (await kit.ReadCheckpointAsync())!.NewestReleaseId);
        }

        Assert.Empty(await kit.ReadGapsAsync());
        var checkpoint = await kit.ReadCheckpointAsync();
        Assert.Null(checkpoint!.NewestPublishedUtc);
        // Every undated item a poll reads is counted, once per poll that reads it.
        Assert.Equal(400, checkpoint.UndatedItemCount);
    }

    [Fact]
    public async Task UndatedBurstPastThePageBound_RecordsAGapBoundedByPollTimes()
    {
        // D-T4(f2): 1100 new undated items push the checkpoint item past the 10th page.
        var kit = await CreateAsync(FileService.GetTempPath());
        kit.Feed.Items = Undated(0, 100);
        await kit.PollAsync(BaselineUtc);
        kit.Feed.Items = [.. Undated(1000, 1100), .. kit.Feed.Items];

        await kit.PollAsync(SecondPollUtc);

        Assert.Equal(10, kit.Feed.Requests.Count);
        var gap = Assert.Single(await kit.ReadGapsAsync());
        Assert.Equal(BaselineUtc, gap.GapStartUtc);
        Assert.Equal(SecondPollUtc, gap.GapEndUtc);
        Assert.Contains(kit.Log.Warnings, warning => warning.Contains("local poll times", StringComparison.Ordinal));
        Assert.Equal(ReleaseId(1000), (await kit.ReadCheckpointAsync())!.NewestReleaseId);
    }

    [Fact]
    public async Task OutlierTwentyHoursAhead_ThenAnOutagePastThePageBound_StillRecordsAGap()
    {
        // D-T4(g): the outlier never becomes the checkpoint, so the outage is not hidden behind it.
        var kit = await BaselineAsync();
        var outlier = Item(9000, SecondPollUtc.AddHours(20));
        var firstFresh = Dated(1000, 10, SecondPollUtc.AddMinutes(-1), TimeSpan.FromMinutes(1));
        kit.Feed.Items = [outlier, .. firstFresh, .. kit.Feed.Items];
        await kit.PollAsync(SecondPollUtc);
        var afterOutlier = await kit.ReadCheckpointAsync();
        Assert.Equal(firstFresh[0].PublishedUtc, afterOutlier!.NewestPublishedUtc);

        var outageEndUtc = SecondPollUtc.AddHours(6);
        var burst = Dated(2000, 1200, outageEndUtc.AddMinutes(-1), TimeSpan.FromSeconds(15));
        kit.Feed.Items = [outlier, .. burst, .. firstFresh, .. kit.Feed.Items.Skip(1 + firstFresh.Count)];

        var result = await kit.PollAsync(outageEndUtc);

        var gap = Assert.Single(await kit.ReadGapsAsync());
        Assert.Equal(firstFresh[0].PublishedUtc, gap.GapStartUtc);
        Assert.Equal(burst[998].PublishedUtc, gap.GapEndUtc);
        Assert.Equal(result.Gap, new DiscoveryCoverageGapSpan(gap.GapStartUtc, gap.GapEndUtc));
        Assert.Equal(2, (await kit.ReadCheckpointAsync())!.FutureDatedItemCount);
    }

    [Fact]
    public async Task IndexerThatCapsPagesAtFifty_IsPagedPastAFiftyItemPage_WithoutAFalseGap()
    {
        // D-T4(h): a short page is not the end of the feed.
        var kit = await CreateAsync(FileService.GetTempPath());
        kit.Feed.PageSize = 50;
        kit.Feed.Items = Dated(0, 100, CheckpointUtc, TimeSpan.FromMinutes(5));
        await kit.PollAsync(BaselineUtc);
        var fresh = Dated(1000, 80, SecondPollUtc.AddMinutes(-1), TimeSpan.FromSeconds(10));
        kit.Feed.Items = [.. fresh, .. kit.Feed.Items];

        var result = await kit.PollAsync(SecondPollUtc);

        Assert.Equal([0, 50], kit.Feed.Offsets);
        Assert.Null(result.Gap);
        Assert.Empty(await kit.ReadGapsAsync());
        Assert.Superset(fresh.Select(item => item.ReleaseId).ToHashSet(), await kit.ReadReleaseIdsAsync());
    }

    [Fact]
    public async Task ShortFeedPolledAgainWithNothingNew_RecordsNoGapAndNoWarning()
    {
        // D-T4(i), E1: the feed's whole history spans 30 minutes, less than the overlap, so the
        // window never closes; the oldest item is not newer than C, so nothing was missed.
        var kit = await CreateAsync(FileService.GetTempPath());
        kit.Feed.Items = Dated(0, 5, CheckpointUtc, TimeSpan.FromMinutes(7.5));
        await kit.PollAsync(BaselineUtc);

        foreach (var pollUtc in new[] { SecondPollUtc, SecondPollUtc.AddMinutes(15) })
        {
            var result = await kit.PollAsync(pollUtc);

            Assert.Equal([0, 5], kit.Feed.Offsets);
            Assert.Null(result.Gap);
        }

        Assert.Empty(await kit.ReadGapsAsync());
        Assert.Empty(kit.Log.Warnings);
        Assert.Equal(CheckpointUtc, (await kit.ReadCheckpointAsync())!.NewestPublishedUtc);
    }

    [Fact]
    public async Task EmptyFeedPolledThreeTimes_RecordsNoGapAndNoWarning_AndMovesOnlyThePollTime()
    {
        // E7: a poll that reads no items has nothing to compare with the checkpoint.
        var kit = await BaselineAsync();
        kit.Feed.Items = [];

        for (var poll = 1; poll <= 3; poll++)
        {
            var pollUtc = BaselineUtc.AddMinutes(15 * poll);

            var result = await kit.PollAsync(pollUtc);

            Assert.Null(result.Gap);
            Assert.False(result.Advanced);
            var checkpoint = await kit.ReadCheckpointAsync();
            Assert.Equal(pollUtc, checkpoint!.LastPollUtc);
            Assert.Equal(BaselineUtc, checkpoint.LastSuccessUtc);
            Assert.Equal(CheckpointUtc, checkpoint.NewestPublishedUtc);
            Assert.Equal(ReleaseId(0), checkpoint.NewestReleaseId);
        }

        Assert.Empty(await kit.ReadGapsAsync());
        Assert.Empty(kit.Log.Warnings);
    }

    private async Task<IndexerFeedPollerTestKit> BaselineAsync()
    {
        var kit = await CreateAsync(FileService.GetTempPath());
        kit.Feed.Items = Dated(0, 100, CheckpointUtc, TimeSpan.FromMinutes(1));
        await kit.PollAsync(BaselineUtc);
        return kit;
    }
}
