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

using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using Listenarr.Infrastructure.Search.RequestBudget;
using Listenarr.Tests.Common;
using Listenarr.Tests.Features.Infrastructure.Persistence.LargeLibrary;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestGate.IndexerRequestGateHarness;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestBudget;

/// <summary>
/// Runs EXPLAIN QUERY PLAN on every statement the daily request budget runs, as the
/// reservation really sends it (captured from a command interceptor, with its parameter values,
/// rather than retyped), on the large library with a pruned and an unpruned usage history,
/// before and after ANALYZE.
/// </summary>
[Trait("Name", "IndexerRequestBudgetPlanTests")]
[Trait("Category", "Infrastructure")]
public sealed class IndexerRequestBudgetPlanTests : BaseTests
{
    private const string UsageTable = "IndexerRequestUsages";
    private const string UsageKeyIndex = "sqlite_autoindex_IndexerRequestUsages_1";
    private const string UsageHourIndex = "IX_IndexerRequestUsages_HourStartUtc";
    private const string WindowSearch = $"USING INDEX {UsageKeyIndex} (IndexerId=? AND HourStartUtc>?)";
    // With foreign keys enforced, as the application's connections have them, SQLite collects
    // the rows to delete from the hour index alone before deleting them, so it reads as covering.
    private const string PruneSearch = $"USING COVERING INDEX {UsageHourIndex} (HourStartUtc<?)";
    private const string KeySearch = "USING INTEGER PRIMARY KEY (rowid=?)";
    private static readonly DateTime ClockUtc = new(2026, 3, 1, 12, 30, 0, DateTimeKind.Utc);

    private readonly ITestOutputHelper _output;

    public IndexerRequestBudgetPlanTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(IndexerRequestUsageVariant.Pruned)]
    [InlineData(IndexerRequestUsageVariant.Unpruned)]
    public async Task Plan_WindowSum_BucketRead_Prune_LimitRead(IndexerRequestUsageVariant variant)
    {
        // Given: the large library, five budgeted indexers and their usage history.
        var builder = new LargeLibraryFixtureBuilder(ClockUtc);
        await using var fixture = await builder.BuildAsync();
        var indexerIds = await builder.AddIndexerRequestUsageAsync(fixture, variant);
        var hours = variant == IndexerRequestUsageVariant.Pruned
            ? LargeLibraryFixtureBuilder.PrunedUsageHoursPerIndexer
            : LargeLibraryFixtureBuilder.UnprunedUsageHoursPerIndexer;
        Assert.Equal(indexerIds.Count * hours, await ScalarAsync(fixture, $"SELECT COUNT(*) FROM \"{UsageTable}\""));
        // EF reads the rows the fixture wrote, so the hours are stored in the form EF compares.
        Assert.Equal(
            Enumerable.Range(0, 25).Sum(LargeLibraryFixtureBuilder.UsageCountForHoursAgo),
            await WindowSumThroughEfAsync(fixture, indexerIds[0]));

        // When
        var statements = await CaptureStatementsAsync(fixture, indexerIds[0]);

        // Then
        _output.WriteLine(
            $"SQLite {await ScalarTextAsync(fixture, "SELECT sqlite_version()")}, "
            + $"foreign_keys={await ScalarTextAsync(fixture, "PRAGMA foreign_keys")}");
        var failures = new List<string>();
        foreach (var analyzed in new[] { false, true })
        {
            if (analyzed)
            {
                await ExecuteAsync(fixture, "ANALYZE;");
            }

            foreach (var statement in statements)
            {
                var plan = await ExplainAsync(fixture, statement.Command);
                var label = $"{variant}, {(analyzed ? "after" : "before")} ANALYZE, {statement.Name}";
                _output.WriteLine($"== {label}\n{statement.Command.Sql}\n-- plan\n{plan}");
                failures.AddRange(DescribePlanProblems(statement, plan).Select(problem => $"{label}: {problem}"));
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
        // The capture never wrote: the history is the one the plans were taken on.
        Assert.Equal(indexerIds.Count * hours, await ScalarAsync(fixture, $"SELECT COUNT(*) FROM \"{UsageTable}\""));
    }

    /// <summary>
    /// Drives the reservation through an admitted charge, a refused charge and a peek, and
    /// returns each distinct statement it sent. Writes are recorded but not run, with the rows
    /// affected the step needs, so the history keeps its shape and every path is reached.
    /// </summary>
    private static async Task<IReadOnlyList<BudgetStatement>> CaptureStatementsAsync(
        LargeLibraryFixture fixture,
        int indexerId)
    {
        var capture = new CommandCapture();
        var reservation = new EfIndexerRequestReservation(
            new PooledDbContextFactory<ListenArrDbContext>(fixture.CreateOptions(capture)),
            new ManualClock(new DateTimeOffset(ClockUtc)),
            NullLogger<EfIndexerRequestReservation>.Instance);
        var request = new IndexerRequestContext(indexerId, SearchOrigin.Interactive, IndexerRequestKind.Search);

        capture.RowsAffected = 1;
        Assert.Null(await reservation.TryReserveAsync(request, CancellationToken.None));
        var admitted = capture.Take();
        capture.RowsAffected = 0;
        var refusal = await reservation.TryReserveAsync(request, CancellationToken.None);
        Assert.Equal(RequestDeferralReason.AllowanceExhausted, refusal?.Reason);
        var refused = capture.Take();
        Assert.Null(await reservation.PeekAsync(indexerId, SearchOrigin.Interactive, CancellationToken.None));
        var peeked = capture.Take();

        // Every call reads the limit and the reserve first, with the same statements.
        Assert.Equal(4, admitted.Count);
        Assert.Equal(4, refused.Count);
        Assert.Equal(3, peeked.Count);
        foreach (var call in new[] { refused, peeked })
        {
            Assert.Equal(admitted[0].Sql, call[0].Sql);
            Assert.Equal(admitted[1].Sql, call[1].Sql);
        }

        Assert.Equal(admitted[2].Sql, refused[2].Sql);
        return
        [
            new BudgetStatement("limit read", admitted[0], "Indexers", KeySearch),
            new BudgetStatement("settings read", admitted[1], "ApplicationSettings", KeySearch),
            new BudgetStatement("window sum", admitted[2], UsageTable, WindowSearch),
            new BudgetStatement("prune", admitted[3], UsageTable, PruneSearch),
            new BudgetStatement("bucket read", refused[3], UsageTable, WindowSearch),
            new BudgetStatement("peek sum", peeked[2], UsageTable, WindowSearch)
        ];
    }

    private static IEnumerable<string> DescribePlanProblems(BudgetStatement statement, string plan)
    {
        var lines = plan.Split('\n').Select(line => line.Trim()).ToList();
        var source = TableReference(statement.Command.Sql, statement.Table);
        var expected = $"SEARCH {source} {statement.ExpectedSearch}";
        if (!lines.Contains(expected))
        {
            yield return $"the plan has no line \"{expected}\"";
        }

        var usageScan = new Regex($@"^SCAN ({Regex.Escape(TableReference(statement.Command.Sql, UsageTable))}|{UsageTable})\b");
        if (lines.Any(usageScan.IsMatch))
        {
            yield return $"the plan scans {UsageTable}";
        }

        if (plan.Contains("TEMP B-TREE", StringComparison.Ordinal))
        {
            yield return "the plan uses a temp B-tree";
        }
    }

    // EF names each table with an alias, and the plan names the alias; hand-written SQL has none.
    private static string TableReference(string sql, string table)
    {
        var alias = Regex.Match(sql, $"FROM \"{Regex.Escape(table)}\" AS \"(?<alias>[^\"]+)\"");
        return alias.Success ? alias.Groups["alias"].Value : table;
    }

    private static async Task<int> WindowSumThroughEfAsync(LargeLibraryFixture fixture, int indexerId)
    {
        var windowStart = IndexerRequestBudgetPolicy.WindowStartUtc(
            IndexerRequestBudgetPolicy.HourStartUtc(new DateTimeOffset(ClockUtc)));
        await using var context = new ListenArrDbContext(fixture.CreateOptions());
        return await context.IndexerRequestUsages
            .Where(usage => usage.IndexerId == indexerId && usage.HourStartUtc >= windowStart)
            .SumAsync(usage => usage.Count);
    }

    // A new connection for every plan, so one taken after ANALYZE reads the new statistics.
    private static async Task<string> ExplainAsync(LargeLibraryFixture fixture, CapturedCommand captured)
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + captured.Sql;
        foreach (var (name, value) in captured.Parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var depthById = new Dictionary<long, int> { [0] = -1 };
        var plan = new StringBuilder();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var depth = depthById.GetValueOrDefault(reader.GetInt64(1), -1) + 1;
            depthById[reader.GetInt64(0)] = depth;
            plan.Append(' ', depth * 2).AppendLine(reader.GetString(3));
        }

        return plan.ToString().TrimEnd();
    }

    private static async Task<int> ScalarAsync(LargeLibraryFixture fixture, string sql) =>
        Convert.ToInt32(await ScalarTextAsync(fixture, sql));

    private static async Task<string> ScalarTextAsync(LargeLibraryFixture fixture, string sql)
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(LargeLibraryFixture fixture, string sql)
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed record BudgetStatement(
        string Name,
        CapturedCommand Command,
        string Table,
        string ExpectedSearch);

    private sealed record CapturedCommand(
        string Sql,
        IReadOnlyList<(string Name, object? Value)> Parameters);

    /// <summary>
    /// Records the text and parameter values of every command EF runs. Writes are not run:
    /// each reports <see cref="RowsAffected"/> instead.
    /// </summary>
    private sealed class CommandCapture : DbCommandInterceptor
    {
        private readonly List<CapturedCommand> _commands = [];

        public int RowsAffected { get; set; }

        public List<CapturedCommand> Take()
        {
            var taken = _commands.ToList();
            _commands.Clear();
            return taken;
        }

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
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(RowsAffected));
        }

        private void Record(DbCommand command) => _commands.Add(new CapturedCommand(
            command.CommandText,
            command.Parameters
                .Cast<DbParameter>()
                .Select(parameter => (parameter.ParameterName, (object?)parameter.Value))
                .ToList()));
    }
}
