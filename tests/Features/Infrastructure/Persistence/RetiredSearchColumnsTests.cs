/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// The indexer backoff columns and the fan-out ceiling stay in the schema after the code that used
/// them was retired, so an older build can be put back on the same database. These pin that the
/// current build's own writes leave them exactly as the database holds them.
/// </summary>
[Trait("Area", "Persistence")]
[Trait("Name", "RetiredSearchColumnsTests")]
[Trait("Category", "Infrastructure")]
public class RetiredSearchColumnsTests : BaseTests
{
    [Fact]
    [Trait("Scenario", "SettingsSaveKeepsStoredCeiling")]
    public async Task SavingSettings_LeavesTheRetiredFanOutCeilingAsStored()
    {
        await using var connection = await OpenMigratedAsync();
        await using (var seed = CreateContext(connection))
        {
            await new EfApplicationSettingsRepository(seed).InitializeIfMissingAsync(new ApplicationSettings());
        }
        await ExecuteAsync(connection, "UPDATE ApplicationSettings SET MaxConcurrentIndexerSearches = 7 WHERE Id = 1");

        await using (var context = CreateContext(connection))
        {
            var repository = new EfApplicationSettingsRepository(context);
            var settings = (await repository.GetAsync())!;
            settings.OutputPath = "saved";
            await repository.SaveAsync(settings);
        }

        Assert.Equal("saved", await ScalarAsync(connection, "SELECT OutputPath FROM ApplicationSettings WHERE Id = 1"));
        Assert.Equal(7L, await ScalarAsync(connection, "SELECT MaxConcurrentIndexerSearches FROM ApplicationSettings WHERE Id = 1"));
    }

    [Fact]
    [Trait("Scenario", "IndexerEditKeepsStoredBackoff")]
    public async Task UpdatingAnIndexer_LeavesTheRetiredBackoffColumnsAsStored()
    {
        await using var connection = await OpenMigratedAsync();
        int id;
        await using (var seed = CreateContext(connection))
        {
            id = (await new EfIndexerRepository(seed).AddAsync(new Indexer
            {
                Name = "Before",
                Url = "http://indexer.invalid",
                Implementation = "Newznab",
                Categories = "3030"
            })).Id;
        }
        await ExecuteAsync(
            connection,
            $"UPDATE Indexers SET EscalationLevel = 3, DisabledTill = '2030-01-01 00:00:00', InitialFailure = '2029-12-31 00:00:00', MostRecentFailure = '2029-12-31 12:00:00', LastFailureReason = 'RateLimited' WHERE Id = {id}");

        await using (var context = CreateContext(connection))
        {
            var edited = (await context.Indexers.AsNoTracking().SingleAsync(i => i.Id == id));
            edited.Name = "After";
            await new EfIndexerRepository(context).UpdateAsync(edited);
        }

        Assert.Equal("After", await ScalarAsync(connection, $"SELECT Name FROM Indexers WHERE Id = {id}"));
        Assert.Equal(
            "3|2030-01-01 00:00:00|2029-12-31 00:00:00|2029-12-31 12:00:00|RateLimited",
            await ScalarAsync(connection, $"SELECT EscalationLevel || '|' || DisabledTill || '|' || InitialFailure || '|' || MostRecentFailure || '|' || LastFailureReason FROM Indexers WHERE Id = {id}"));
    }

    private static async Task<SqliteConnection> OpenMigratedAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.MigrateAsync();
        return connection;
    }

    private static ListenArrDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name))
            .Options);

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
}
