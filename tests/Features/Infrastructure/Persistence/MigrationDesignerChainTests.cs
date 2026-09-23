/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Domain.Configuration;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// Guards the migration Designers (each migration's TargetModel) of a composed stack.
///
/// Every item scaffolds its migration against its own base, so its Designer only knows that
/// item's schema. Composed, the migration order is fine but the Designers are not, and nothing
/// else notices: EF builds a migration's Down SQL against the PREVIOUS migration's TargetModel,
/// and on SQLite a DropColumn in Down is a table rebuild whose column list comes from that model.
/// A stale predecessor rebuilds the table without the columns it does not know about, silently
/// dropping other items' columns and their data. Measured before this guard existed: stepping
/// down through AddHousekeepingRetention dropped sixteen unrelated ApplicationSettings columns,
/// and the downgrade then failed three steps later on a column already gone.
///
/// The frontier is the last migration at the canary base the stack is built on. Everything
/// after it is checked, which includes any upstream migration that arrives later.
/// </summary>
[Trait("Area", "Persistence")]
[Trait("Name", "MigrationDesignerChainTests")]
[Trait("Category", "Infrastructure")]
public class MigrationDesignerChainTests : BaseTests
{
    private const string FrontierMigrationId = "20260825021432_AddWeakStorageVerifiedCleanup";

    // Item 129 unmapped Indexer.Tags without a migration, on purpose: existing databases keep the
    // column, orphaned and harmless. The snapshot describes the mapped model and so lacks it; the
    // Designers drive Down-path table rebuilds, which act on the physical table, and so carry it.
    // Leaving it out of them would make every Down that rebuilds Indexers drop the column, and no
    // Up would ever bring it back.
    private const string PhysicalOnlyDifference = "DropColumn Indexers.Tags";

