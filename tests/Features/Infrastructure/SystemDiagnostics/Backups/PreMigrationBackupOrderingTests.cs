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
using Listenarr.Infrastructure.DependencyInjection;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Listenarr.Tests.Features.Infrastructure.SystemDiagnostics.Backups;

/// <summary>
/// The startup path repairs legacy data as well as migrating it, and the repair is a write. These
/// tests pin the backup to the front of that path.
/// </summary>
[Trait("Name", "PreMigrationBackupOrderingTests")]
[Trait("Category", "Backup")]
public sealed class PreMigrationBackupOrderingTests : BaseTests
{
    // The window in which ListenarrDatabaseMigrationPreflight.RepairLegacyData fires: after root
    // folders exist, before the durable filesystem recovery migration.
    private const string BeforeTheLegacyRootFolderRepair = "20260621002226_AddApplicationSettingsConcurrency";

    [Fact]
    [Trait("Scenario", "BackupPrecedesTheLegacyRepair")]
    public void ApplyListenarrDatabaseMigrations_TakesTheBackupBeforeTheLegacyRepairRewritesRows()
    {
        // Given an old populated database with two root folders both flagged default, which is the
        // state the startup path silently repairs
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name))
            .Options;

        using (var baseline = new ListenArrDbContext(options))
        {
            baseline.GetService<IMigrator>().Migrate(BeforeTheLegacyRootFolderRepair);
            baseline.Database.ExecuteSqlRaw(
                """
                INSERT INTO "RootFolders" ("Id", "Name", "Path", "IsDefault")
                VALUES (10, 'First', '/library/first', 1),
                       (20, 'Second', '/library/second', 1);
                """);
        }

        Assert.Equal(2, CountDefaultRootFolders(connection));

        // When the startup path runs, with a backup service that records what the database looked
        // like at the moment it was asked for a copy
        var defaultsSeenByTheBackup = -1;
        var backupService = new Mock<IBackupService>();
        backupService
            .Setup(service => service.CreateAsync(BackupTrigger.Migration, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                defaultsSeenByTheBackup = CountDefaultRootFolders(connection);
                return new BackupArchive
                {
                    Name = "stub.zip",
                    Trigger = BackupTrigger.Migration,
                    SizeBytes = 0,
                    CreatedAtUtc = DateTime.UtcNow
                };
            });

        var services = new ServiceCollection();
        services.AddDbContextFactory<ListenArrDbContext>(builder =>
            builder.UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name)));
        services.AddSingleton(backupService.Object);
        using var provider = services.BuildServiceProvider();

        provider.ApplyListenarrDatabaseMigrations();

        // Then the backup saw both flags still set. The repair is not reversible from an archive
        // taken after it, so a backup that runs second cannot restore which root folder was
        // default. The control is the assertion below: the repair really did happen on this run,
        // so an ordering that put the backup second would have seen 1 here rather than 2.
        Assert.Equal(2, defaultsSeenByTheBackup);
        Assert.Equal(1, CountDefaultRootFolders(connection));
        backupService.Verify(
            service => service.CreateAsync(BackupTrigger.Migration, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Scenario", "PopulatedDatabaseWithNoHistory")]
    public void ApplyListenarrDatabaseMigrations_StillBacksUp_WhenAPopulatedDatabaseHasNoMigrationHistory()
    {
        // Given a database with real tables and data whose __EFMigrationsHistory rows are gone,
        // which is what a partial dump or a troubleshooting session leaves behind
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name))
            .Options;

        using (var baseline = new ListenArrDbContext(options))
        {
            baseline.GetService<IMigrator>().Migrate(BeforeTheLegacyRootFolderRepair);
            baseline.Database.ExecuteSqlRaw("""DELETE FROM "__EFMigrationsHistory";""");
        }

        var backupService = new Mock<IBackupService>();
        backupService
            .Setup(service => service.CreateAsync(It.IsAny<BackupTrigger>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackupArchive
            {
                Name = "stub.zip",
                Trigger = BackupTrigger.Migration,
                SizeBytes = 0,
                CreatedAtUtc = DateTime.UtcNow
            });

        var services = new ServiceCollection();
        services.AddDbContextFactory<ListenArrDbContext>(builder =>
            builder.UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name)));
        services.AddSingleton(backupService.Object);
        using var provider = services.BuildServiceProvider();

        // When the startup path runs. Migrate itself will fail here, since the tables already
        // exist and EF believes it must create them, and that is beside the point: what matters is
        // that a copy was taken first. Counting applied migrations would have called this a fresh
        // install and skipped it.
        try
        {
            provider.ApplyListenarrDatabaseMigrations();
        }
        catch (Exception exception) when (exception is not Xunit.Sdk.XunitException)
        {
            // Expected on this deliberately inconsistent database.
        }

        // Then the backup still happened, because the question asked was whether the database has
        // tables, not whether it has history
        backupService.Verify(
            service => service.CreateAsync(BackupTrigger.Migration, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static int CountDefaultRootFolders(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM "RootFolders" WHERE "IsDefault" = 1;""";
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
