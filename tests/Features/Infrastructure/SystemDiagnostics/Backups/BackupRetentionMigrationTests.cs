/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Listenarr.Tests.Features.Infrastructure.SystemDiagnostics.Backups;

[Trait("Name", "BackupRetentionMigrationTests")]
[Trait("Category", "Backup")]
public sealed class BackupRetentionMigrationTests : BaseTests
{
    private const string MigrationBeforeBackupRetention =
        "20260825021432_AddWeakStorageVerifiedCleanup";

    [Fact]
    [Trait("Scenario", "UpgradeBackFillsTheFamilyDefault")]
    public async Task AddBackupRetentionDays_BackFillsAnExistingRowWithTheSameDefaultANewInstallGets()
    {
        // Given a populated database at the release before this column existed, holding a
        // settings row written by that build
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name))
            .Options;

        await using (var before = new ListenArrDbContext(options))
        {
            await before.GetService<IMigrator>().MigrateAsync(MigrationBeforeBackupRetention);
            await InsertSettingsRowAsync(connection);
            Assert.False(await ColumnExistsAsync(connection, "BackupRetentionDays"));
        }

        // When the upgrade runs, which is what pulling a new image does
        await using (var after = new ListenArrDbContext(options))
        {
            await after.Database.MigrateAsync();
        }

        // Then the existing row carries 28, the same value a fresh install gets from the entity.
        // The scaffolder writes 0 here, which BackupService reads as "never sweep", so without the
        // hand-set default an upgraded install and a new one would behave differently forever.
        await using var read = connection.CreateCommand();
        read.CommandText = """SELECT "BackupRetentionDays" FROM "ApplicationSettings" WHERE "Id" = 1;""";
        Assert.Equal(28L, Convert.ToInt64(await read.ExecuteScalarAsync()));
        Assert.Equal(28, new ApplicationSettings().BackupRetentionDays);
        Assert.Equal(28, BackupService.DefaultRetentionDays);
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM pragma_table_info('ApplicationSettings') WHERE "name" = $column;""";
        command.Parameters.AddWithValue("$column", column);
        return Convert.ToInt64(await command.ExecuteScalarAsync()) > 0;
    }

    /// <summary>
    /// Writes one settings row using whatever columns the schema has at this point in history,
    /// supplying a placeholder for every column that is NOT NULL without a default. Built from
    /// pragma_table_info rather than hard-coded so it keeps working as the table grows.
    /// </summary>
    private static async Task InsertSettingsRowAsync(SqliteConnection connection)
    {
        var columns = new List<(string Name, string Type)>();

        await using (var describe = connection.CreateCommand())
        {
            describe.CommandText =
                """SELECT "name", "type", "notnull", "dflt_value" FROM pragma_table_info('ApplicationSettings');""";
            await using var reader = await describe.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(0);
                var notNull = reader.GetInt64(2) != 0;
                var hasDefault = !await reader.IsDBNullAsync(3);
                if (name == "Id" || (notNull && !hasDefault))
                {
                    columns.Add((name, reader.GetString(1).ToUpperInvariant()));
                }
            }
        }

        var names = string.Join(", ", columns.Select(column => $"\"{column.Name}\""));
        var values = string.Join(", ", columns.Select(column =>
            column.Name == "Id" ? "1" : column.Type.Contains("INT") || column.Type.Contains("REAL") ? "0" : "''"));

        await using var insert = connection.CreateCommand();
        insert.CommandText = $"""INSERT INTO "ApplicationSettings" ({names}) VALUES ({values});""";
        await insert.ExecuteNonQueryAsync();
    }
}
