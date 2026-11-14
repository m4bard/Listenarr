using System.Data.Common;
using Listenarr.Application.Search.Discovery;
using Listenarr.Domain.Search.Discovery;
using Listenarr.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Listenarr.Tests.Features.Infrastructure.Persistence.LargeLibrary;

/// <summary>One statement EF sent, with the parameter values it was sent with.</summary>
internal sealed record CapturedStatement(string Sql, IReadOnlyList<KeyValuePair<string, object?>> Parameters);

/// <summary>The plan SQLite chose for one captured statement, before and after ANALYZE.</summary>
internal sealed record StatementPlan(
    string Name,
    CapturedStatement Statement,
    IReadOnlyList<string> BeforeAnalyze,
    IReadOnlyList<string> AfterAnalyze);

/// <summary>
/// Runs the real discovery store against a large-library fixture, captures the SQL EF generates
/// for the batch read (Q1), the prune (Q3), the open-gap read (Q4) and the status checkpoint
/// read (Q6), and asks SQLite for the query plan of exactly that SQL.
/// </summary>
internal static class DiscoveryQueryPlanCapture
{
    private const int BatchLimit = 200;
    private const int GapLimit = 20;
    private const int PruneLimitPerState = 1_000;

    public static async Task<IReadOnlyList<StatementPlan>> CaptureAsync(
        LargeLibraryFixture fixture,
        LargeLibraryDiscoveryCounts counts)
    {
        var recorder = new StatementRecorder();
        var store = new EfDiscoveryStore(new OptionsFactory(fixture.CreateOptions(recorder)));
        var first = counts.IndexerIds[0];
        var cutoff = fixture.ClockUtc.AddDays(-30);
        var statements = new List<(string Name, CapturedStatement Statement)>();

        await store.GetBatchAsync(DiscoveredReleaseState.Pending, BatchLimit, CancellationToken.None);
        statements.Add(("Q1 batch Pending", Take(recorder, "DiscoveredReleases")));
        await store.GetBatchAsync(DiscoveredReleaseState.Matched, BatchLimit, CancellationToken.None);
        statements.Add(("Q1 batch Matched", Take(recorder, "DiscoveredReleases")));
        await store.GetStatusAsync(first, GapLimit, CancellationToken.None);
        statements.Add(("Q6 status checkpoint", Take(recorder, "IndexerDiscoveryCheckpoints")));
        statements.Add(("Q4 open gaps", Take(recorder, "IndexerCoverageGaps")));
        await store.PruneAsync(cutoff, PruneLimitPerState, CancellationToken.None);
        statements.Add(("Q3 prune Handled", Take(recorder, "DiscoveredReleases")));
        statements.Add(("Q3 prune Ignored", Take(recorder, "DiscoveredReleases")));

        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var before = await PlanAllAsync(connection, statements);
        await ExecuteAsync(connection, "ANALYZE");
        var after = await PlanAllAsync(connection, statements);
        return statements
            .Select((entry, position) => new StatementPlan(entry.Name, entry.Statement, before[position], after[position]))
            .ToList();
    }

    public static async Task DropPlanIndexesAsync(LargeLibraryFixture fixture)
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, "DROP INDEX \"IX_DiscoveredReleases_State_FirstSeenUtc\"");
        await ExecuteAsync(connection, "DROP INDEX \"IX_IndexerCoverageGaps_IndexerId_DetectedUtc\"");
    }

    // The first recorded statement that names the table; later ones stay for the next Take.
    private static CapturedStatement Take(StatementRecorder recorder, string table)
    {
        var statement = recorder.Statements.First(candidate => candidate.Sql.Contains(table, StringComparison.Ordinal));
        recorder.Statements.Remove(statement);
        return statement;
    }

    private static async Task<List<IReadOnlyList<string>>> PlanAllAsync(
        SqliteConnection connection,
        List<(string Name, CapturedStatement Statement)> statements)
    {
        var plans = new List<IReadOnlyList<string>>();
        foreach (var (_, statement) in statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "EXPLAIN QUERY PLAN " + statement.Sql;
            foreach (var (name, value) in statement.Parameters)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            var lines = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lines.Add(reader.GetString(3));
            }

            plans.Add(lines);
        }

        return plans;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class StatementRecorder : DbCommandInterceptor
    {
        public List<CapturedStatement> Statements { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Record(DbCommand command) => Statements.Add(new CapturedStatement(
            command.CommandText,
            command.Parameters.Cast<DbParameter>()
                .Select(parameter => KeyValuePair.Create(parameter.ParameterName, (object?)parameter.Value))
                .ToList()));
    }

    private sealed class OptionsFactory(DbContextOptions<ListenArrDbContext> options)
        : IDbContextFactory<ListenArrDbContext>
    {
        public ListenArrDbContext CreateDbContext() => new(options);
    }
}
