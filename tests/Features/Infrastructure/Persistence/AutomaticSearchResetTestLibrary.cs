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
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// Books in no-result backoff for the metadata-change reset tests: each is due a day from now,
/// has failed twice, and has a pair row on each of two indexers that is not due before then.
/// </summary>
internal sealed class AutomaticSearchResetTestLibrary(AutomaticSearchScheduleTestDatabase database)
{
    public static readonly DateTime NowUtc = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime LaterUtc = NowUtc.AddHours(24);
    public const string Title = "A Princess of Mars";
    public const string Author = "Edgar Rice Burroughs";
    public const string Series = "Barsoom";
    public const string Asin = "B008DFUGCQ";
    public const int FailureCount = 2;

    private int[] _indexerIds = [];

    public int ProfileId { get; private set; }

    public int OtherProfileId { get; private set; }

    public async Task InitializeAsync()
    {
        await database.MigrateAsync();
        _indexerIds =
        [
            await database.SeedIndexerAsync("Indexer A"),
            await database.SeedIndexerAsync("Indexer B")
        ];
        await using var context = database.CreateContext();
        var profile = new QualityProfile { Name = "Any" };
        var other = new QualityProfile { Name = "Other" };
        context.QualityProfiles.AddRange(profile, other);
        await context.SaveChangesAsync();
        (ProfileId, OtherProfileId) = (profile.Id, other.Id);
    }

    public async Task<int> SeedBookInBackoffAsync(bool monitored = true)
    {
        int bookId;
        await using (var context = database.CreateContext())
        {
            var book = new AudiobookBuilder()
                .WithTitle(Title)
                .WithAuthor(Author)
                .WithSeries(Series)
                .WithAsin(Asin)
                .WithMonitored(monitored)
                .Build();
            book.QualityProfileId = ProfileId;
            book.AutomaticSearchDueUtc = LaterUtc;
            book.AutomaticSearchFailureCount = FailureCount;
            context.Audiobooks.Add(book);
            await context.SaveChangesAsync();
            bookId = book.Id;
        }

        foreach (var indexerId in _indexerIds)
        {
            await database.SeedPairAsync(new AudiobookIndexerSearchState
            {
                AudiobookId = bookId,
                IndexerId = indexerId,
                NextAttemptUtc = LaterUtc,
                NoResultCount = 3,
                LastOutcome = "NoAcceptableResults"
            });
        }

        return bookId;
    }

    /// <summary>The book's schedule as committed, read through a fresh context.</summary>
    public async Task<(DateTime DueUtc, int FailureCount, int PairRows)> ReadScheduleAsync(int bookId)
    {
        var book = await database.ReadBookAsync(bookId);
        await using var context = database.CreateContext();
        var pairRows = await context.AudiobookIndexerSearchStates.AsNoTracking()
            .CountAsync(state => state.AudiobookId == bookId);
        return (book.DueUtc, book.FailureCount, pairRows);
    }

    public static (DateTime DueUtc, int FailureCount, int PairRows) Reset => (NowUtc, 0, 0);

    public static (DateTime DueUtc, int FailureCount, int PairRows) Untouched => (LaterUtc, FailureCount, 2);
}
