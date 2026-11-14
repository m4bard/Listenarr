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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// Recent-release discovery is off until someone turns it on: on a new install, and on an
/// install whose settings row existed before the discovery migration ran.
/// </summary>
[Trait("Area", "Persistence")]
[Trait("Name", "RecentReleaseDiscoverySettingsMigrationTests")]
[Trait("Category", "Infrastructure")]
public class RecentReleaseDiscoverySettingsMigrationTests : BaseTests
{
    private const string DiscoveryMigrationSuffix = "_AddRecentReleaseDiscovery";

    [Fact]
    public void NewSettings_HaveDiscoveryOff()
    {
        Assert.False(new ApplicationSettings().RecentReleaseDiscoveryEnabled);
    }

    [Fact]
    public async Task FreshDatabase_SettingsRowSavedThroughTheModel_ReadsDiscoveryOff()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using (var context = new ListenArrDbContext(CreateOptions(connection)))
        {
            await context.Database.MigrateAsync();
            context.ApplicationSettings.Add(new ApplicationSettings());
            await context.SaveChangesAsync();
        }

        Assert.False(await ReadDiscoveryEnabledAsync(connection));
    }

    [Fact]
    public async Task UpgradedDatabase_SettingsRowFromBeforeTheMigration_ReadsDiscoveryOff()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using (var context = new ListenArrDbContext(CreateOptions(connection)))
        {
            var migrations = context.Database.GetMigrations().ToList();
            var discoveryIndex = migrations.FindIndex(id =>
                id.EndsWith(DiscoveryMigrationSuffix, StringComparison.Ordinal));
            Assert.True(discoveryIndex > 0, "The discovery migration must follow an earlier one.");

            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(migrations[discoveryIndex - 1]);
            await InsertPreDiscoverySettingsRowAsync(connection);
            await migrator.MigrateAsync(migrations[discoveryIndex]);
        }

        Assert.False(await ReadDiscoveryEnabledAsync(connection));
    }

    private static DbContextOptions<ListenArrDbContext> CreateOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name))
            .Options;

    private static async Task<bool> ReadDiscoveryEnabledAsync(SqliteConnection connection)
    {
        await using var context = new ListenArrDbContext(CreateOptions(connection));
        return await context.ApplicationSettings
            .AsNoTracking()
            .Select(settings => settings.RecentReleaseDiscoveryEnabled)
            .SingleAsync();
    }

    /// <summary>
    /// Writes the singleton row with the schema as it stood before the discovery migration,
    /// filling every required column that has no default, so the new column's value can only
    /// come from the migration.
    /// </summary>
    private static async Task InsertPreDiscoverySettingsRowAsync(SqliteConnection connection)
    {
        var columns = new List<string> { "\"Id\"" };
        var values = new List<string> { "1" };
        await using (var describe = connection.CreateCommand())
        {
            describe.CommandText =
                "SELECT \"name\", \"type\" FROM pragma_table_info('ApplicationSettings') " +
                "WHERE \"notnull\" = 1 AND \"dflt_value\" IS NULL AND \"pk\" = 0;";
            await using var reader = await describe.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add($"\"{reader.GetString(0)}\"");
                values.Add(reader.GetString(1) == "TEXT" ? "''" : "0");
            }
        }

        Assert.DoesNotContain("\"RecentReleaseDiscoveryEnabled\"", columns);
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            $"INSERT INTO \"ApplicationSettings\" ({string.Join(", ", columns)}) " +
            $"VALUES ({string.Join(", ", values)});";
        Assert.Equal(1, await insert.ExecuteNonQueryAsync());
    }
}
