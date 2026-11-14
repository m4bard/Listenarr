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
using Listenarr.Domain.Search.Discovery;
using Listenarr.Tests.Features.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search;

/// <summary>
/// A scripted feed, a settable clock, a log recorder and a SQLite file database behind the real
/// discovery store, shared by the feed poller tests.
/// </summary>
internal sealed class IndexerFeedPollerTestKit
{
    public static readonly DateTime BaselineUtc = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly DiscoveryStoreTestDatabase _database;

    private IndexerFeedPollerTestKit(DiscoveryStoreTestDatabase database)
    {
        _database = database;
        Indexer = new Indexer { Id = database.IndexerIds[0], Name = "Feed 1" };
    }

    public Indexer Indexer { get; }

    public ScriptedFeed Feed { get; } = new();

    public SettableTimeProvider Clock { get; } = new(BaselineUtc);

    public LogRecorder Log { get; } = new();

    public static async Task<IndexerFeedPollerTestKit> CreateAsync(string directory) =>
        new(await DiscoveryStoreTestDatabase.CreateAsync(directory));

    /// <summary>Polls once at <paramref name="pollUtc"/>, through a store with the given interceptors.</summary>
    public Task<IndexerFeedPollResult> PollAsync(DateTime pollUtc, params IInterceptor[] interceptors) =>
        PollAsync(pollUtc, CancellationToken.None, interceptors);

    public Task<IndexerFeedPollResult> PollAsync(
        DateTime pollUtc,
        CancellationToken cancellationToken,
        params IInterceptor[] interceptors)
    {
        Clock.UtcNow = pollUtc;
        var poller = new IndexerFeedPoller(_database.CreateStore(interceptors), Clock, Log);
        return poller.PollAsync(Feed, Indexer, cancellationToken);
    }

    public async Task<IndexerDiscoveryCheckpoint?> ReadCheckpointAsync()
    {
        await using var db = _database.CreateContext();
        return await db.IndexerDiscoveryCheckpoints.AsNoTracking()
            .SingleOrDefaultAsync(checkpoint => checkpoint.IndexerId == Indexer.Id);
    }

    public async Task<HashSet<string>> ReadReleaseIdsAsync()
    {
        await using var db = _database.CreateContext();
        return (await db.DiscoveredReleases.AsNoTracking().Select(release => release.ReleaseId).ToListAsync())
            .ToHashSet(StringComparer.Ordinal);
    }

    public async Task<List<IndexerCoverageGap>> ReadGapsAsync()
    {
        await using var db = _database.CreateContext();
        return await db.IndexerCoverageGaps.AsNoTracking().OrderBy(gap => gap.Id).ToListAsync();
    }

    /// <summary>A feed item named after <paramref name="number"/>.</summary>
    public static DiscoveredFeedItem Item(int number, DateTime? publishedUtc) =>
        new(
            new IndexerSearchResult
            {
                Id = $"guid-{number:D5}",
                Title = $"Release {number:D5}",
                TorrentUrl = $"https://indexer.example/download/{number}",
                DownloadType = "Torrent"
            },
            $"release-{number:D5}",
            publishedUtc);

    /// <summary>
    /// <paramref name="count"/> items numbered from <paramref name="first"/>, newest first: the
    /// first is dated <paramref name="newestUtc"/> and each next one <paramref name="spacing"/> older.
    /// </summary>
    public static List<DiscoveredFeedItem> Dated(int first, int count, DateTime newestUtc, TimeSpan spacing) =>
        Enumerable.Range(0, count).Select(index => Item(first + index, newestUtc - (spacing * index))).ToList();

    /// <summary><paramref name="count"/> items numbered from <paramref name="first"/>, with no dates.</summary>
    public static List<DiscoveredFeedItem> Undated(int first, int count) =>
        Enumerable.Range(first, count).Select(number => Item(number, null)).ToList();

    public static string ReleaseId(int number) => $"release-{number:D5}";
}

/// <summary>One request the poller made to the feed.</summary>
internal sealed record FeedRequest(int Offset, IndexerSearchContext Context);

/// <summary>
/// A feed held in memory, newest item first, served <see cref="PageSize"/> items at a time from
/// the requested offset. A page can be scripted to come back with another status.
/// </summary>
internal sealed class ScriptedFeed : IIndexerFeedProvider
{
    private readonly Dictionary<int, Func<IndexerFeedPage>> _scriptedPages = [];

    public List<DiscoveredFeedItem> Items { get; set; } = [];

    public int PageSize { get; set; } = 100;

    /// <summary>Whether pages state the feed's total, as newznab's response element does.</summary>
    public bool StatesTotal { get; set; }

    /// <summary>Release ids the indexer sends but the adapter leaves out (no link, no identity).</summary>
    public HashSet<string> LeftOut { get; } = [];

    /// <summary>The requests of the latest poll.</summary>
    public List<FeedRequest> Requests { get; } = [];

    public IReadOnlyList<int> Offsets => Requests.Select(request => request.Offset).ToList();

    /// <summary>Makes the next request for page <paramref name="pageIndex"/> of a poll come back as scripted.</summary>
    public void ScriptPage(int pageIndex, Func<IndexerFeedPage> page) => _scriptedPages[pageIndex] = page;

    public bool Supports(Indexer indexer) => true;

    public Task<IndexerFeedPage> FetchPageAsync(
        Indexer indexer,
        int offset,
        IndexerSearchContext context,
        CancellationToken cancellationToken)
    {
        // A poll starts with its FeedPoll request, so Requests holds the latest poll's only.
        if (context.QueryKind == IndexerRequestKind.FeedPoll)
        {
            Requests.Clear();
        }

        var pageIndex = Requests.Count;
        Requests.Add(new FeedRequest(offset, context));
        if (_scriptedPages.Remove(pageIndex, out var scripted))
        {
            return Task.FromResult(scripted());
        }

        var received = Items.Skip(offset).Take(PageSize).ToList();
        return Task.FromResult(new IndexerFeedPage(
            indexer.Id,
            IndexerSearchStatus.Completed,
            received.Where(item => !LeftOut.Contains(item.ReleaseId)).ToList(),
            received.Count,
            StatesTotal ? Items.Count : null));
    }

    public static IndexerFeedPage Failed(int indexerId, string reason) =>
        new(indexerId, IndexerSearchStatus.Failed, [], 0, null, FailureReason: reason);

    public static IndexerFeedPage Deferred(int indexerId, DateTime untilUtc) =>
        new(indexerId, IndexerSearchStatus.Deferred, [], 0, null,
            new RequestDeferral(untilUtc, RequestDeferralReason.CycleRequestBound));

    public static IndexerFeedPage Cancelled(int indexerId) =>
        new(indexerId, IndexerSearchStatus.Cancelled, [], 0, null);
}

internal sealed class SettableTimeProvider(DateTime utcNow) : TimeProvider
{
    public DateTime UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
}

/// <summary>Records every entry the poller logs.</summary>
internal sealed class LogRecorder : ILogger<IndexerFeedPoller>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IEnumerable<string> Warnings =>
        Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}
