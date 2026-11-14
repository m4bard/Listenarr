/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using System.Data.Common;
using System.Globalization;
using Listenarr.Infrastructure.DependencyInjection;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// Books that existed before the automatic-search schedule take the scaffolded due time when the
/// schedule migration runs. The startup repair gives each of them its own due time from its last
/// search and the configured cooldown, once, in bounded chunks. Runs on migrated SQLite.
/// </summary>
[Trait("Name", "AutomaticSearchScheduleStartupRepairTests")]
[Trait("Category", "Persistence")]
public sealed class AutomaticSearchScheduleStartupRepairTests : BaseTests
{
    private const string PreviousMigrationId = "20261029020400_AddReleaseBlocklist";
    private const string ScaffoldedDueText = "0001-01-01 00:00:00";
    private static readonly DateTime NowUtc = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task StartupRepair_KeepsCooldowns()
    {
        await using var connection = await OpenUpgradedAsync(
            (1, "'2026-11-13 11:00:00'"),
            (2, "'2026-01-05 07:00:00.1234567'"),
            (3, "NULL"),
            (4, "'not a date'"));
        await SeedSettingsAsync(connection, "24");
        await ExecuteAsync(connection, """
            INSERT INTO "Audiobooks" ("Id", "Monitored", "Explicit", "Abridged", "AutomaticSearchDueUtc")
            VALUES (5, 1, 0, 0, '2026-11-20 00:00:00');
            """);

        Assert.Equal(4, Repair(connection));

        var due = await ReadDueTextsAsync(connection);
        Assert.Equal("2026-11-14 11:00:00", due[1]);
        Assert.StartsWith("2026-01-06 07:00:00", due[2], StringComparison.Ordinal);
        Assert.Equal("2026-11-13 12:00:00", due[3]);
        Assert.Equal("2026-11-13 12:00:00", due[4]);
        Assert.Equal("2026-11-20 00:00:00", due[5]);
        Assert.Equal([2, 3, 4], await DueIdsAsync(connection, NowUtc));
        Assert.Equal([1, 2, 3, 4], await DueIdsAsync(connection, NowUtc.AddHours(23)));
        Assert.Equal("not a date", await ScalarAsync(connection, "SELECT \"LastSearchTime\" FROM \"Audiobooks\" WHERE \"Id\" = 4"));
    }

    [Theory]
    [InlineData("48", "2026-11-03 00:00:00")]
    [InlineData("9999", "2026-12-01 00:00:00")]
    [InlineData("0", "2026-11-01 01:00:00")]
    [InlineData("-5", "2026-11-01 01:00:00")]
    [InlineData("'many'", "2026-11-02 00:00:00")]
    [InlineData(null, "2026-11-02 00:00:00")]
    public async Task StartupRepair_ClampsTheCooldownLikeTheSweep(string? storedCooldownHours, string expectedDue)
    {
        await using var connection = await OpenUpgradedAsync((1, "'2026-11-01 00:00:00'"));
        if (storedCooldownHours is not null)
        {
            await SeedSettingsAsync(connection, storedCooldownHours);
        }

        Assert.Equal(1, Repair(connection));

        Assert.Equal(expectedDue, (await ReadDueTextsAsync(connection))[1]);
    }

    [Fact]
    public async Task StartupRepair_SecondRunLeavesProcessedBooksAlone()
    {
        await using var connection = await OpenUpgradedAsync((1, "'2026-11-13 11:00:00'"), (2, "NULL"));
        Assert.Equal(2, Repair(connection));
        await using (var context = new ListenArrDbContext(CreateOptions(connection)))
        {
            await context.Audiobooks.Where(book => book.Id == 2).ExecuteUpdateAsync(setters =>
                setters.SetProperty(book => book.AutomaticSearchDueUtc, NowUtc.AddDays(3)));
        }

        var before = await ReadDueTextsAsync(connection);

        Assert.Equal(0, Repair(connection, NowUtc.AddDays(1)));
        Assert.Equal(before, await ReadDueTextsAsync(connection));
    }

