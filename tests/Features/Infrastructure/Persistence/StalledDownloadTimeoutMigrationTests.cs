/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// Kept apart from <see cref="SqliteMigrationSchemaTests"/> for the same reason as
/// <see cref="IndexerSearchConcurrencyMigrationTests"/>: adding one setting should not rewrite
/// lines of a file every migration-carrying branch in the stack also edits.
/// </summary>
[Trait("Area", "Persistence")]
[Trait("Name", "StalledDownloadTimeoutMigrationTests")]
[Trait("Category", "Infrastructure")]
public class StalledDownloadTimeoutMigrationTests : BaseTests
{
    [Fact]
    [Trait("Scenario", "UpgradedInstallKeepsStallHandlingOff")]
    public async Task Migration_DefaultsTheStallTimeoutToOff_LikeAFreshInstall()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(
            new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseSqlite(connection, sqlite =>
                    sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name))
                .Options);

        await context.Database.MigrateAsync();

        // SQLite fills an existing row from the column default, so this is the value an upgraded
        // install comes up with. It has to be the model default, or an upgrade and a fresh
        // install would disagree about whether Listenarr fails stalled torrents on its own.
        Assert.Equal(0, new ApplicationSettings().StalledDownloadTimeoutHours);
        Assert.Equal("0", await ColumnDefaultAsync(connection, "ApplicationSettings", "StalledDownloadTimeoutHours"));

        // Control: the same query against a column whose default is known and is not 0, so a
        // helper that returned "0" for anything, or for a missing column, could not pass above.
        Assert.Equal("60", await ColumnDefaultAsync(connection, "ApplicationSettings", "MetadataRefreshRequestsPerHour"));
        Assert.Null(await ColumnDefaultAsync(connection, "ApplicationSettings", "NoSuchColumn"));
    }

    private static async Task<string?> ColumnDefaultAsync(SqliteConnection connection, string table, string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT dflt_value FROM pragma_table_info('{table}') WHERE name = $column";
        command.Parameters.AddWithValue("$column", column);
        return await command.ExecuteScalarAsync() as string;
    }
}
