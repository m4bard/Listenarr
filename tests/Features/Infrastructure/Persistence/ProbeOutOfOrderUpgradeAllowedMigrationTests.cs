using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

// PROBE for the trial branch only, never part of the PR: a database that has already run
// every canary migration, including ones newer than AddQualityProfileUpgradeAllowed, and
// then receives that older migration.
[Trait("Area", "Persistence")]
[Trait("Name", "ProbeOutOfOrderUpgradeAllowedMigrationTests")]
[Trait("Category", "Infrastructure")]
public class ProbeOutOfOrderUpgradeAllowedMigrationTests : BaseTests
{
    private const string UpgradeAllowedMigrationId = "20261112054132_AddQualityProfileUpgradeAllowed";

    [Fact]
    public async Task OlderMigration_AppliesAfterNewerOnesAreAlreadyApplied()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name))
            .Options;
        await using var context = new ListenArrDbContext(options);
        await context.Database.MigrateAsync();

        // Make it a canary database that never had this PR: no column, no history row.
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE QualityProfiles DROP COLUMN UpgradeAllowed;");
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM __EFMigrationsHistory WHERE MigrationId = {0};", UpgradeAllowedMigrationId);

        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
        var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
        Assert.Equal(new[] { UpgradeAllowedMigrationId }, pending);
        Assert.Contains("20261121052354_AddMonitoringQualityProfile", applied);
        Assert.True(string.CompareOrdinal(applied[^1], UpgradeAllowedMigrationId) > 0);

        await context.GetService<IMigrator>().MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('QualityProfiles') WHERE name = 'UpgradeAllowed';";
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }
}