    [Fact]
    public async Task StartupRepair_UpdatesInBoundedChunks()
    {
        await using var connection = await OpenUpgradedAsync(
            (1, "NULL"), (2, "NULL"), (3, "NULL"), (4, "NULL"), (5, "NULL"));
        var updates = new UpdateRowCounter();

        Assert.Equal(5, Repair(connection, NowUtc, chunkSize: 2, updates));

        Assert.Equal([2, 2, 1], updates.RowsAffected);
        Assert.DoesNotContain(ScaffoldedDueText, (await ReadDueTextsAsync(connection)).Values);
    }

    [Fact]
    public async Task ApplyListenarrDatabaseMigrations_RepairsScaffoldedDueTimes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await MigrateAsync(connection, PreviousMigrationId);
        var searchedRecently = DateTime.UtcNow.AddHours(-1).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        await ExecuteAsync(connection, $"""
            INSERT INTO "Audiobooks" ("Id", "Monitored", "Explicit", "Abridged", "LastSearchTime")
            VALUES (1, 1, 0, 0, '{searchedRecently}'), (2, 1, 0, 0, NULL);
            """);
        var services = new ServiceCollection();
        services.AddDbContextFactory<ListenArrDbContext>(options =>
            options.UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name)));
        await using var provider = services.BuildServiceProvider();

        provider.ApplyListenarrDatabaseMigrations();

        Assert.Equal([2], await DueIdsAsync(connection, DateTime.UtcNow));
    }

    private static int Repair(
        SqliteConnection connection,
        DateTime? nowUtc = null,
        int chunkSize = AutomaticSearchScheduleStartupRepair.DefaultChunkSize,
        UpdateRowCounter? updates = null)
    {
        var builder = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name));
        if (updates is not null)
        {
            builder.AddInterceptors(updates);
        }

        using var context = new ListenArrDbContext(builder.Options);
        return AutomaticSearchScheduleStartupRepair.SetScaffoldedDueTimes(context, nowUtc ?? NowUtc, chunkSize);
    }

    // Books as the previous schema stored them, upgraded through the schedule migration.
    private static async Task<SqliteConnection> OpenUpgradedAsync(params (int Id, string LastSearchSql)[] books)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await MigrateAsync(connection, PreviousMigrationId);
        await ExecuteAsync(connection, """
            INSERT INTO "Audiobooks" ("Id", "Monitored", "Explicit", "Abridged", "LastSearchTime") VALUES
            """ + string.Join(",", books.Select(book => $" ({book.Id}, 1, 0, 0, {book.LastSearchSql})")) + ";");
        await MigrateAsync(connection, null);
        return connection;
    }

    private static async Task SeedSettingsAsync(SqliteConnection connection, string cooldownHoursSql)
    {
        await using (var context = new ListenArrDbContext(CreateOptions(connection)))
        {
            context.ApplicationSettings.Add(new ApplicationSettingsBuilder().Build());
            await context.SaveChangesAsync();
        }

        await ExecuteAsync(connection, $"UPDATE \"ApplicationSettings\" SET \"AutomaticSearchCooldownHours\" = {cooldownHoursSql};");
    }

    private static async Task<List<int>> DueIdsAsync(SqliteConnection connection, DateTime nowUtc)
    {
        await using var context = new ListenArrDbContext(CreateOptions(connection));
        return await context.Audiobooks
            .Where(book => book.AutomaticSearchDueUtc <= nowUtc)
            .OrderBy(book => book.Id)
            .Select(book => book.Id)
            .ToListAsync();
    }

    private static async Task<Dictionary<int, string>> ReadDueTextsAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"Id\", \"AutomaticSearchDueUtc\" FROM \"Audiobooks\" ORDER BY \"Id\";";
        var rows = new Dictionary<int, string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetInt32(0), reader.GetString(1));
        }

        return rows;
    }

    private static async Task MigrateAsync(SqliteConnection connection, string? targetMigration)
    {
        await using var context = new ListenArrDbContext(CreateOptions(connection));
        await context.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private static DbContextOptions<ListenArrDbContext> CreateOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name))
            .Options;

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private sealed class UpdateRowCounter : DbCommandInterceptor
    {
        public List<int> RowsAffected { get; } = [];

        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        {
            if (command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                RowsAffected.Add(result);
            }

            return result;
        }
    }
}
