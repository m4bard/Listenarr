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

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// The indexer-to-download-client binding is a new nullable column. On upgrade every existing
/// indexer must come out unbound, which is the family default (Readarr's
/// 030_download_client_per_indexer.cs adds the column with 0, meaning "any client"), so an
/// upgrade routes nothing differently from the day before.
/// </summary>
[Trait("Area", "Persistence")]
[Trait("Name", "IndexerDownloadClientMigrationTests")]
[Trait("Category", "Infrastructure")]
public class IndexerDownloadClientMigrationTests : BaseTests
{
    // The newest migration below this branch in the stack. Migrating to it gives a database
    // exactly as an install has it before the binding exists.
    private const string MigrationBeforeBinding = "20260922220833_AddHousekeepingRetention";

    private static DbContextOptions<ListenArrDbContext> CreateOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name))
            .Options;

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string table, string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name=$name";
        command.Parameters.AddWithValue("$name", column);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    [Fact]
    [Trait("Scenario", "ExistingIndexersUpgradeUnbound")]
    public async Task Upgrade_LeavesEveryExistingIndexerUnbound()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        // A real upgrade: migrate to the state before the column existed, put two indexers
        // in, then migrate forward and read the rows back.
        await using (var before = new ListenArrDbContext(CreateOptions(connection)))
        {
            await before.GetService<IMigrator>().MigrateAsync(MigrationBeforeBinding);
        }

        Assert.False(await ColumnExistsAsync(connection, "Indexers", "DownloadClientId"));

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText =
                "INSERT INTO Indexers "
                + "(Name, Type, Implementation, Url, EnableRss, EnableAutomaticSearch, EnableInteractiveSearch, "
                + "EnableAnimeStandardSearch, IsEnabled, Priority, MinimumAge, Retention, MaximumSize, "
                + "EscalationLevel, CreatedAt, UpdatedAt) VALUES "
                + "('Public Tracker', 'Torrent', 'Torznab', 'https://tracker.example.test', 1, 1, 1, 0, 1, 25, 0, 0, 0, 0, '2026-01-01 00:00:00', '2026-01-01 00:00:00'), "
                + "('Usenet Indexer', 'Usenet', 'Newznab', 'https://usenet.example.test', 1, 1, 1, 0, 1, 25, 0, 0, 0, 0, '2026-01-01 00:00:00', '2026-01-01 00:00:00')";
            await insert.ExecuteNonQueryAsync();
        }

        await using (var after = new ListenArrDbContext(CreateOptions(connection)))
        {
            await after.Database.MigrateAsync();
        }

        Assert.True(await ColumnExistsAsync(connection, "Indexers", "DownloadClientId"));

        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT COUNT(*), SUM(CASE WHEN DownloadClientId IS NULL THEN 1 ELSE 0 END) FROM Indexers";
        await using var reader = await read.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        // Control: both rows must still be there, so an insert that silently did nothing
        // cannot pass as "every row is unbound".
        Assert.Equal(2L, reader.GetInt64(0));
        Assert.Equal(2L, reader.GetInt64(1));
    }

    [Fact]
    [Trait("Scenario", "BindingRoundTrips")]
    public async Task Binding_RoundTripsThroughTheDatabase()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(CreateOptions(connection));
        await context.Database.MigrateAsync();

        context.Indexers.Add(new Indexer
        {
            Name = "Bound",
            Type = "Torrent",
            Implementation = "Torznab",
            Url = "https://bound.example.test",
            DownloadClientId = "seedbox-client"
        });
        context.Indexers.Add(new Indexer
        {
            Name = "Unbound",
            Type = "Torrent",
            Implementation = "Torznab",
            Url = "https://unbound.example.test"
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var stored = await context.Indexers.ToListAsync();

        // Control pair: a column that dropped every value would fail the first line, and one
        // that invented a value would fail the second.
        Assert.Equal("seedbox-client", stored.Single(i => i.Name == "Bound").DownloadClientId);
        Assert.Null(stored.Single(i => i.Name == "Unbound").DownloadClientId);
    }
}
