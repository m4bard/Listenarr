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
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// The schedule writes on EF InMemory, the default test host, which has no set-based updates or
/// transactions: the store falls back to tracked saves there, and the values must still land.
/// </summary>
[Trait("Name", "EfAutomaticSearchScheduleStoreInMemoryWritesTests")]
[Trait("Category", "Persistence")]
public sealed class EfAutomaticSearchScheduleStoreInMemoryWritesTests : BaseTests
{
    private static readonly DateTime NowUtc = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SeededUtc = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Cooldown = TimeSpan.FromHours(24);

    private readonly DbContextOptions<ListenArrDbContext> _options = new DbContextOptionsBuilder<ListenArrDbContext>()
        .UseInMemoryDatabase($"schedule-writes-{Guid.NewGuid():N}")
        .Options;

    [Fact]
    public async Task PersistPairAndBook_LandOnInMemory()
    {
        int book, searched, neverSearched;
        await using (var context = new ListenArrDbContext(_options))
        {
            var audiobook = new AudiobookBuilder().WithTitle("A Princess of Mars").WithMonitored().Build();
            audiobook.AutomaticSearchDueUtc = SeededUtc;
            var first = new IndexerBuilder().WithName("Searched").WithEnabled().Build();
            var second = new IndexerBuilder().WithName("Never searched").WithEnabled().Build();
            context.AddRange(audiobook, first, second);
            await context.SaveChangesAsync();
            (book, searched, neverSearched) = (audiobook.Id, first.Id, second.Id);
        }

        await using (var context = new ListenArrDbContext(_options))
        {
            var store = new EfAutomaticSearchScheduleStore(context);
            await store.PersistPairAsync(
                new PairScheduleUpdate(book, searched, PairScheduleResult.NoAcceptableResults), NowUtc, Cooldown);
            await store.PersistPairAsync(
                new PairScheduleUpdate(book, searched, PairScheduleResult.NoAcceptableResults), NowUtc, Cooldown);
            await store.PersistPairAsync(
                new PairScheduleUpdate(book, neverSearched, PairScheduleResult.Failed), NowUtc, Cooldown);
            await store.PersistBookAsync(
                new BookScheduleUpdate(book, BookSearchOutcome.BookFailed, AnyPairCompleted: true), NowUtc, Cooldown);
        }

        await using var reader = new ListenArrDbContext(_options);
        var pairs = await reader.AudiobookIndexerSearchStates.AsNoTracking()
            .OrderBy(pair => pair.IndexerId)
            .Select(pair => new { pair.IndexerId, pair.NoResultCount, pair.FailureCount, pair.NextAttemptUtc })
            .ToListAsync();
        Assert.Equal(
            [new { IndexerId = searched, NoResultCount = 2, FailureCount = 0, NextAttemptUtc = NowUtc.AddHours(48) },
             new { IndexerId = neverSearched, NoResultCount = 0, FailureCount = 1, NextAttemptUtc = NowUtc.AddHours(1) }],
            pairs);
        var stored = await reader.Audiobooks.AsNoTracking().SingleAsync(candidate => candidate.Id == book);
        Assert.Equal(
            (NowUtc.AddMinutes(15), 1, (DateTime?)NowUtc),
            (stored.AutomaticSearchDueUtc, stored.AutomaticSearchFailureCount, stored.LastSearchTime));
    }
}
