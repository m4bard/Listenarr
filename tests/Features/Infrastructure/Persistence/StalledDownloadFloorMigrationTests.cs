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
/// <see cref="StalledDownloadTimeoutMigrationTests"/>: adding one setting should not rewrite
/// lines of a file every migration-carrying branch in the stack also edits.
/// </summary>
[Trait("Area", "Persistence")]
[Trait("Name", "StalledDownloadFloorMigrationTests")]
[Trait("Category", "Infrastructure")]
public class StalledDownloadFloorMigrationTests : BaseTests
{
    [Fact]
    [Trait("Scenario", "UpgradedInstallGetsTheSameFloorAsAFreshInstall")]
    public async Task Migration_DefaultsTheFloorToOnePercent_LikeAFreshInstall()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(
            new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseSqlite(connection, sqlite =>
                    sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name))
                .Options);

        await context.Database.MigrateAsync();

        // SQLite fills an existing row from the column default, so this is the value an
        // upgraded install comes up with. It has to be the model default (the scaffolder's own
        // CLR-default guess here was 0, which would have meant the floor is OFF on every
        // upgraded install while a fresh install gets 1%).
        Assert.Equal(1m, new ApplicationSettings().StalledDownloadFloorPercent);

        // Decimal is a TEXT column, so SQLite's own dflt_value pragma hands the literal back
        // quoted, unlike the plain integer "0" below.
        Assert.Equal("'1.0'", await ColumnDefaultAsync(connection, "ApplicationSettings", "StalledDownloadFloorPercent"));

        // Control: the same query against a column whose default is known and is not 1, so a
        // helper that returned "1" for anything, or for a missing column, could not pass above.
        Assert.Equal("0", await ColumnDefaultAsync(connection, "ApplicationSettings", "StalledDownloadTimeoutHours"));
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
