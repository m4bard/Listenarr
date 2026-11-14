/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Builders;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// The automatic-search schedule columns are written only by set-based updates. A whole-entity
/// save made from a copy read before such an update must not put the old schedule back. Runs on
/// migrated SQLite, the production provider: EF InMemory does not honour after-save Ignore on
/// these paths.
/// </summary>
[Trait("Name", "AutomaticSearchScheduleSaveBehaviorTests")]
[Trait("Category", "Persistence")]
public sealed class AutomaticSearchScheduleSaveBehaviorTests : BaseTests
{
    private static readonly DateTime SeededDueUtc = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime StoreDueUtc = new(2026, 4, 2, 10, 30, 0, DateTimeKind.Utc);
    private const int StoreFailureCount = 3;

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await _connection.OpenAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public override async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task StaleDetachedUpdate_KeepsTheScheduleTheStoreWrote()
    {
        var bookId = await SeedBookAsync();
        Audiobook stale;
        await using (var reader = CreateContext())
        {
            stale = await reader.Audiobooks.AsNoTracking().SingleAsync(book => book.Id == bookId);
        }
        Assert.Equal((SeededDueUtc, 0), (stale.AutomaticSearchDueUtc, stale.AutomaticSearchFailureCount));

        await WriteScheduleAsTheStoreDoesAsync(bookId);
        Assert.Equal((StoreDueUtc, StoreFailureCount), await ReadScheduleAsync(bookId));

        stale.Monitored = false;
        await using (var editor = CreateContext())
        {
            Assert.True(await new AudiobookRepository(editor).UpdateAsync(stale));
        }

        Assert.Equal((StoreDueUtc, StoreFailureCount), await ReadScheduleAsync(bookId));
        Assert.False(await ReadMonitoredAsync(bookId));
    }

    [Fact]
    public async Task StaleTrackedWholeEntityUpdate_KeepsTheScheduleTheStoreWrote()
    {
        var bookId = await SeedBookAsync();
        await using var editor = CreateContext();
        var stale = await editor.Audiobooks.SingleAsync(book => book.Id == bookId);

        await WriteScheduleAsTheStoreDoesAsync(bookId);
        Assert.Equal((StoreDueUtc, StoreFailureCount), await ReadScheduleAsync(bookId));

        // Update marks every property modified, so the stale tracked copy is written whole.
        stale.Monitored = false;
        editor.Audiobooks.Update(stale);
        Assert.True(await new AudiobookRepository(editor).UpdateAsync(stale));

        Assert.Equal((StoreDueUtc, StoreFailureCount), await ReadScheduleAsync(bookId));
        Assert.False(await ReadMonitoredAsync(bookId));
    }

    private ListenArrDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ListenArrDbContext>().UseSqlite(_connection).Options);

    private async Task<int> SeedBookAsync()
    {
        await using var context = CreateContext();
        var book = new AudiobookBuilder().WithTitle("A Princess of Mars").WithMonitored().Build();
        book.AutomaticSearchDueUtc = SeededDueUtc;
        context.Audiobooks.Add(book);
        await context.SaveChangesAsync();
        return book.Id;
    }

    private async Task WriteScheduleAsTheStoreDoesAsync(int bookId)
    {
        await using var context = CreateContext();
        var affected = await context.Audiobooks
            .Where(book => book.Id == bookId)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(book => book.AutomaticSearchDueUtc, StoreDueUtc)
                .SetProperty(book => book.AutomaticSearchFailureCount, StoreFailureCount));
        Assert.Equal(1, affected);
    }

    private async Task<(DateTime DueUtc, int FailureCount)> ReadScheduleAsync(int bookId)
    {
        await using var context = CreateContext();
        var schedule = await context.Audiobooks.AsNoTracking()
            .Where(book => book.Id == bookId)
            .Select(book => new { book.AutomaticSearchDueUtc, book.AutomaticSearchFailureCount })
            .SingleAsync();
        return (schedule.AutomaticSearchDueUtc, schedule.AutomaticSearchFailureCount);
    }

    private async Task<bool> ReadMonitoredAsync(int bookId)
    {
        await using var context = CreateContext();
        return await context.Audiobooks.AsNoTracking()
            .Where(book => book.Id == bookId)
            .Select(book => book.Monitored)
            .SingleAsync();
    }
}