    private static DbContextOptions<ListenArrDbContext> CreateOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name))
            .Options;

    private static string Describe(MigrationOperation operation) => operation switch
    {
        AddColumnOperation o => $"AddColumn {o.Table}.{o.Name}",
        DropColumnOperation o => $"DropColumn {o.Table}.{o.Name}",
        AlterColumnOperation o => $"AlterColumn {o.Table}.{o.Name}",
        RenameColumnOperation o => $"RenameColumn {o.Table}.{o.Name}->{o.NewName}",
        CreateTableOperation o => $"CreateTable {o.Name}",
        DropTableOperation o => $"DropTable {o.Name}",
        RenameTableOperation o => $"RenameTable {o.Name}->{o.NewName}",
        CreateIndexOperation o => $"CreateIndex {o.Table}.{o.Name}",
        DropIndexOperation o => $"DropIndex {o.Name}",
        RenameIndexOperation o => $"RenameIndex {o.Name}->{o.NewName}",
        AddForeignKeyOperation o => $"AddForeignKey {o.Table}.{o.Name}",
        DropForeignKeyOperation o => $"DropForeignKey {o.Table}.{o.Name}",
        AddPrimaryKeyOperation o => $"AddPrimaryKey {o.Table}.{o.Name}",
        DropPrimaryKeyOperation o => $"DropPrimaryKey {o.Table}.{o.Name}",
        AddUniqueConstraintOperation o => $"AddUniqueConstraint {o.Table}.{o.Name}",
        DropUniqueConstraintOperation o => $"DropUniqueConstraint {o.Table}.{o.Name}",
        AddCheckConstraintOperation o => $"AddCheckConstraint {o.Table}.{o.Name}",
        DropCheckConstraintOperation o => $"DropCheckConstraint {o.Table}.{o.Name}",
        _ => operation.GetType().Name,
    };

    // Raw SQL and seed data do not appear in a model diff; everything else a migration does must.
    private static bool IsSchemaOperation(MigrationOperation operation) =>
        operation is not (SqlOperation or InsertDataOperation or UpdateDataOperation or DeleteDataOperation);

    private sealed record ChainStep(string Id, Migration Migration, IModel Target);

    private static List<ChainStep> StepsFromFrontier(ListenArrDbContext context)
    {
        var assembly = context.GetService<IMigrationsAssembly>();
        var initializer = context.GetService<IModelRuntimeInitializer>();
        var provider = context.Database.ProviderName!;
        var steps = new List<ChainStep>();
        foreach (var (id, type) in assembly.Migrations)
        {
            if (string.CompareOrdinal(id, FrontierMigrationId) < 0)
            {
                continue;
            }

            var migration = assembly.CreateMigration(type, provider);
            Assert.NotNull(migration.TargetModel);
            steps.Add(new ChainStep(id, migration, Finalize(initializer, migration.TargetModel!)));
        }

        Assert.Equal(FrontierMigrationId, steps[0].Id);
        return steps;
    }

    private static IModel Finalize(IModelRuntimeInitializer initializer, IModel model) =>
        initializer.Initialize(
            model is IMutableModel mutable ? mutable.FinalizeModel() : model,
            designTime: true,
            validationLogger: null);

    [Fact]
    [Trait("Scenario", "DesignerIsPredecessorPlusOwnUp")]
    public void EveryDesignerAfterTheFrontier_IsItsPredecessorPlusItsOwnUpOperations()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        using var context = new ListenArrDbContext(CreateOptions(connection));
        var differ = context.GetService<IMigrationsModelDiffer>();
        var steps = StepsFromFrontier(context);

        var failures = new List<string>();
        for (var i = 1; i < steps.Count; i++)
        {
            var modelDiff = differ
                .GetDifferences(steps[i - 1].Target.GetRelationalModel(), steps[i].Target.GetRelationalModel())
                .Select(Describe)
                .ToHashSet();
            var upOperations = steps[i].Migration.UpOperations
                .Where(IsSchemaOperation)
                .Select(Describe)
                .ToHashSet();
            var onlyInModel = modelDiff.Except(upOperations).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var onlyInUp = upOperations.Except(modelDiff).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (onlyInModel.Count + onlyInUp.Count > 0)
            {
                failures.Add(
                    $"{steps[i].Id}: its Designer is not {steps[i - 1].Id}'s plus its own Up. " +
                    $"Only in the model diff: [{string.Join(", ", onlyInModel)}]. " +
                    $"Only in Up: [{string.Join(", ", onlyInUp)}].");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    [Trait("Scenario", "LastDesignerMatchesSnapshot")]
    public void LastDesigner_MatchesTheSnapshot_ExceptTheUnmappedIndexerTagsColumn()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        using var context = new ListenArrDbContext(CreateOptions(connection));
        var differ = context.GetService<IMigrationsModelDiffer>();
        var initializer = context.GetService<IModelRuntimeInitializer>();
        var snapshot = Finalize(initializer, context.GetService<IMigrationsAssembly>().ModelSnapshot!.Model);
        var last = StepsFromFrontier(context)[^1];

        var differences = differ
            .GetDifferences(last.Target.GetRelationalModel(), snapshot.GetRelationalModel())
            .Select(Describe)
            .ToList();

        Assert.Equal(new[] { PhysicalOnlyDifference }, differences);
    }

    [Fact]
    [Trait("Scenario", "StepDownRemovesOnlyOwnSchema")]
    public async Task SteppingDownOneMigrationAtATime_RemovesOnlyWhatThatMigrationAdded_AndKeepsOtherData()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(CreateOptions(connection));
        var migrator = context.GetService<IMigrator>();
        var steps = StepsFromFrontier(context);

        await migrator.MigrateAsync();
        // Sentinels in columns that belong to different items, so a Down that rebuilds the table
        // from a stale column list shows up as a lost value, not only as a lost column.
        context.ApplicationSettings.Add(new ApplicationSettings
        {
            EmbedCoverArtInAudioFiles = true,
            RecycleBinPath = "sentinel-recycle-bin",
            BackupRetentionDays = 91,
        });
        await context.SaveChangesAsync();

        var schema = await ReadSchemaAsync(connection);
        for (var i = steps.Count - 1; i > 0; i--)
        {
            var step = steps[i];
            await migrator.MigrateAsync(steps[i - 1].Id);
            var after = await ReadSchemaAsync(connection);

            var up = step.Migration.UpOperations;
            var expectedLostColumns = up.OfType<AddColumnOperation>().Select(o => $"{o.Table}.{o.Name}").ToHashSet();
            var expectedLostTables = up.OfType<CreateTableOperation>().Select(o => o.Name).ToHashSet();
            var expectedRestoredTables = up.OfType<DropTableOperation>().Select(o => o.Name).ToHashSet();

            var lostTables = schema.Keys.Except(after.Keys).ToHashSet();
            var restoredTables = after.Keys.Except(schema.Keys).ToHashSet();
            var lostColumns = schema.Keys.Intersect(after.Keys)
                .SelectMany(table => schema[table].Except(after[table]).Select(column => $"{table}.{column}"))
                .ToHashSet();
            var gainedColumns = schema.Keys.Intersect(after.Keys)
                .SelectMany(table => after[table].Except(schema[table]).Select(column => $"{table}.{column}"))
                .ToList();

            Assert.True(expectedLostColumns.SetEquals(lostColumns),
                $"Down of {step.Id} removed columns [{string.Join(", ", lostColumns)}], expected [{string.Join(", ", expectedLostColumns)}]");
            Assert.True(expectedLostTables.SetEquals(lostTables),
                $"Down of {step.Id} removed tables [{string.Join(", ", lostTables)}], expected [{string.Join(", ", expectedLostTables)}]");
            Assert.True(expectedRestoredTables.SetEquals(restoredTables),
                $"Down of {step.Id} restored tables [{string.Join(", ", restoredTables)}], expected [{string.Join(", ", expectedRestoredTables)}]");
            Assert.True(gainedColumns.Count == 0,
                $"Down of {step.Id} added columns [{string.Join(", ", gainedColumns)}]");

            await AssertSentinelIfPresentAsync(connection, after, "EmbedCoverArtInAudioFiles", 1L, step.Id);
            await AssertSentinelIfPresentAsync(connection, after, "RecycleBinPath", "sentinel-recycle-bin", step.Id);
            await AssertSentinelIfPresentAsync(connection, after, "BackupRetentionDays", 91L, step.Id);
            schema = after;
        }

        Assert.Contains("Tags", schema["Indexers"]);

        await migrator.MigrateAsync();
        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static async Task AssertSentinelIfPresentAsync(
        SqliteConnection connection,
        Dictionary<string, HashSet<string>> schema,
        string column,
        object expected,
        string afterDownOf)
    {
        if (!schema["ApplicationSettings"].Contains(column))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT \"{column}\" FROM \"ApplicationSettings\"";
        var actual = await command.ExecuteScalarAsync();
        Assert.True(Equals(expected, actual),
            $"ApplicationSettings.{column} was {actual ?? "missing"} after the Down of {afterDownOf}, expected {expected}");
    }

    private static async Task<Dictionary<string, HashSet<string>>> ReadSchemaAsync(SqliteConnection connection)
    {
        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'table' " +
                "AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory'";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var schema = new Dictionary<string, HashSet<string>>();
        foreach (var table in tables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
            await using var reader = await command.ExecuteReaderAsync();
            var columns = new HashSet<string>();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }

            schema[table] = columns;
        }

        return schema;
    }
}
