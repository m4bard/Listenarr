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
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// Upgrades a database holding books from before the automatic-search schedule existed, and
/// downgrades it again, through the real SQLite migration pipeline. The migration is a pure EF
/// scaffold, so existing books take the scaffolded due time; moving them to their own due times
/// is a startup step, not part of the migration.
/// </summary>
[Trait("Name", "AutomaticSearchScheduleMigrationTests")]
[Trait("Category", "Persistence")]
public sealed class AutomaticSearchScheduleMigrationTests : BaseTests
{
    private const string PreviousMigrationId = "20261029020400_AddReleaseBlocklist";
    private const string ScheduleMigrationId = "20261114021916_AddAutomaticSearchSchedule";
    private const string ScaffoldedDueText = "0001-01-01 00:00:00";

    private readonly ITestOutputHelper _output;

    public AutomaticSearchScheduleMigrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Upgrade_GivesExistingBooksTheScaffoldedScheduleAndBuildsTheIndexes()
    {
        await using var connection = await OpenAtPreviousMigrationAsync();
        await SeedBooksAsync(connection);

        await MigrateAsync(connection, ScheduleMigrationId);

        Assert.Equal(
            [
                (ScaffoldedDueText, 0L, "2026-01-05 07:00:00"),
                (ScaffoldedDueText, 0L, (string?)null)
            ],
            await ReadBooksAsync(connection));
        Assert.Equal(
            "CREATE INDEX \"IX_Audiobooks_AutomaticSearchDue\" ON \"Audiobooks\" (\"AutomaticSearchDueUtc\") "
            + "WHERE \"Monitored\" AND \"QualityProfileId\" IS NOT NULL",
            await ScalarAsync(connection, "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'IX_Audiobooks_AutomaticSearchDue'"));
        Assert.Equal(
            "sqlite_autoindex_AudiobookIndexerSearchStates_1,IX_AudiobookIndexerSearchStates_IndexerId",
            await ScalarAsync(connection, "SELECT group_concat(name, ',') FROM (SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'AudiobookIndexerSearchStates' ORDER BY name DESC)"));
        Assert.Equal(
            "25,300",
            await ScalarAsync(connection, "SELECT group_concat(dflt_value, ',') FROM (SELECT dflt_value FROM pragma_table_info('ApplicationSettings') WHERE name LIKE 'AutomaticSearchMax%' ORDER BY name)"));
    }

    [Fact]
    public async Task PairRows_AreDeletedWithTheirBookOrIndexer()
    {
        await using var connection = await OpenAtPreviousMigrationAsync();
        await MigrateAsync(connection, ScheduleMigrationId);
        int[] bookIds;
        int[] indexerIds;
        await using (var context = new ListenArrDbContext(CreateOptions(connection)))
        {
            var books = new[] { "A Princess of Mars", "The Wonderful Wizard of Oz" }
                .Select(title => new AudiobookBuilder().WithTitle(title).Build()).ToArray();
            var indexers = new[] { "first", "second" }
                .Select(name => new IndexerBuilder().WithName(name).Build()).ToArray();
            context.Audiobooks.AddRange(books);
            context.Indexers.AddRange(indexers);
            await context.SaveChangesAsync();
            bookIds = books.Select(book => book.Id).ToArray();
            indexerIds = indexers.Select(indexer => indexer.Id).ToArray();
            context.AudiobookIndexerSearchStates.AddRange(
                from bookId in bookIds
                from indexerId in indexerIds
                select new AudiobookIndexerSearchState
                {
                    AudiobookId = bookId,
                    IndexerId = indexerId,
                    NextAttemptUtc = DateTime.UtcNow,
                    LastOutcome = "Completed"
                });
            await context.SaveChangesAsync();
        }

        await ScalarAsync(connection, $"DELETE FROM \"Audiobooks\" WHERE \"Id\" = {bookIds[0]}");
        await ScalarAsync(connection, $"DELETE FROM \"Indexers\" WHERE \"Id\" = {indexerIds[1]}");

        Assert.Equal(
            $"{bookIds[1]}:{indexerIds[0]}",
            await ScalarAsync(connection, "SELECT group_concat(\"AudiobookId\" || ':' || \"IndexerId\", ',') FROM \"AudiobookIndexerSearchStates\""));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(AutomaticSearchBounds.DefaultMaxBooksPerCycle, AutomaticSearchBounds.DefaultMaxRequestsPerCycle)]
    [InlineData(7, 900)]
    public async Task SettingsBounds_AreStoredAsGivenOnInsert(int maxBooks, int maxRequests)
    {
        await using var connection = await OpenAtPreviousMigrationAsync();
        await MigrateAsync(connection, ScheduleMigrationId);
        await using (var context = new ListenArrDbContext(CreateOptions(connection)))
        {
            var settings = new ApplicationSettingsBuilder().Build();
            settings.AutomaticSearchMaxBooksPerCycle = maxBooks;
            settings.AutomaticSearchMaxRequestsPerCycle = maxRequests;
            context.ApplicationSettings.Add(settings);
            await context.SaveChangesAsync();
        }

        Assert.Equal(
            $"{maxBooks},{maxRequests}",
            await ScalarAsync(connection, "SELECT \"AutomaticSearchMaxBooksPerCycle\" || ',' || \"AutomaticSearchMaxRequestsPerCycle\" FROM \"ApplicationSettings\""));
    }

