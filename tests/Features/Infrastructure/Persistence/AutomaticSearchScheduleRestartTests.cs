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
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// The restart half of the durable schedule: what one store instance persisted, a new store on a
/// new context over the same database file selects again in the same way, and an indexer whose
/// search completed for a book is not asked again before its own next attempt.
/// </summary>
[Trait("Name", "AutomaticSearchScheduleRestartTests")]
[Trait("Category", "Persistence")]
public sealed class AutomaticSearchScheduleRestartTests : BaseTests
{
    private static readonly DateTime NowUtc = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Cooldown = TimeSpan.FromHours(24);

    private AutomaticSearchScheduleTestDatabase _database = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _database = new AutomaticSearchScheduleTestDatabase(FileService.GetTempPath());
        await _database.MigrateAsync();
    }

    [Fact]
    public async Task Restart_PreservesPairAndBookState()
    {
        var searched = await _database.SeedBookAsync(NowUtc.AddHours(-1));
        var waiting = await _database.SeedBookAsync(NowUtc.AddMinutes(30));
        var completed = await _database.SeedIndexerAsync("Completed");
        var failed = await _database.SeedIndexerAsync("Failed");
        var failedRetryUtc = NowUtc.AddHours(1);

        Selection beforeRestart;
        await using (var context = _database.CreateContext())
        {
            var store = new EfAutomaticSearchScheduleStore(context);
            await store.PersistPairAsync(
                new PairScheduleUpdate(searched, completed, PairScheduleResult.NoAcceptableResults), NowUtc, Cooldown);
            await store.PersistPairAsync(
                new PairScheduleUpdate(searched, failed, PairScheduleResult.Failed), NowUtc, Cooldown);
            await store.PersistBookAsync(
                new BookScheduleUpdate(searched, BookSearchOutcome.PartiallyCompleted, AnyPairCompleted: true), NowUtc, Cooldown);

            beforeRestart = await SelectAsync(store, failedRetryUtc, [completed, failed]);
            Assert.Empty((await SelectAsync(store, NowUtc, [completed, failed])).Books);
        }

        await using (var context = _database.CreateContext())
        {
            var afterRestart = await SelectAsync(new EfAutomaticSearchScheduleStore(context), failedRetryUtc, [completed, failed]);

            Assert.Equal(beforeRestart, afterRestart);
            Assert.Equal([waiting, searched], afterRestart.Books.Select(book => book.Id));
            Assert.Equal([completed, failed], afterRestart.Books[0].DueIndexers);
            Assert.Equal([failed], afterRestart.Books[1].DueIndexers);
        }

        var stored = await _database.ReadBookAsync(searched);
        Assert.Equal((failedRetryUtc, NowUtc), (stored.DueUtc, stored.LastSearchUtc));
    }

    /// <summary>
    /// The books a cycle at <paramref name="atUtc"/> would pick, with the indexers each would ask:
    /// those with no pair row, or whose next attempt has come.
    /// </summary>
    private static async Task<Selection> SelectAsync(
        EfAutomaticSearchScheduleStore store, DateTime atUtc, IReadOnlyList<int> indexers)
    {
        var page = await store.GetDuePageAsync(atUtc, [], 10);
        var pairs = await store.GetPairStatesAsync(page.Select(book => book.Id).ToList());
        return new Selection(page
            .Select(book => new SelectedBook(book.Id, indexers
                .Where(indexer => !pairs.Any(pair => pair.AudiobookId == book.Id
                    && pair.IndexerId == indexer
                    && pair.NextAttemptUtc > atUtc))
                .ToList()))
            .ToList());
    }

    private sealed record SelectedBook(int Id, IReadOnlyList<int> DueIndexers)
    {
        public bool Equals(SelectedBook? other) =>
            other is not null && Id == other.Id && DueIndexers.SequenceEqual(other.DueIndexers);

        public override int GetHashCode() => Id;
    }

    private sealed record Selection(IReadOnlyList<SelectedBook> Books)
    {
        public bool Equals(Selection? other) => other is not null && Books.SequenceEqual(other.Books);

        public override int GetHashCode() => Books.Count;
    }
}
