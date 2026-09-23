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

using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// Download client failure status is persisted, as Readarr's DownloadClientStatus table is, so a
/// restart neither forgets a client that has been down for an hour nor, through the startup window,
/// buries one for longer than five minutes.
/// </summary>
[Trait("Area", "Persistence")]
[Trait("Name", "DownloadClientStatusPersistenceTests")]
[Trait("Category", "Infrastructure")]
public class DownloadClientStatusPersistenceTests : BaseTests
{
    // The newest migration below this branch in the stack. Migrating to it gives a database
    // exactly as an install has it before client status exists.
    private const string MigrationBeforeStatus = "20260923051638_AddIndexerDownloadClientBinding";

    private static DbContextOptions<ListenArrDbContext> CreateOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name))
            .Options;

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<SqliteConnection> MigratedWithClientAsync(string clientId)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(CreateOptions(connection));
        await context.Database.MigrateAsync();
        context.DownloadClientConfigurations.Add(new DownloadClientConfiguration
        {
            Id = clientId,
            Name = clientId,
            Type = "qbittorrent",
            Host = "localhost",
            Port = 8080,
            IsEnabled = true
        });
        await context.SaveChangesAsync();
        return connection;
    }

    [Fact]
    [Trait("Scenario", "SurvivesRestart")]
    public async Task Status_SurvivesARestart()
    {
        await using var connection = await MigratedWithClientAsync("qb-main");
        var disabledTill = new DateTime(2026, 9, 23, 13, 0, 0, DateTimeKind.Utc);

        await using (var first = new ListenArrDbContext(CreateOptions(connection)))
        {
            var written = await new EfDownloadClientStatusRepository(first).UpsertAsync(new DownloadClientStatus
            {
                ClientId = "qb-main",
                InitialFailure = disabledTill.AddHours(-2),
                MostRecentFailure = disabledTill.AddHours(-1),
                EscalationLevel = 5,
                DisabledTill = disabledTill
            });
            Assert.True(written);
        }

        // A second context on the same database is the process coming back up.
        await using var second = new ListenArrDbContext(CreateOptions(connection));
        var stored = await new EfDownloadClientStatusRepository(second).GetByClientIdAsync("qb-main");

        Assert.NotNull(stored);
        Assert.Equal(5, stored!.EscalationLevel);
        Assert.Equal(disabledTill, stored.DisabledTill);
        Assert.Equal(disabledTill.AddHours(-2), stored.InitialFailure);
        Assert.Equal(disabledTill.AddHours(-1), stored.MostRecentFailure);
        Assert.Single(await new EfDownloadClientStatusRepository(second).GetAllAsync());
    }

    [Fact]
    [Trait("Scenario", "UpsertUpdates")]
    public async Task Upsert_UpdatesTheExistingRowRatherThanAddingASecond()
    {
        await using var connection = await MigratedWithClientAsync("qb-main");

        await using (var context = new ListenArrDbContext(CreateOptions(connection)))
        {
            var repository = new EfDownloadClientStatusRepository(context);
            await repository.UpsertAsync(new DownloadClientStatus { ClientId = "qb-main", EscalationLevel = 1 });
            await repository.UpsertAsync(new DownloadClientStatus { ClientId = "qb-main", EscalationLevel = 2 });
        }

        Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM DownloadClientStatuses"));
        Assert.Equal(2, await ScalarAsync(connection, "SELECT EscalationLevel FROM DownloadClientStatuses"));
    }

    [Fact]
    [Trait("Scenario", "UnknownClient")]
    public async Task Upsert_ForAClientThatDoesNotExist_WritesNothing()
    {
        await using var connection = await MigratedWithClientAsync("qb-main");
        await using var context = new ListenArrDbContext(CreateOptions(connection));

        // A connection test from the add form names a client id with no row behind it.
        var written = await new EfDownloadClientStatusRepository(context)
            .UpsertAsync(new DownloadClientStatus { ClientId = "not-saved-yet", EscalationLevel = 1 });

        Assert.False(written);
        Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM DownloadClientStatuses"));
    }

    [Fact]
    [Trait("Scenario", "DeletedWithClient")]
    public async Task DeletingAClient_DeletesItsStatus()
    {
        // Readarr removes the status row on ProviderDeletedEvent
        // (ProviderStatusServiceBase.cs:152-155). Here the foreign key does it.
        await using var connection = await MigratedWithClientAsync("qb-main");
        await using (var context = new ListenArrDbContext(CreateOptions(connection)))
        {
            await new EfDownloadClientStatusRepository(context)
                .UpsertAsync(new DownloadClientStatus { ClientId = "qb-main", EscalationLevel = 3 });
        }

        Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM DownloadClientStatuses"));

        await using (var context = new ListenArrDbContext(CreateOptions(connection)))
        {
            Assert.True(await new EfDownloadClientConfigurationRepository(context).DeleteAsync("qb-main"));
        }

        Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM DownloadClientStatuses"));
    }

    [Fact]
    [Trait("Scenario", "PopulatedUpgrade")]
    public async Task Upgrade_OnAPopulatedDatabase_KeepsEveryClientAndStartsThemAllHealthy()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using (var before = new ListenArrDbContext(CreateOptions(connection)))
        {
            await before.GetService<IMigrator>().MigrateAsync(MigrationBeforeStatus);
        }

        Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DownloadClientStatuses'"));

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText =
                "INSERT INTO DownloadClientConfigurations "
                + "(Id, Name, Type, Host, Port, Username, Password, DownloadPath, UseSSL, IsEnabled, Priority, "
                + "RemoveCompletedDownloads, SettingsJson, CreatedAt) VALUES "
                + "('qb-main', 'Main', 'qbittorrent', 'localhost', 8080, '', '', '', 0, 1, 1, 'none', '{}', '2026-01-01 00:00:00'), "
                + "('sab-main', 'Usenet', 'sabnzbd', 'localhost', 8081, '', '', '', 0, 1, 3, 'none', '{}', '2026-01-01 00:00:00')";
            await insert.ExecuteNonQueryAsync();
        }

        await using (var after = new ListenArrDbContext(CreateOptions(connection)))
        {
            await after.Database.MigrateAsync();
        }

        // Control: both clients are still there, with the priority they had, so a migration that
        // rebuilt the table and lost rows cannot pass as "no status rows".
        Assert.Equal(2, await ScalarAsync(connection, "SELECT COUNT(*) FROM DownloadClientConfigurations"));
        Assert.Equal(3, await ScalarAsync(connection, "SELECT Priority FROM DownloadClientConfigurations WHERE Id='sab-main'"));
        Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM DownloadClientStatuses"));

        // And the new table accepts a row for a client that existed before the upgrade.
        await using var context = new ListenArrDbContext(CreateOptions(connection));
        Assert.True(await new EfDownloadClientStatusRepository(context)
            .UpsertAsync(new DownloadClientStatus { ClientId = "sab-main", EscalationLevel = 1 }));
    }
}
