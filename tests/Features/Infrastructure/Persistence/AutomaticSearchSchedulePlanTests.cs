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
using System.Text.RegularExpressions;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Common;
using Listenarr.Tests.Features.Infrastructure.Persistence.LargeLibrary;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// Runs EXPLAIN QUERY PLAN on the SQL EF really generates for the automatic
/// search's due page and pair lookup, captured from a command interceptor
/// rather than retyped, on a representative large library.
/// </summary>
[Trait("Name", "AutomaticSearchSchedulePlanTests")]
[Trait("Category", "Persistence")]
public sealed partial class AutomaticSearchSchedulePlanTests : BaseTests
{
    private const string DueIndexName = "IX_Audiobooks_AutomaticSearchDue";
    private const string PairKeyIndexName = "sqlite_autoindex_AudiobookIndexerSearchStates_1";
    private const int PageSize = AutomaticSearchBounds.DefaultMaxBooksPerCycle;
    private const int ExcludedIdCount = 20;
    private static readonly DateTime ClockUtc = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly ITestOutputHelper _output;

    public AutomaticSearchSchedulePlanTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task DuePage_ReadsTheDueIndex_BeforeAndAfterAnalyze()
    {
        await using var fixture = await BuildStampedAsync();
        var firstPage = await CaptureDuePageAsync(fixture, []);
        Assert.Equal(PageSize, firstPage.Ids.Count);
        // EF writes the exclusion three ways: omitted, "<>" for one id and
        // NOT IN for more, so each is planned.
        var exclusions = new (int[] Ids, string? Form)[]
        {
            ([], null),
            ([firstPage.Ids[0]], "\"Id\" <> @"),
            ([.. firstPage.Ids.Take(ExcludedIdCount)], "\"Id\" NOT IN (@")
        };
        var failures = new List<string>();

        foreach (var analyzed in new[] { false, true })
        {
            if (analyzed)
            {
                await ExecuteAsync(fixture, "ANALYZE;");
            }

            foreach (var (excluded, form) in exclusions)
            {
                var page = await CaptureDuePageAsync(fixture, excluded);
                var label = $"{(analyzed ? "after" : "before")} ANALYZE, {excluded.Length} excluded ids";
                _output.WriteLine($"== {label}\n{page.Command.Sql}\n-- plan\n{page.Plan}");
                Assert.True(
                    form is null ? !ExclusionPattern().IsMatch(page.Command.Sql) : page.Command.Sql.Contains(form, StringComparison.Ordinal),
                    $"{label}: expected the exclusion form {form ?? "omitted"}");
                failures.AddRange(DescribeDuePageProblems(page.Plan).Select(problem => $"{label}: {problem}"));
                Assert.Equal(PageSize, page.Ids.Count);
                Assert.Empty(page.Ids.Intersect(excluded));
                Assert.InRange(await CountRowsAsync(fixture, page.Command), 1, PageSize);
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public async Task PairLookup_ReadsThePrimaryKey_BeforeAndAfterAnalyze()
    {
        await using var fixture = await BuildStampedAsync();
        var pageIds = (await CaptureDuePageAsync(fixture, [])).Ids;

        foreach (var analyzed in new[] { false, true })
        {
            if (analyzed)
            {
                await ExecuteAsync(fixture, "ANALYZE;");
            }

            var capture = new CommandCapture();
            await using var context = new ListenArrDbContext(fixture.CreateOptions(capture));
            await context.Database.OpenConnectionAsync();
            var states = await new EfAutomaticSearchScheduleStore(context).GetPairStatesAsync(pageIds);

            var command = Assert.Single(capture.Commands);
            var plan = await ExplainAsync(context.Database.GetDbConnection(), command);
            _output.WriteLine($"== pair lookup, {(analyzed ? "after" : "before")} ANALYZE\n{command.Sql}\n-- plan\n{plan.Text}");
            Assert.Equal(pageIds.Count * LargeLibraryFixtureBuilder.SearchIndexerCount, states.Count);
            Assert.Matches($"USING (COVERING )?INDEX {PairKeyIndexName}", plan.Text);
            Assert.DoesNotMatch(@"\bSCAN (a|AudiobookIndexerSearchStates)\b", plan.Text);
            Assert.DoesNotContain("USE TEMP B-TREE", plan.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task LargeLibrary_HasTheSearchScheduleMix()
    {
        var builder = new LargeLibraryFixtureBuilder(ClockUtc);
        await using var fixture = await builder.BuildAsync();
        var counts = await builder.StampAutomaticSearchScheduleAsync(fixture);
        var due = $"\"AutomaticSearchDueUtc\" <= '{ClockUtc:yyyy-MM-dd HH:mm:ss}'";
        var eligible = "\"Monitored\" AND \"QualityProfileId\" IS NOT NULL";

        Assert.Equal(LargeLibraryFixtureBuilder.AudiobookCount, await ScalarAsync(fixture, "SELECT COUNT(*) FROM \"Audiobooks\""));
        Assert.Equal(10_000, await ScalarAsync(fixture, "SELECT COUNT(*) FROM \"Audiobooks\" WHERE NOT \"Monitored\""));
        Assert.Equal(0, await ScalarAsync(fixture, "SELECT COUNT(*) FROM \"Audiobooks\" WHERE \"QualityProfileId\" IS NULL"));
        Assert.Equal(
            LargeLibraryFixtureBuilder.SearchIndexerCount,
            await ScalarAsync(fixture, "SELECT COUNT(*) FROM \"Indexers\" WHERE \"IsEnabled\" AND \"EnableAutomaticSearch\""));
        Assert.Equal(LargeLibraryFixtureBuilder.SearchPairCount, counts.PairRows);
        Assert.Equal(LargeLibraryFixtureBuilder.SearchPairCount, await ScalarAsync(fixture, "SELECT COUNT(*) FROM \"AudiobookIndexerSearchStates\""));
        // Due times sit within 30 days either side of the clock, about half of them due.
        Assert.Equal(0, await ScalarAsync(fixture, $"SELECT COUNT(*) FROM \"Audiobooks\" WHERE \"AutomaticSearchDueUtc\" < '{ClockUtc.AddDays(-30):yyyy-MM-dd HH:mm:ss}' OR \"AutomaticSearchDueUtc\" > '{ClockUtc.AddDays(30):yyyy-MM-dd HH:mm:ss}'"));
        Assert.InRange(await ScalarAsync(fixture, $"SELECT COUNT(*) FROM \"Audiobooks\" WHERE {eligible} AND {due}"), 19_000, 21_000);
        Assert.Equal(counts.DueEligibleBooks, await ScalarAsync(fixture, $"SELECT COUNT(*) FROM \"Audiobooks\" WHERE {eligible} AND {due}"));
    }

    [Fact]
    public async Task DuePage_WithoutTheDueIndex_FailsThePlanAssertions()
    {
        // Control for the harness: with the index gone the same captured SQL
        // must be reported as a scan or a sort, or the assertions prove nothing.
        await using var fixture = await BuildStampedAsync();
        await ExecuteAsync(fixture, $"DROP INDEX \"{DueIndexName}\";");

        var page = await CaptureDuePageAsync(fixture, []);
        _output.WriteLine($"{page.Command.Sql}\n-- plan without the index\n{page.Plan}");

        Assert.NotEmpty(DescribeDuePageProblems(page.Plan));
    }

    [Fact]
    public async Task CanaryEligibilityQuery_ReadsEveryEligibleBookWithoutTheDueIndex()
    {
        // The query the sweep used before the schedule existed, on the same
        // library: its plan is recorded as the "before" side, and it hands
        // every eligible book to the caller with no page bound.
        await using var fixture = await BuildStampedAsync();
        var capture = new CommandCapture();
        await using var context = new ListenArrDbContext(fixture.CreateOptions(capture));
        await context.Database.OpenConnectionAsync();
        var books = await new AudiobookRepository(context).GetMonitoredAudiobooksForSearchAsync(ClockUtc.AddHours(-1));

        var command = Assert.Single(capture.Commands);
        var plan = await ExplainAsync(context.Database.GetDbConnection(), command);
        _output.WriteLine($"{command.Sql}\n-- plan\n{plan.Text}\n-- rows {books.Count}");

        Assert.DoesNotContain("LIMIT", command.Sql, StringComparison.Ordinal);
        Assert.Equal(40_000, books.Count);
        Assert.NotEmpty(DescribeDuePageProblems(plan));
    }

    private static async Task<LargeLibraryFixture> BuildStampedAsync()
    {
        var builder = new LargeLibraryFixtureBuilder(ClockUtc);
        var fixture = await builder.BuildAsync();
        try
        {
            await builder.StampAutomaticSearchScheduleAsync(fixture);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    // The page is read in a co-routine (the LIMIT sits in a subquery, the
    // profile join outside it). The table read inside it must come off the
    // index in order; a sort of the at most page-size rows the co-routine
    // yields is outside it and depends on the SQLite build, so it is recorded
    // rather than judged.
    private static IEnumerable<string> DescribeDuePageProblems(QueryPlan plan)
    {
        var tableRead = plan.CoRoutineBody();
        var text = string.Join("\n", tableRead);
        if (!text.Contains($"USING INDEX {DueIndexName}", StringComparison.Ordinal))
        {
            yield return $"the table read does not use {DueIndexName}";
        }

        if (Regex.IsMatch(plan.Text, @"\bSCAN (a|Audiobooks)\b"))
        {
            yield return "the plan scans Audiobooks";
        }

        if (text.Contains("USE TEMP B-TREE", StringComparison.Ordinal))
        {
            yield return "the table read sorts in a temp B-tree";
        }
    }

    private static async Task<CapturedPage> CaptureDuePageAsync(
        LargeLibraryFixture fixture,
        IReadOnlyCollection<int> excludedIds)
    {
        var capture = new CommandCapture();
        await using var context = new ListenArrDbContext(fixture.CreateOptions(capture));
        // Keep EF's connection open so the plan is explained on it.
        await context.Database.OpenConnectionAsync();
        var page = await new EfAutomaticSearchScheduleStore(context)
            .GetDuePageAsync(fixture.ClockUtc, excludedIds, PageSize);

        var command = Assert.Single(capture.Commands);
        return new CapturedPage(
            command,
            await ExplainAsync(context.Database.GetDbConnection(), command),
            [.. page.Select(book => book.Id)]);
    }

    // Re-runs the captured statement to count the rows it hands to EF.
    private static async Task<int> CountRowsAsync(LargeLibraryFixture fixture, CapturedCommand captured)
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = CreateCommand(connection, $"SELECT COUNT(*) FROM ({captured.Sql})", captured);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<QueryPlan> ExplainAsync(DbConnection connection, CapturedCommand captured)
    {
        await using var command = CreateCommand(connection, "EXPLAIN QUERY PLAN " + captured.Sql, captured);
        var depthById = new Dictionary<long, int> { [0] = -1 };
        var nodes = new List<(int Depth, string Detail)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var depth = depthById.GetValueOrDefault(reader.GetInt64(1), -1) + 1;
            depthById[reader.GetInt64(0)] = depth;
            nodes.Add((depth, reader.GetString(3)));
        }

        return new QueryPlan(nodes);
    }

    private static DbCommand CreateCommand(DbConnection connection, string sql, CapturedCommand captured)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in captured.Parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private static async Task<int> ScalarAsync(LargeLibraryFixture fixture, string sql)
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(LargeLibraryFixture fixture, string sql)
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    [GeneratedRegex("\"Id\" (<>|NOT IN)")]
    private static partial Regex ExclusionPattern();

    private sealed record CapturedPage(CapturedCommand Command, QueryPlan Plan, IReadOnlyList<int> Ids);

    private sealed record CapturedCommand(
        string Sql,
        IReadOnlyList<(string Name, object? Value)> Parameters);

    /// <summary>An EXPLAIN QUERY PLAN result, one node per row with its depth.</summary>
    private sealed record QueryPlan(IReadOnlyList<(int Depth, string Detail)> Nodes)
    {
        public string Text => string.Join("\n", Nodes.Select(node => new string(' ', node.Depth * 2) + node.Detail));

        // The nodes under the first CO-ROUTINE, or the whole plan when the
        // query is not split into one.
        public IReadOnlyList<string> CoRoutineBody()
        {
            var start = Nodes.ToList().FindIndex(node => node.Detail.StartsWith("CO-ROUTINE", StringComparison.Ordinal));
            if (start < 0)
            {
                return [.. Nodes.Select(node => node.Detail)];
            }

            return [.. Nodes.Skip(start + 1)
                .TakeWhile(node => node.Depth > Nodes[start].Depth)
                .Select(node => node.Detail)];
        }

        public override string ToString() => Text;
    }

    /// <summary>Records the text and parameter values of every query EF runs.</summary>
    private sealed class CommandCapture : DbCommandInterceptor
    {
        public List<CapturedCommand> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(new CapturedCommand(
                command.CommandText,
                command.Parameters
                    .Cast<DbParameter>()
                    .Select(parameter => (parameter.ParameterName, (object?)parameter.Value))
                    .ToList()));
            return ValueTask.FromResult(result);
        }
    }
}
