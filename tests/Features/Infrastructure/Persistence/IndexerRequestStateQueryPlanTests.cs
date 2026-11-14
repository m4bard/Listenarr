/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using System.Data.Common;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// Explains the SQL the indexer request state store really sends, captured from EF, on a
/// database with one state row for each of five indexers, before and after ANALYZE.
/// </summary>
[Trait("Name", "IndexerRequestStateQueryPlanTests")]
[Trait("Category", "Infrastructure")]
public sealed class IndexerRequestStateQueryPlanTests(ITestOutputHelper output) : BaseTests
{
    private const int IndexerCount = 5;
    private const string PrimaryKeySearch =
        "SEARCH IndexerRequestStates USING INTEGER PRIMARY KEY (rowid=?)";
    private static readonly DateTime ClockUtc = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task StoreStatements_UseThePrimaryKeyAndLoadOneRowPerIndexer()
    {
        var capture = new CommandCapture();
        await using var database = await IndexerRequestStateTestDatabase.CreateAsync(
            IndexerCount,
            targetMigration: null,
            pooling: false,
            capture);
        var store = new EfIndexerRequestStateStore(database.Factory);
        CapturedCommand? upsert = null;
        foreach (var indexerId in database.IndexerIds)
        {
            capture.Commands.Clear();
            await store.UpsertAsync(new IndexerRequestState
            {
                IndexerId = indexerId,
                ConsecutiveFailures = 3,
                CooldownUntilUtc = ClockUtc.AddHours(1),
                CooldownReason = "RateLimited",
                LastDispatchUtc = ClockUtc,
                UpdatedUtc = ClockUtc
            });
            upsert = Assert.Single(capture.Commands);
        }

        capture.Commands.Clear();
        await store.ClearConsecutiveFailuresAsync(database.IndexerIds[2], ClockUtc);
        var clear = Assert.Single(capture.Commands);
        capture.Commands.Clear();
        var loaded = await store.LoadAllAsync();
        var loadAll = Assert.Single(capture.Commands);

        Assert.Equal(IndexerCount, loaded.Count);
        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync();
        output.WriteLine($"SQLite {connection.ServerVersion}");
        foreach (var (name, captured) in new[] { ("clear", clear), ("load all", loadAll), ("upsert", upsert!) })
        {
            output.WriteLine($"{name}: {captured.Sql}");
        }

        foreach (var phase in new[] { "before ANALYZE", "after ANALYZE" })
        {
            if (phase == "after ANALYZE")
            {
                await ExecuteAsync(connection, "ANALYZE");
            }

            var clearPlan = await ExplainQueryPlanAsync(connection, clear);
            var loadAllPlan = await ExplainQueryPlanAsync(connection, loadAll);
            var upsertPlan = await ExplainQueryPlanAsync(connection, upsert!);
            var upsertOpcodes = await ExplainOpcodesAsync(connection, upsert!);
            output.WriteLine($"{phase}: clear [{string.Join("; ", clearPlan)}]");
            output.WriteLine($"{phase}: load all [{string.Join("; ", loadAllPlan)}]");
            output.WriteLine($"{phase}: upsert plan [{string.Join("; ", upsertPlan)}] opcodes [{string.Join(' ', upsertOpcodes)}]");

            // EF aliases the table, so the plans name the alias rather than the table.
            Assert.Contains("FROM \"IndexerRequestStates\" AS ", loadAll.Sql);
            var clearSearch = Assert.Single(clearPlan);
            Assert.StartsWith("SEARCH ", clearSearch);
            Assert.EndsWith(" USING INTEGER PRIMARY KEY (rowid=?)", clearSearch);
            // Loading every row reads the whole table by design: it holds one row per indexer.
            Assert.Matches("^SCAN \\w+$", Assert.Single(loadAllPlan));
            // A single-row INSERT has no query plan. Its conflict check is a rowid probe
            // (NotExists) and nothing in it loops over a table (Rewind).
            Assert.Empty(upsertPlan);
            Assert.Contains("NotExists", upsertOpcodes);
            Assert.DoesNotContain("Rewind", upsertOpcodes);
        }

        // The same assertions are live: a filter on a column that is not the key scans.
        Assert.Equal(
            ["SCAN IndexerRequestStates"],
            await ExplainQueryPlanAsync(connection, new CapturedCommand(
                "SELECT \"IndexerId\" FROM \"IndexerRequestStates\" WHERE \"CooldownReason\" = 'RateLimited'",
                [])));
        Assert.Equal(
            [PrimaryKeySearch],
            await ExplainQueryPlanAsync(connection, new CapturedCommand(
                "SELECT \"CooldownReason\" FROM \"IndexerRequestStates\" WHERE \"IndexerId\" = 1",
                [])));
        Assert.Contains("Rewind", await ExplainOpcodesAsync(connection, loadAll));
    }

    private static async Task<List<string>> ExplainQueryPlanAsync(
        SqliteConnection connection,
        CapturedCommand captured)
    {
        await using var command = CreateCommand(connection, "EXPLAIN QUERY PLAN " + captured.Sql, captured);
        await using var reader = await command.ExecuteReaderAsync();
        var details = new List<string>();
        while (await reader.ReadAsync())
        {
            details.Add(reader.GetString(reader.GetOrdinal("detail")));
        }

        return details;
    }

    private static async Task<List<string>> ExplainOpcodesAsync(
        SqliteConnection connection,
        CapturedCommand captured)
    {
        await using var command = CreateCommand(connection, "EXPLAIN " + captured.Sql, captured);
        await using var reader = await command.ExecuteReaderAsync();
        var opcodes = new List<string>();
        while (await reader.ReadAsync())
        {
            opcodes.Add(reader.GetString(reader.GetOrdinal("opcode")));
        }

        return opcodes;
    }

    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        string sql,
        CapturedCommand captured)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in captured.Parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed record CapturedCommand(
        string Sql,
        IReadOnlyList<(string Name, object? Value)> Parameters);

    /// <summary>Records the text and parameter values of every command EF sends.</summary>
    private sealed class CommandCapture : DbCommandInterceptor
    {
        public List<CapturedCommand> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        private void Record(DbCommand command) => Commands.Add(new CapturedCommand(
            command.CommandText,
            command.Parameters
                .Cast<DbParameter>()
                .Select(parameter => (parameter.ParameterName, (object?)parameter.Value))
                .ToList()));
    }
}
