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

[Trait("Name", "EfIndexerRequestStateStoreTests")]
[Trait("Category", "Infrastructure")]
public sealed class EfIndexerRequestStateStoreTests : BaseTests
{
    private const string MigrationBeforeIndexerRequestState = "20261029020400_AddReleaseBlocklist";
    private static readonly DateTime ClockUtc = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Migration_CreatesStateTableKeyedByIndexerRowid()
    {
        await using var database = await IndexerRequestStateTestDatabase.CreateAsync(indexerCount: 2);
        var indexerId = database.IndexerIds[1];
        await new EfIndexerRequestStateStore(database.Factory).UpsertAsync(CooldownState(indexerId));

        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync();
        var keyColumns = await QueryAsync(
            connection,
            "SELECT \"name\", \"type\" FROM pragma_table_info('IndexerRequestStates') WHERE \"pk\" > 0");
        Assert.Equal(["IndexerId|INTEGER"], keyColumns);
        var foreignKeys = await QueryAsync(
            connection,
            "SELECT \"table\", \"from\", \"to\", \"on_delete\" FROM pragma_foreign_key_list('IndexerRequestStates')");
        Assert.Equal(["Indexers|IndexerId|Id|CASCADE"], foreignKeys);
        // IndexerId is an alias for the rowid only when the two always agree.
        var rowids = await QueryAsync(
            connection,
            "SELECT \"rowid\", \"IndexerId\" FROM \"IndexerRequestStates\"");
        Assert.Equal([$"{indexerId}|{indexerId}"], rowids);
    }

    [Fact]
    public async Task UpsertAsync_InsertsThenReplacesTheWholeRow()
    {
        await using var database = await IndexerRequestStateTestDatabase.CreateAsync(indexerCount: 1);
        var store = new EfIndexerRequestStateStore(database.Factory);
        var inserted = CooldownState(database.IndexerIds[0]);

        await store.UpsertAsync(inserted);
        var afterInsert = Assert.Single(await store.LoadAllAsync());
        var replacement = new IndexerRequestState
        {
            IndexerId = inserted.IndexerId,
            ConsecutiveFailures = 0,
            CooldownUntilUtc = null,
            CooldownReason = null,
            LastDispatchUtc = ClockUtc.AddMinutes(30),
            UpdatedUtc = ClockUtc.AddMinutes(30)
        };
        await store.UpsertAsync(replacement);
        var afterReplace = Assert.Single(await store.LoadAllAsync());

        AssertSameState(inserted, afterInsert);
        AssertSameState(replacement, afterReplace);
    }

    [Fact]
    public async Task DeletingIndexer_CascadesItsStateRow()
    {
        await using var database = await IndexerRequestStateTestDatabase.CreateAsync(indexerCount: 2);
        var store = new EfIndexerRequestStateStore(database.Factory);
        var deletedId = database.IndexerIds[0];
        var keptId = database.IndexerIds[1];
        await store.UpsertAsync(CooldownState(deletedId));
        await store.UpsertAsync(CooldownState(keptId));

        await using (var context = database.CreateContext())
        {
            await new EfIndexerRepository(context).DeleteAsync(deletedId);
        }

        var remaining = Assert.Single(await store.LoadAllAsync());
        Assert.Equal(keptId, remaining.IndexerId);
    }

    [Fact]
    public async Task ExistingIndexers_HaveNoStateRowAfterUpgrade()
    {
        await using var database = await IndexerRequestStateTestDatabase.CreateAsync(
            indexerCount: 3,
            targetMigration: MigrationBeforeIndexerRequestState);
        await using (var context = database.CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var store = new EfIndexerRequestStateStore(database.Factory);
        Assert.Empty(await store.LoadAllAsync());
        // A success for an indexer without a row has nothing to clear and creates none.
        await store.ClearConsecutiveFailuresAsync(database.IndexerIds[0], ClockUtc);
        Assert.Empty(await store.LoadAllAsync());
    }

    [Fact]
    public async Task ClearConsecutiveFailuresAsync_ResetsTheCountAndLeavesTheCooldown()
    {
        await using var database = await IndexerRequestStateTestDatabase.CreateAsync(indexerCount: 2);
        var store = new EfIndexerRequestStateStore(database.Factory);
        var cleared = CooldownState(database.IndexerIds[0]);
        var untouched = CooldownState(database.IndexerIds[1]);
        await store.UpsertAsync(cleared);
        await store.UpsertAsync(untouched);
        var successUtc = ClockUtc.AddMinutes(5);

        await store.ClearConsecutiveFailuresAsync(cleared.IndexerId, successUtc);

        var rows = (await store.LoadAllAsync()).ToDictionary(state => state.IndexerId);
        var after = rows[cleared.IndexerId];
        Assert.Equal(0, after.ConsecutiveFailures);
        Assert.Equal(successUtc, after.UpdatedUtc);
        Assert.Equal(cleared.CooldownUntilUtc, after.CooldownUntilUtc);
        Assert.Equal(cleared.CooldownReason, after.CooldownReason);
        Assert.Equal(cleared.LastDispatchUtc, after.LastDispatchUtc);
        AssertSameState(untouched, rows[untouched.IndexerId]);
    }

    private static IndexerRequestState CooldownState(int indexerId) => new()
    {
        IndexerId = indexerId,
        ConsecutiveFailures = 4,
        CooldownUntilUtc = ClockUtc.AddHours(1),
        CooldownReason = "RateLimited",
        LastDispatchUtc = ClockUtc.AddSeconds(-2),
        UpdatedUtc = ClockUtc
    };

    private static void AssertSameState(IndexerRequestState expected, IndexerRequestState actual)
    {
        Assert.Equal(expected.IndexerId, actual.IndexerId);
        Assert.Equal(expected.ConsecutiveFailures, actual.ConsecutiveFailures);
        Assert.Equal(expected.CooldownUntilUtc, actual.CooldownUntilUtc);
        Assert.Equal(expected.CooldownReason, actual.CooldownReason);
        Assert.Equal(expected.LastDispatchUtc, actual.LastDispatchUtc);
        Assert.Equal(expected.UpdatedUtc, actual.UpdatedUtc);
    }

    private static async Task<List<string>> QueryAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(string.Join('|', Enumerable.Range(0, reader.FieldCount)
                .Select(ordinal => Convert.ToString(reader.GetValue(ordinal)))));
        }

        return rows;
    }
}
