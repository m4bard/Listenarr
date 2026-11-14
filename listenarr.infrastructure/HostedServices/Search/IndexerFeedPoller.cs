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
using System.Text.Json;
using Listenarr.Application.Search.Discovery;
using Listenarr.Domain.Search.Discovery;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.HostedServices.Search
{
    /// <summary>What one poll of an indexer's feed did.</summary>
    /// <param name="Status">Completed when the poll reached a stopping point; otherwise the status
    /// of the page that ended it.</param>
    /// <param name="PagesRead">Pages read successfully.</param>
    /// <param name="ReleasesStored">Releases the poll stored that were not already known.</param>
    /// <param name="Advanced">Whether the checkpoint moved (or was created).</param>
    /// <param name="Gap">The coverage gap the poll recorded, if any.</param>
    internal sealed record IndexerFeedPollResult(
        IndexerSearchStatus Status,
        int PagesRead,
        int ReleasesStored,
        bool Advanced,
        DiscoveryCoverageGapSpan? Gap);

    /// <summary>
    /// Reads one indexer's feed from its newest item back to the checkpoint, overlapping the
    /// checkpoint so that items indexed late are still seen, and stores what it read. The
    /// checkpoint moves only in the transaction that stores the items, and only when the poll
    /// either reached the checkpoint or recorded the stretch it could not cover as a gap.
    /// </summary>
    internal sealed class IndexerFeedPoller(
        IDiscoveryStore store,
        TimeProvider timeProvider,
        ILogger<IndexerFeedPoller> logger)
    {
        /// <summary>
        /// How far behind the checkpoint a poll keeps reading, and how far past the poll time
        /// an item's publish date may lie and still be used.
        /// </summary>
        internal static readonly TimeSpan Overlap = TimeSpan.FromHours(1);

        /// <summary>The most pages one poll reads. The first poll of an indexer reads one.</summary>
        internal const int PageBound = 10;

        public async Task<IndexerFeedPollResult> PollAsync(
            IIndexerFeedProvider feed,
            Indexer indexer,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(feed);
            ArgumentNullException.ThrowIfNull(indexer);
            var pollUtc = timeProvider.GetUtcNow().UtcDateTime;
            var existing = (await store.GetStatusAsync(indexer.Id, gapLimit: 0, cancellationToken)).Checkpoint;
            // A row that holds neither a date nor an item has nothing to read back to, so the
            // poll is a baseline: it reads one page and records no gap.
            var checkpoint = existing is { NewestPublishedUtc: not null } or { NewestReleaseId: not null } ? existing : null;
            var window = new FeedWindow(pollUtc + Overlap);
            var pageLimit = checkpoint is null ? 1 : PageBound;
            var offset = 0;
            var reachedCheckpoint = false;
            for (var page = 0; page < pageLimit; page++)
            {
                var context = new IndexerSearchContext(SearchOrigin.Discovery, CycleAllowance: null)
                {
                    QueryKind = page == 0 ? IndexerRequestKind.FeedPoll : IndexerRequestKind.FeedPage
                };
                var result = await feed.FetchPageAsync(indexer, offset, context, cancellationToken);
                if (result.Status == IndexerSearchStatus.Cancelled)
                {
                    return new IndexerFeedPollResult(IndexerSearchStatus.Cancelled, page, 0, false, null);
                }

                if (result.Status != IndexerSearchStatus.Completed)
                {
                    // The pages already read are stored, but the checkpoint stays: the next poll
                    // reads the same window again.
                    var kept = await CommitAsync(indexer.Id, window, advance: null, gap: null, pollUtc, cancellationToken);
                    logger.LogInformation(
                        "Discovery poll of indexer {IndexerName} stopped at page {Page} ({Status}: {Reason}); the checkpoint is unchanged",
                        indexer.Name,
                        page,
                        result.Status,
                        result.FailureReason ?? result.Deferral?.Reason.ToString());
                    return new IndexerFeedPollResult(result.Status, page, kept, false, null);
                }

                window.Add(result.Items, isFirstPage: page == 0, checkpoint?.NewestReleaseId);
                offset += result.ItemsReceived;
                if (checkpoint is not null && PageReachesCheckpoint(result.Items, checkpoint, window.FutureCutoffUtc))
                {
                    reachedCheckpoint = true;
                    break;
                }

                if (result.ItemsReceived == 0 || (result.TotalAvailable is int total && offset >= total))
                {
                    break;
                }
            }

            var gap = checkpoint is not null && !reachedCheckpoint ? FindGap(checkpoint, window, pollUtc) : null;
            var inserted = await CommitAsync(indexer.Id, window, Advance(checkpoint, window), gap?.Span, pollUtc, cancellationToken);
            if (gap is not null)
            {
                LogGap(indexer, gap);
            }

            logger.LogDebug(
                "Discovery poll of indexer {IndexerName}: {Pages} pages, {Stored} new releases",
                indexer.Name,
                window.PagesRead,
                inserted);
            return new IndexerFeedPollResult(IndexerSearchStatus.Completed, window.PagesRead, inserted, true, gap?.Span);
        }

        /// <summary>
        /// Whether a page closes the window. On a page with usable dates, only the dates decide:
        /// the page reaches back to an Overlap before the checkpoint. Otherwise, or while the
        /// checkpoint has no date, the page closes the window when it holds the checkpoint item.
        /// </summary>
        private static bool PageReachesCheckpoint(
            IReadOnlyList<DiscoveredFeedItem> items,
            IndexerDiscoveryCheckpoint checkpoint,
            DateTime futureCutoffUtc)
        {
            var oldestUsableUtc = items
                .Select(item => item.PublishedUtc)
                .Where(published => published <= futureCutoffUtc)
                .Min();
            if (oldestUsableUtc is not null && checkpoint.NewestPublishedUtc is not null)
            {
                return oldestUsableUtc <= checkpoint.NewestPublishedUtc - Overlap;
            }

            return checkpoint.NewestReleaseId is not null
                && items.Any(item => item.ReleaseId == checkpoint.NewestReleaseId);
        }

        /// <summary>
        /// The stretch a poll that ended without reaching the checkpoint could not cover, or null
        /// when what it fetched shows that nothing was missed: its oldest usable date is at or
        /// before the checkpoint's, or, without dates to compare, it saw the checkpoint item.
        /// </summary>
        private static FeedGap? FindGap(IndexerDiscoveryCheckpoint checkpoint, FeedWindow window, DateTime pollUtc)
        {
            if (checkpoint.NewestPublishedUtc is DateTime checkpointUtc && window.OldestUsableUtc is DateTime oldestUtc)
            {
                return oldestUtc > checkpointUtc
                    ? new FeedGap(new DiscoveryCoverageGapSpan(checkpointUtc, oldestUtc), PollTimeBounds: false)
                    : null;
            }

            return window.SawCheckpointRelease
                ? null
                : new FeedGap(
                    new DiscoveryCoverageGapSpan(checkpoint.LastSuccessUtc ?? pollUtc, pollUtc),
                    PollTimeBounds: true);
        }

        /// <summary>
        /// The checkpoint after a poll that may advance: the newest usable date seen so far and
        /// the item that carries it. When the first page has no usable date, the checkpoint item
        /// is that page's first item that is not dated in the future, so an undated feed can find
        /// it again.
        /// </summary>
        private static DiscoveryCheckpointAdvance Advance(IndexerDiscoveryCheckpoint? checkpoint, FeedWindow window)
        {
            var newest = window.NewestUsable;
            var advance = newest is not null
                && (checkpoint?.NewestPublishedUtc is null || newest.PublishedUtc > checkpoint.NewestPublishedUtc)
                ? new DiscoveryCheckpointAdvance(newest.PublishedUtc, newest.ReleaseId)
                : new DiscoveryCheckpointAdvance(checkpoint?.NewestPublishedUtc, checkpoint?.NewestReleaseId);
            return window.FirstPageUndatedReleaseId is null
                ? advance
                : advance with { NewestReleaseId = window.FirstPageUndatedReleaseId };
        }

        private Task<int> CommitAsync(
            int indexerId,
            FeedWindow window,
            DiscoveryCheckpointAdvance? advance,
            DiscoveryCoverageGapSpan? gap,
            DateTime pollUtc,
            CancellationToken cancellationToken) =>
            store.CommitPollAsync(
                indexerId,
                window.Items
                    .Select(item => new DiscoveredReleaseCandidate(
                        item.ReleaseId,
                        item.PublishedUtc,
                        item.Result.Title,
                        JsonSerializer.Serialize(item.Result)))
                    .ToList(),
                advance,
                gap,
                window.FutureDatedCount,
                window.UndatedCount,
                pollUtc,
                cancellationToken);

        private void LogGap(Indexer indexer, FeedGap gap)
        {
            if (gap.PollTimeBounds)
            {
                logger.LogWarning(
                    "Indexer {IndexerName} recent-release discovery didn't cover the period between {GapStartUtc} and {GapEndUtc} UTC. Search may be required. The feed has no usable dates, so these bounds are local poll times.",
                    indexer.Name,
                    gap.Span.GapStartUtc,
                    gap.Span.GapEndUtc);
                return;
            }

            logger.LogWarning(
                "Indexer {IndexerName} recent-release discovery didn't cover the period between {GapStartUtc} and {GapEndUtc} UTC. Search may be required.",
                indexer.Name,
                gap.Span.GapStartUtc,
                gap.Span.GapEndUtc);
        }

        private sealed record FeedGap(DiscoveryCoverageGapSpan Span, bool PollTimeBounds);

        /// <summary>
        /// Everything one poll has read so far. An item with no publish date, or one dated later
        /// than <see cref="FutureCutoffUtc"/>, is undated: it is stored, but never ordered and
        /// never the checkpoint's date.
        /// </summary>
        private sealed class FeedWindow(DateTime futureCutoffUtc)
        {
            private readonly List<DiscoveredFeedItem> _items = [];

            public DateTime FutureCutoffUtc { get; } = futureCutoffUtc;

            public IReadOnlyList<DiscoveredFeedItem> Items => _items;

            public int PagesRead { get; private set; }

            public int FutureDatedCount { get; private set; }

            public int UndatedCount { get; private set; }

            public DiscoveredFeedItem? NewestUsable { get; private set; }

            public DateTime? OldestUsableUtc { get; private set; }

            public bool SawCheckpointRelease { get; private set; }

            /// <summary>Set when the first page has no usable date.</summary>
            public string? FirstPageUndatedReleaseId { get; private set; }

            public void Add(IReadOnlyList<DiscoveredFeedItem> items, bool isFirstPage, string? checkpointReleaseId)
            {
                PagesRead++;
                _items.AddRange(items);
                var pageHasUsableDate = false;
                foreach (var item in items)
                {
                    SawCheckpointRelease |= checkpointReleaseId is not null && item.ReleaseId == checkpointReleaseId;
                    if (item.PublishedUtc is not DateTime publishedUtc || publishedUtc > FutureCutoffUtc)
                    {
                        UndatedCount++;
                        FutureDatedCount += item.PublishedUtc is null ? 0 : 1;
                        continue;
                    }

                    pageHasUsableDate = true;
                    if (NewestUsable is null || publishedUtc > NewestUsable.PublishedUtc)
                    {
                        NewestUsable = item;
                    }

                    if (OldestUsableUtc is null || publishedUtc < OldestUsableUtc)
                    {
                        OldestUsableUtc = publishedUtc;
                    }
                }

                if (isFirstPage && !pageHasUsableDate && items.Count > 0)
                {
                    FirstPageUndatedReleaseId = (items.FirstOrDefault(item => item.PublishedUtc is null) ?? items[0]).ReleaseId;
                }
            }
        }
    }
}
