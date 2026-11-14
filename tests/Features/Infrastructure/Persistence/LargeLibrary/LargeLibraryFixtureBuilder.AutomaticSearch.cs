/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Application.Search.Scheduling;
using Microsoft.Data.Sqlite;

namespace Listenarr.Tests.Features.Infrastructure.Persistence.LargeLibrary;

public sealed partial class LargeLibraryFixtureBuilder
{
    public const int SearchIndexerCount = 5;
    public const int SearchPairCount = AudiobookCount * SearchIndexerCount;

    // The search schedule draws use a stream of their own, so stamping leaves
    // the rows that BuildAsync generated unchanged.
    private const int AutomaticSearchSeedOffset = 2;
    private static readonly TimeSpan SearchDueWindow = TimeSpan.FromDays(30);

    /// <summary>
    /// Gives every book a quality profile and an automatic-search due time,
    /// adds the indexers enabled for automatic search and one search state
    /// row per book and indexer, on a fixture this builder built. Call it
    /// after <see cref="BuildAsync"/>; the same clock and seed give the same
    /// rows.
    /// </summary>
    public async Task<LargeLibraryAutomaticSearchCounts> StampAutomaticSearchScheduleAsync(
        LargeLibraryFixture fixture,
        CancellationToken cancellationToken = default)
    {
        int profileId;
        int[] indexerIds;
        await using (var context = new ListenArrDbContext(fixture.CreateOptions()))
        {
            var profile = new QualityProfile { Name = "Any" };
            var indexers = Enumerable.Range(1, SearchIndexerCount)
                .Select(number => new Indexer
                {
                    Name = $"Indexer {number}",
                    Type = "torrent",
                    Implementation = "Custom",
                    Url = $"https://indexer-{number}.example/",
                    IsEnabled = true,
                    EnableAutomaticSearch = true,
                    AdditionalSettings = "{}"
                })
                .ToList();
            context.QualityProfiles.Add(profile);
            context.Indexers.AddRange(indexers);
            await context.SaveChangesAsync(cancellationToken);
            profileId = profile.Id;
            indexerIds = [.. indexers.Select(indexer => indexer.Id)];
        }

        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var book = connection.CreateCommand();
        book.Transaction = transaction;
        book.CommandText = """
            UPDATE "Audiobooks" SET "QualityProfileId" = $profile, "AutomaticSearchDueUtc" = $due
            WHERE "Id" = $id;
            """;
        var bookId = book.Parameters.Add("$id", SqliteType.Integer);
        var due = book.Parameters.Add("$due", SqliteType.Text);
        book.Parameters.Add("$profile", SqliteType.Integer).Value = profileId;
        await book.PrepareAsync(cancellationToken);

        await using var pair = connection.CreateCommand();
        pair.Transaction = transaction;
        pair.CommandText = """
            INSERT INTO "AudiobookIndexerSearchStates"
                ("AudiobookId", "IndexerId", "NextAttemptUtc", "NoResultCount", "FailureCount",
                 "LastOutcome", "LastCompletedUtc")
            VALUES ($audiobookId, $indexerId, $nextAttempt, $noResults, $failures,
                 $outcome, $completed);
            """;
        var pairBookId = pair.Parameters.Add("$audiobookId", SqliteType.Integer);
        var indexerId = pair.Parameters.Add("$indexerId", SqliteType.Integer);
        var nextAttempt = pair.Parameters.Add("$nextAttempt", SqliteType.Text);
        var noResults = pair.Parameters.Add("$noResults", SqliteType.Integer);
        var failures = pair.Parameters.Add("$failures", SqliteType.Integer);
        var outcome = pair.Parameters.Add("$outcome", SqliteType.Text);
        var completed = pair.Parameters.Add("$completed", SqliteType.Text);
        await pair.PrepareAsync(cancellationToken);

        var random = new Random(_seed + AutomaticSearchSeedOffset);
        var outcomes = Enum.GetValues<PairScheduleResult>();
        var counts = new LargeLibraryAutomaticSearchCounts();
        for (var index = 0; index < AudiobookCount; index++)
        {
            var dueUtc = SpreadAroundClock(random);
            bookId.Value = AudiobookId(index);
            due.Value = dueUtc;
            await book.ExecuteNonQueryAsync(cancellationToken);
            counts.RecordBook(index % UnmonitoredAudiobookInterval != 0, dueUtc <= ClockUtc);

            for (var position = 0; position < indexerIds.Length; position++)
            {
                // Outcomes rotate per book and indexer, so every outcome sits
                // on every indexer.
                var result = outcomes[(index + position) % outcomes.Length];
                var finished = result is PairScheduleResult.Satisfied
                    or PairScheduleResult.NoAcceptableResults
                    or PairScheduleResult.GrabNotCompleted;
                pairBookId.Value = AudiobookId(index);
                indexerId.Value = indexerIds[position];
                nextAttempt.Value = SpreadAroundClock(random);
                noResults.Value = result == PairScheduleResult.NoAcceptableResults ? 1 : 0;
                failures.Value = result == PairScheduleResult.Failed ? 1 : 0;
                outcome.Value = result.ToString();
                completed.Value = finished
                    ? ClockUtc - TimeSpan.FromSeconds(random.NextDouble() * SearchDueWindow.TotalSeconds)
                    : DBNull.Value;
                await pair.ExecuteNonQueryAsync(cancellationToken);
                counts.RecordPair();
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return counts;
    }

    // Half of the times are past and half future, within the window either
    // side of the clock.
    private DateTime SpreadAroundClock(Random random)
    {
        var offset = TimeSpan.FromSeconds(random.NextDouble() * SearchDueWindow.TotalSeconds);
        return random.Next(2) == 0 ? ClockUtc - offset : ClockUtc + offset;
    }
}

/// <summary>Automatic-search row counts of a stamped large library, for test assertions.</summary>
public sealed class LargeLibraryAutomaticSearchCounts
{
    public int DueEligibleBooks { get; private set; }
    public int PairRows { get; private set; }

    internal void RecordBook(bool monitored, bool due)
    {
        if (monitored && due)
        {
            DueEligibleBooks++;
        }
    }

    internal void RecordPair() => PairRows++;
}
