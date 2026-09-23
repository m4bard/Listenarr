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
/// Kept apart from <see cref="SqliteMigrationSchemaTests"/> so that adding this one setting does
/// not rewrite lines of a file every migration-carrying branch in the stack also edits.
/// </summary>
[Trait("Area", "Persistence")]
[Trait("Name", "IndexerSearchConcurrencyMigrationTests")]
[Trait("Category", "Infrastructure")]
public class IndexerSearchConcurrencyMigrationTests : BaseTests
{
    [Fact]
    [Trait("Scenario", "UpgradedInstallKeepsShippedCeiling")]
    public async Task Migration_DefaultsTheConcurrencyCeilingToTheFourThatWasHardcoded()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(
            new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseSqlite(connection, sqlite =>
                    sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name))
                .Options);

        await context.Database.MigrateAsync();

        // The scaffolder writes a column default from the CLR default, 0, rather than from the
        // property initializer. SQLite fills an existing row from the column default, so an
        // upgraded install left on 0 would be clamped to 1 and search every indexer one at a
        // time. This asserts the literal 4 rather than "whatever the entity says" for that reason.
        Assert.Equal("4", await ColumnDefaultAsync(connection, "ApplicationSettings", "MaxConcurrentIndexerSearches"));

        // Control: the same query against a column whose default is known, so a helper that
        // returned "4" for anything could not pass the assertion above by accident.
        Assert.Equal("60", await ColumnDefaultAsync(connection, "ApplicationSettings", "MetadataRefreshRequestsPerHour"));
    }

    private static async Task<string?> ColumnDefaultAsync(SqliteConnection connection, string table, string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT dflt_value FROM pragma_table_info('{table}') WHERE name = $column";
        command.Parameters.AddWithValue("$column", column);
        return await command.ExecuteScalarAsync() as string;
    }
}
