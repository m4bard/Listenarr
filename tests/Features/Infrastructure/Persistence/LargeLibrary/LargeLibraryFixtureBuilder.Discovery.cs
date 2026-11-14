/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Domain.Search.Discovery;
using Microsoft.Data.Sqlite;

namespace Listenarr.Tests.Features.Infrastructure.Persistence.LargeLibrary;

public sealed partial class LargeLibraryFixtureBuilder
{
    public const int DiscoveryIndexerCount = 5;
    public const int DiscoveredReleasesPerIndexer = 50_000;
    public const int DiscoveredReleaseCount = DiscoveryIndexerCount * DiscoveredReleasesPerIndexer;
    public const int CoverageGapCount = 10_000;

    // One release in 100 (1 percent) is still Pending, one in 500 (0.2 percent) is Matched;
    // the rest alternate between Handled and Ignored.
    private const int PendingReleaseInterval = 100;
    private const int MatchedReleaseInterval = 500;
    private const int DiscoveryWindowDays = 60;

    /// <summary>
    /// Adds the discovery rows to a fixture this builder built: five Torznab indexers with a
    /// checkpoint each, the discovered releases spread over the last 60 days (the newest first),
    /// and the coverage gaps. Call it after <see cref="BuildAsync"/>; the same clock gives the
    /// same rows. The rows are written in raw parameterised batches in one transaction.
    /// </summary>
    public async Task<LargeLibraryDiscoveryCounts> SeedDiscoveryAsync(
        LargeLibraryFixture fixture,
        CancellationToken cancellationToken = default)
    {
        var books = LoadBooks();
        var indexerIds = await AddDiscoveryIndexersAsync(fixture, cancellationToken);
        var counts = new LargeLibraryDiscoveryCounts(indexerIds);
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await InsertDiscoveredReleasesAsync(connection, transaction, books, indexerIds, counts, cancellationToken);
        await InsertCoverageGapsAsync(connection, transaction, indexerIds, cancellationToken);
        await InsertCheckpointsAsync(connection, transaction, indexerIds, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return counts;
    }

    private static async Task<IReadOnlyList<int>> AddDiscoveryIndexersAsync(
        LargeLibraryFixture fixture,
        CancellationToken cancellationToken)
    {
        var indexers = Enumerable.Range(0, DiscoveryIndexerCount)
            .Select(number => new Indexer
            {
                Name = $"Feed {number}",
                Type = "Torrent",
                Implementation = "Torznab",
                Url = $"https://feed-{number}.example"
            })
            .ToList();
        await using var context = new ListenArrDbContext(fixture.CreateOptions());
        context.Indexers.AddRange(indexers);
        await context.SaveChangesAsync(cancellationToken);
        return indexers.Select(indexer => indexer.Id).ToList();
    }

    private async Task InsertDiscoveredReleasesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<LargeLibraryBook> books,
        IReadOnlyList<int> indexerIds,
        LargeLibraryDiscoveryCounts counts,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO "DiscoveredReleases"
                ("IndexerId", "ReleaseId", "PublishedUtc", "Title", "Payload", "State",
                 "FirstSeenUtc", "HandledUtc", "MatchedAudiobookId", "Outcome")
            VALUES ($indexerId, $releaseId, $published, $title, '{}', $state,
                 $firstSeen, $handled, $audiobookId, $outcome);
            """;
        var indexerId = command.Parameters.Add("$indexerId", SqliteType.Integer);
        var releaseId = command.Parameters.Add("$releaseId", SqliteType.Text);
        var published = command.Parameters.Add("$published", SqliteType.Text);
        var title = command.Parameters.Add("$title", SqliteType.Text);
        var state = command.Parameters.Add("$state", SqliteType.Text);
        var firstSeen = command.Parameters.Add("$firstSeen", SqliteType.Text);
        var handled = command.Parameters.Add("$handled", SqliteType.Text);
        var audiobookId = command.Parameters.Add("$audiobookId", SqliteType.Integer);
        var outcome = command.Parameters.Add("$outcome", SqliteType.Text);
        await command.PrepareAsync(cancellationToken);

        for (var index = 0; index < DiscoveredReleaseCount; index++)
        {
            var feed = index % DiscoveryIndexerCount;
            var releaseState = ReleaseState(index);
            var seenUtc = DiscoverySeenUtc(index);
            indexerId.Value = indexerIds[feed];
            releaseId.Value = $"fixture-{feed}-{index}";
            published.Value = seenUtc;
            // Releases cycle through the corpus, verbatim.
            title.Value = books[index % books.Count].Title;
            state.Value = releaseState.ToString();
            firstSeen.Value = seenUtc;
            var isTerminal = releaseState is DiscoveredReleaseState.Handled or DiscoveredReleaseState.Ignored;
            handled.Value = isTerminal ? seenUtc : DBNull.Value;
            audiobookId.Value = releaseState == DiscoveredReleaseState.Matched
                ? AudiobookId(index % AudiobookCount)
                : DBNull.Value;
            outcome.Value = releaseState switch
            {
                DiscoveredReleaseState.Handled => "Queued",
                DiscoveredReleaseState.Ignored => "NoMatch",
                _ => DBNull.Value
            };
            await command.ExecuteNonQueryAsync(cancellationToken);
            counts.Record(releaseState);
        }
    }

    private async Task InsertCoverageGapsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<int> indexerIds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO "IndexerCoverageGaps"
                ("IndexerId", "GapStartUtc", "GapEndUtc", "DetectedUtc")
            VALUES ($indexerId, $start, $end, $detected);
            """;
        var indexerId = command.Parameters.Add("$indexerId", SqliteType.Integer);
        var start = command.Parameters.Add("$start", SqliteType.Text);
        var end = command.Parameters.Add("$end", SqliteType.Text);
        var detected = command.Parameters.Add("$detected", SqliteType.Text);
        await command.PrepareAsync(cancellationToken);

        for (var index = 0; index < CoverageGapCount; index++)
        {
            var detectedUtc = ClockUtc.AddSeconds(
                -(long)index * DiscoveryWindowDays * 86_400 / CoverageGapCount);
            indexerId.Value = indexerIds[index % DiscoveryIndexerCount];
            start.Value = detectedUtc.AddMinutes(-30);
            end.Value = detectedUtc.AddMinutes(-5);
            detected.Value = detectedUtc;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task InsertCheckpointsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<int> indexerIds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO "IndexerDiscoveryCheckpoints"
                ("IndexerId", "NewestPublishedUtc", "NewestReleaseId", "LastPollUtc", "LastSuccessUtc")
            VALUES ($indexerId, $newest, $newestId, $poll, $poll);
            """;
        var indexerId = command.Parameters.Add("$indexerId", SqliteType.Integer);
        var newest = command.Parameters.Add("$newest", SqliteType.Text);
        var newestId = command.Parameters.Add("$newestId", SqliteType.Text);
        var poll = command.Parameters.Add("$poll", SqliteType.Text);
        await command.PrepareAsync(cancellationToken);

        for (var feed = 0; feed < DiscoveryIndexerCount; feed++)
        {
            // Index 0 is the newest release of feed 0, index 1 of feed 1, and so on.
            indexerId.Value = indexerIds[feed];
            newest.Value = DiscoverySeenUtc(feed);
            newestId.Value = $"fixture-{feed}-{feed}";
            poll.Value = ClockUtc;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    // Releases are spread evenly over the window, index 0 being the newest.
    private DateTime DiscoverySeenUtc(int index) => ClockUtc.AddSeconds(
        -(long)index * DiscoveryWindowDays * 86_400 / DiscoveredReleaseCount);

    private static DiscoveredReleaseState ReleaseState(int index)
    {
        if (index % PendingReleaseInterval == 0)
        {
            return DiscoveredReleaseState.Pending;
        }

        if (index % MatchedReleaseInterval == 1)
        {
            return DiscoveredReleaseState.Matched;
        }

        return index % 2 == 0 ? DiscoveredReleaseState.Handled : DiscoveredReleaseState.Ignored;
    }
}

/// <summary>Row counts and indexer ids of the discovery rows of a large library.</summary>
public sealed class LargeLibraryDiscoveryCounts(IReadOnlyList<int> indexerIds)
{
    private readonly Dictionary<DiscoveredReleaseState, int> _byState = [];

    public IReadOnlyList<int> IndexerIds { get; } = indexerIds;

    public int Total => _byState.Values.Sum();

    public int InState(DiscoveredReleaseState state) => _byState.GetValueOrDefault(state);

    internal void Record(DiscoveredReleaseState state) =>
        _byState[state] = InState(state) + 1;
}
