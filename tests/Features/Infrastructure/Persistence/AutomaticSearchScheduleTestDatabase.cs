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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// A migrated, file-backed SQLite database for the automatic search's schedule tests. Every
/// context opens its own connection to the file, so a new context sees only what was committed.
/// </summary>
internal sealed class AutomaticSearchScheduleTestDatabase(string directory)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(directory, "schedule.db"),
        Pooling = false
    }.ToString();

    public ListenArrDbContext CreateContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(_connectionString)
            .AddInterceptors(interceptors)
            .Options);

    public async Task MigrateAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task<int> SeedBookAsync(DateTime dueUtc, int failureCount = 0, DateTime? lastSearchUtc = null)
    {
        await using var context = CreateContext();
        var profile = new QualityProfile { Name = "Any" };
        context.QualityProfiles.Add(profile);
        await context.SaveChangesAsync();

        var book = new AudiobookBuilder().WithTitle("A Princess of Mars").WithMonitored().Build();
        book.QualityProfileId = profile.Id;
        book.AutomaticSearchDueUtc = dueUtc;
        book.AutomaticSearchFailureCount = failureCount;
        book.LastSearchTime = lastSearchUtc;
        context.Audiobooks.Add(book);
        await context.SaveChangesAsync();
        return book.Id;
    }

    public async Task<int> SeedIndexerAsync(string name, bool enabled = true, bool automaticSearch = true)
    {
        await using var context = CreateContext();
        var builder = new IndexerBuilder().WithName(name);
        var indexer = (enabled ? builder.WithEnabled() : builder.WithDisabled()).Build();
        indexer.EnableAutomaticSearch = automaticSearch;
        context.Indexers.Add(indexer);
        await context.SaveChangesAsync();
        return indexer.Id;
    }

    public async Task SeedPairAsync(AudiobookIndexerSearchState state)
    {
        await using var context = CreateContext();
        context.AudiobookIndexerSearchStates.Add(state);
        await context.SaveChangesAsync();
    }

    public async Task<(DateTime DueUtc, int FailureCount, DateTime? LastSearchUtc, bool Monitored)> ReadBookAsync(int bookId)
    {
        await using var context = CreateContext();
        var book = await context.Audiobooks.AsNoTracking().SingleAsync(candidate => candidate.Id == bookId);
        return (book.AutomaticSearchDueUtc, book.AutomaticSearchFailureCount, book.LastSearchTime, book.Monitored);
    }

    public async Task<AudiobookIndexerSearchState?> ReadPairAsync(int bookId, int indexerId)
    {
        await using var context = CreateContext();
        return await context.AudiobookIndexerSearchStates.AsNoTracking()
            .SingleOrDefaultAsync(state => state.AudiobookId == bookId && state.IndexerId == indexerId);
    }
}