    [Fact]
    public async Task Downgrade_DropsTheScheduleAndKeepsTheBooks()
    {
        await using var connection = await OpenAtPreviousMigrationAsync();
        await SeedBooksAsync(connection);
        await MigrateAsync(connection, ScheduleMigrationId);
        await using (var context = new ListenArrDbContext(CreateOptions(connection)))
        {
            _output.WriteLine(context.GetService<IMigrator>().GenerateScript(ScheduleMigrationId, PreviousMigrationId));
        }

        await MigrateAsync(connection, PreviousMigrationId);

        Assert.Equal(
            0L,
            await ScalarAsync(connection, "SELECT count(*) FROM sqlite_master WHERE name IN ('AudiobookIndexerSearchStates', 'IX_Audiobooks_AutomaticSearchDue')"));
        Assert.Equal(
            0L,
            await ScalarAsync(connection, "SELECT count(*) FROM pragma_table_info('Audiobooks') WHERE name LIKE 'AutomaticSearch%'"));
        Assert.Equal(
            0L,
            await ScalarAsync(connection, "SELECT count(*) FROM pragma_table_info('ApplicationSettings') WHERE name LIKE 'AutomaticSearchMax%'"));
        Assert.Equal(
            "A Princess of Mars,The Wonderful Wizard of Oz",
            await ScalarAsync(connection, "SELECT group_concat(\"Title\", ',') FROM (SELECT \"Title\" FROM \"Audiobooks\" ORDER BY \"Id\")"));
    }

    private static async Task<SqliteConnection> OpenAtPreviousMigrationAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await MigrateAsync(connection, PreviousMigrationId);
        return connection;
    }

    private static async Task MigrateAsync(SqliteConnection connection, string targetMigration)
    {
        await using var context = new ListenArrDbContext(CreateOptions(connection));
        await context.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private static DbContextOptions<ListenArrDbContext> CreateOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name))
            .Options;

    // Two books as the previous schema stored them: one searched before, one never searched.
    private static async Task SeedBooksAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "Audiobooks" ("Id", "Title", "Monitored", "Explicit", "Abridged", "LastSearchTime")
            VALUES (1, $first, 1, 0, 0, '2026-01-05 07:00:00'),
                   (2, $second, 0, 0, 0, NULL);
            """;
        command.Parameters.AddWithValue("$first", "A Princess of Mars");
        command.Parameters.AddWithValue("$second", "The Wonderful Wizard of Oz");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<(string Due, long FailureCount, string? LastSearchTime)>> ReadBooksAsync(
        SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "AutomaticSearchDueUtc", "AutomaticSearchFailureCount", "LastSearchTime"
            FROM "Audiobooks" ORDER BY "Id";
            """;
        var rows = new List<(string, long, string?)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return rows;
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
