using System.Diagnostics;
using Listenarr.Domain.Search.Discovery;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Persistence.LargeLibrary;

/// <summary>
/// Query plans of the discovery store on the generated large library: 250,000 discovered
/// releases, 10,000 coverage gaps, 50,000 audiobooks. The SQL is what EF generates for the real
/// store methods, and the plans are SQLite's, read before and after ANALYZE.
/// </summary>
[Trait("Area", "Persistence")]
[Trait("Name", "DiscoveryQueryPlanTests")]
[Trait("Category", "Infrastructure")]
public sealed class DiscoveryQueryPlanTests(ITestOutputHelper output) : BaseTests
{
    private const string StateIndex = "IX_DiscoveredReleases_State_FirstSeenUtc";
    private const string GapIndex = "IX_IndexerCoverageGaps_IndexerId_DetectedUtc";
    private static readonly DateTime ClockUtc = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Plans_OnTheLargeLibrary_SearchTheNamedIndexesWithoutScanOrSort()
    {
        await using var fixture = await BuildAsync();
        var plans = await DiscoveryQueryPlanCapture.CaptureAsync(fixture.Fixture, fixture.Counts);
        Report(plans);

        foreach (var plan in plans)
        {
            foreach (var lines in new[] { plan.BeforeAnalyze, plan.AfterAnalyze })
            {
                Assert.DoesNotContain(lines, line => line.StartsWith("SCAN", StringComparison.Ordinal));
                Assert.DoesNotContain(lines, line => line.Contains("TEMP B-TREE", StringComparison.Ordinal));
                Assert.Contains(lines, line => line.StartsWith("SEARCH", StringComparison.Ordinal)
                    && line.Contains(ExpectedAccess(plan.Name), StringComparison.Ordinal));
            }
        }

        Assert.DoesNotContain(
            plans.Where(plan => plan.Name.StartsWith("Q4", StringComparison.Ordinal)
                || plan.Name.StartsWith("Q6", StringComparison.Ordinal)),
            plan => plan.Statement.Sql.Contains("DiscoveredReleases", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Plans_WithTheIndexesDropped_ScanAndSort()
    {
        await using var fixture = await BuildAsync();
        await DiscoveryQueryPlanCapture.DropPlanIndexesAsync(fixture.Fixture);
        var plans = await DiscoveryQueryPlanCapture.CaptureAsync(fixture.Fixture, fixture.Counts);
        Report(plans);

        foreach (var plan in plans.Where(plan => !plan.Name.StartsWith("Q6", StringComparison.Ordinal)))
        {
            Assert.Contains(plan.BeforeAnalyze, line => line.StartsWith("SCAN", StringComparison.Ordinal));
            Assert.Contains(plan.BeforeAnalyze, line => line.Contains("TEMP B-TREE", StringComparison.Ordinal));
        }
    }

    private static string ExpectedAccess(string queryName) => queryName switch
    {
        var name when name.StartsWith("Q1", StringComparison.Ordinal) => StateIndex,
        var name when name.StartsWith("Q3", StringComparison.Ordinal) => "COVERING INDEX " + StateIndex,
        var name when name.StartsWith("Q4", StringComparison.Ordinal) => GapIndex,
        _ => "PRIMARY KEY"
    };

    private async Task<SeededLibrary> BuildAsync()
    {
        var builder = new LargeLibraryFixtureBuilder(ClockUtc);
        var timer = Stopwatch.StartNew();
        var fixture = await builder.BuildAsync();
        var buildTime = timer.Elapsed;
        timer.Restart();
        var counts = await builder.SeedDiscoveryAsync(fixture);
        output.WriteLine($"BuildAsync {buildTime.TotalSeconds:F1}s, SeedDiscoveryAsync {timer.Elapsed.TotalSeconds:F1}s");
        output.WriteLine(
            $"rows {counts.Total}: Pending {counts.InState(DiscoveredReleaseState.Pending)}, "
            + $"Matched {counts.InState(DiscoveredReleaseState.Matched)}, "
            + $"Handled {counts.InState(DiscoveredReleaseState.Handled)}, "
            + $"Ignored {counts.InState(DiscoveredReleaseState.Ignored)}");
        await AssertSeededRowCountsAsync(fixture, counts);
        return new SeededLibrary(fixture, counts);
    }

    private static async Task AssertSeededRowCountsAsync(
        LargeLibraryFixture fixture,
        LargeLibraryDiscoveryCounts counts)
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.Equal(LargeLibraryFixtureBuilder.DiscoveredReleaseCount, await CountAsync(connection, "DiscoveredReleases"));
        Assert.Equal(LargeLibraryFixtureBuilder.CoverageGapCount, await CountAsync(connection, "IndexerCoverageGaps"));
        Assert.Equal(LargeLibraryFixtureBuilder.DiscoveryIndexerCount, await CountAsync(connection, "IndexerDiscoveryCheckpoints"));
        Assert.Equal(2_500, counts.InState(DiscoveredReleaseState.Pending));
        Assert.Equal(500, counts.InState(DiscoveredReleaseState.Matched));
    }

    private static async Task<long> CountAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private void Report(IReadOnlyList<StatementPlan> plans)
    {
        foreach (var plan in plans)
        {
            output.WriteLine($"--- {plan.Name}");
            output.WriteLine(plan.Statement.Sql);
            output.WriteLine("params: " + string.Join(", ", plan.Statement.Parameters.Select(p => $"{p.Key}={p.Value}")));
            plan.BeforeAnalyze.ToList().ForEach(line => output.WriteLine("  before ANALYZE: " + line));
            plan.AfterAnalyze.ToList().ForEach(line => output.WriteLine("  after ANALYZE:  " + line));
        }
    }

    private sealed class SeededLibrary(LargeLibraryFixture fixture, LargeLibraryDiscoveryCounts counts)
        : IAsyncDisposable
    {
        public LargeLibraryFixture Fixture { get; } = fixture;

        public LargeLibraryDiscoveryCounts Counts { get; } = counts;

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();
    }
}
