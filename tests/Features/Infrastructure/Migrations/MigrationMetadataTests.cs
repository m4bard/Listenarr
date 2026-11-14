/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using System.Reflection;
using Listenarr.Infrastructure.Persistence.Migrations;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Listenarr.Tests.Features.Infrastructure.Migrations;

[Trait("Name", "MigrationMetadataTests")]
[Trait("Category", "Infrastructure")]
public class MigrationMetadataTests : BaseTests
{
    [Fact]
    public void AddWeakStorageVerifiedCleanupMigration_IsDiscoverableByEf()
    {
        AssertMigrationId<AddWeakStorageVerifiedCleanup>(
            "20260825021432_AddWeakStorageVerifiedCleanup");
    }

    [Fact]
    public void AddOperationEvidencePublications_IsDiscoverableAndConsolidated()
    {
        AssertMigrationId<AddOperationEvidencePublications>(
            "20261002210137_AddOperationEvidencePublications");
        var migration = new AddOperationEvidencePublications();
        var up = BuildOperations(migration, "Up");
        var down = BuildOperations(migration, "Down");
        Assert.Equal(6, up.Operations.Count);
        Assert.Equal(3, down.Operations.Count);
        Assert.Empty(up.Operations.OfType<SqlOperation>());
        Assert.DoesNotContain(up.Operations, operation =>
            operation is DropTableOperation or DropColumnOperation);
        var columns = up.Operations.OfType<AddColumnOperation>().ToList();
        Assert.Equal(2, columns.Count);
        Assert.All(columns, column =>
        {
            Assert.Equal("CompatibilityFilePublicationJournals", column.Table);
            Assert.True(column.IsNullable);
        });
        var table = Assert.Single(up.Operations.OfType<CreateTableOperation>());
        Assert.Equal("VerifiedFileRenameJournals", table.Name);
        Assert.Contains(table.Columns, column => column.Name == "SourceSha256");
        Assert.Contains(table.Columns, column => column.Name == "RetirementPath");
        Assert.Equal(3, up.Operations.OfType<CreateIndexOperation>().Count());
    }

    [Fact]
    public void AddImportBlacklistExtensionsMigration_IsDiscoverableByEf()
    {
        AssertMigrationId<AddImportBlacklistExtensionsToApplicationSettings>(
            "20260317123000_AddImportBlacklistExtensionsToApplicationSettings");
    }

    [Fact]
    public void AddMoveJobSourcePathHistoryRepair_IsDiscoverableByEf()
    {
        AssertMigrationId<AddMoveJobSourcePath>(
            "20251124102000_AddMoveJobSourcePath");
    }

    [Fact]
    public void AddProcessExecutionLogsHistoryRepair_IsDiscoverableAndPreservesCanaryModel()
    {
        AssertMigrationId<AddProcessExecutionLogs>(
            "20260809121006_AddProcessExecutionLogs");

        var migration = new AddProcessExecutionLogs();
        var upBuilder = BuildOperations(migration, "Up");
        var downBuilder = BuildOperations(migration, "Down");

        var create = Assert.Single(upBuilder.Operations.OfType<CreateTableOperation>());
        Assert.Equal("ProcessExecutionLogs", create.Name);
        Assert.Single(upBuilder.Operations);

        var drop = Assert.Single(downBuilder.Operations.OfType<DropTableOperation>());
        Assert.Equal("ProcessExecutionLogs", drop.Name);
        Assert.Single(downBuilder.Operations);

        var model = migration.TargetModel;
        var applicationSettings = AssertEntity(
            model,
            "Listenarr.Domain.Configuration.ApplicationSettings");
        Assert.NotNull(applicationSettings.FindProperty("Version"));

        var download = AssertEntity(model, "Listenarr.Domain.Downloads.Download");
        Assert.NotNull(download.FindProperty("ActiveAudiobookDeduplicationKey"));

        var importJob = AssertEntity(
            model,
            "Listenarr.Domain.Downloads.DownloadProcessingJob");
        Assert.NotNull(importJob.FindProperty("ActiveDeduplicationKey"));
    }

    [Fact]
    public void AddDurableFilesystemRecovery_IsDiscoverableAndConsolidated()
    {
        AssertMigrationId<AddDurableFilesystemRecovery>(
            "20260810160602_AddDurableFilesystemRecovery");

        var migration = new AddDurableFilesystemRecovery();
        var upBuilder = BuildOperations(migration, "Up");
        var downBuilder = BuildOperations(migration, "Down");

        Assert.Equal(82, upBuilder.Operations.Count);
        Assert.Equal(60, downBuilder.Operations.Count);
        Assert.Empty(upBuilder.Operations.OfType<SqlOperation>());

        var createdTables = upBuilder.Operations
            .OfType<CreateTableOperation>()
            .ToDictionary(operation => operation.Name, StringComparer.Ordinal);
        Assert.Contains("AudiobookDeletionIntents", createdTables.Keys);
        Assert.Contains("FileMutationJournals", createdTables.Keys);
        Assert.Contains("LibraryDirectoryOwnerships", createdTables.Keys);
        Assert.Contains("MoveJobEntries", createdTables.Keys);
        Assert.Contains("MoveScanHandoffs", createdTables.Keys);
        Assert.Contains("RootFolderRelocations", createdTables.Keys);
        Assert.DoesNotContain("ProcessExecutionLogs", createdTables.Keys);
        Assert.DoesNotContain("LibraryDirectoryOwnershipRetiredMarkers", createdTables.Keys);
        Assert.Contains(
            createdTables["FileMutationJournals"].Columns,
            column => column.Name == "AudiobookFileId");

        Assert.Empty(upBuilder.Operations.OfType<AddForeignKeyOperation>());

        var activeIntentIndex = Assert.Single(
            upBuilder.Operations.OfType<CreateIndexOperation>(),
            index => index.Name == "IX_AudiobookDeletionIntents_AudiobookId");
        Assert.True(activeIntentIndex.IsUnique);
        Assert.Equal("\"State\" <> 'Completed'", activeIntentIndex.Filter);

        Assert.DoesNotContain(upBuilder.Operations, operation =>
            operation is DropTableOperation or DropColumnOperation);
        Assert.Empty(downBuilder.Operations.OfType<DropForeignKeyOperation>());
    }

    [Fact]
    public void AddMoveJobRelocationForeignKey_IsDiscoverableAndIsolated()
    {
        AssertMigrationId<AddMoveJobRelocationForeignKey>(
            "20260810160640_AddMoveJobRelocationForeignKey");

        var migration = new AddMoveJobRelocationForeignKey();
        var upBuilder = BuildOperations(migration, "Up");
        var downBuilder = BuildOperations(migration, "Down");

        var add = Assert.Single(upBuilder.Operations.OfType<AddForeignKeyOperation>());
        Assert.Equal("FK_MoveJobs_RootFolderRelocations_RelocationId", add.Name);
        Assert.Equal("MoveJobs", add.Table);
        Assert.Equal("RootFolderRelocations", add.PrincipalTable);
        Assert.Equal("RelocationId", Assert.Single(add.Columns));
        Assert.Equal(ReferentialAction.Restrict, add.OnDelete);
        Assert.Single(upBuilder.Operations);

        var drop = Assert.Single(downBuilder.Operations.OfType<DropForeignKeyOperation>());
        Assert.Equal("FK_MoveJobs_RootFolderRelocations_RelocationId", drop.Name);
        Assert.Equal("MoveJobs", drop.Table);
        Assert.Single(downBuilder.Operations);
    }

    [Fact]
    public void AddFileMutationParentGenerationProofs_IsDiscoverableAndIsolated()
    {
        AssertMigrationId<AddFileMutationParentGenerationProofs>(
            "20260818132300_AddFileMutationParentGenerationProofs");

        var migration = new AddFileMutationParentGenerationProofs();
        var upBuilder = BuildOperations(migration, "Up");
        var downBuilder = BuildOperations(migration, "Down");

        var additions = upBuilder.Operations
            .OfType<AddColumnOperation>()
            .ToDictionary(operation => operation.Name, StringComparer.Ordinal);
        Assert.Equal(2, additions.Count);
        Assert.Contains("SourceParentDirectoryObjectIdentity", additions.Keys);
        Assert.Contains("DestinationParentDirectoryObjectIdentity", additions.Keys);
        Assert.All(additions.Values, operation =>
        {
            Assert.Equal("FileMutationJournals", operation.Table);
            Assert.False(operation.IsNullable);
            Assert.Equal(512, operation.MaxLength);
            Assert.Equal(string.Empty, operation.DefaultValue);
        });

        Assert.Equal(2, upBuilder.Operations.Count);
        Assert.Empty(upBuilder.Operations.OfType<AlterColumnOperation>());

        var removals = downBuilder.Operations
            .OfType<DropColumnOperation>()
            .Select(operation => operation.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "SourceParentDirectoryObjectIdentity",
                "DestinationParentDirectoryObjectIdentity"
            },
            removals);
        Assert.Equal(2, downBuilder.Operations.Count);
        Assert.Empty(downBuilder.Operations.OfType<AlterColumnOperation>());
    }

    [Fact]
    public void FinalMoveMigration_TargetModelMatchesFinalContracts()
    {
        var model = new AddFileMutationParentGenerationProofs().TargetModel;

        var moveJob = AssertEntity(model, "Listenarr.Domain.Audiobooks.MoveJob");
        Assert.Equal(0, moveJob.FindProperty("ExecutionProtocolVersion")?.GetDefaultValue());
        Assert.Equal("None", moveJob.FindProperty("FailureKind")?.GetDefaultValue());
        Assert.Equal("None", moveJob.FindProperty("Phase")?.GetDefaultValue());

        var rootFolder = AssertEntity(model, "Listenarr.Domain.Audiobooks.RootFolder");
        Assert.Equal("Auto", rootFolder.FindProperty("CaseSensitivityMode")?.GetDefaultValue());
        Assert.Equal("Unknown", rootFolder.FindProperty("ResolvedCaseSensitivity")?.GetDefaultValue());
        Assert.Equal("Unavailable", rootFolder.FindProperty("PathIdentityState")?.GetDefaultValue());

        var audiobookFile = AssertEntity(model, "Listenarr.Domain.Audiobooks.AudiobookFile");
        Assert.Equal("Auto", audiobookFile.FindProperty("PathCaseSensitivityMode")?.GetDefaultValue());
        Assert.Equal("Unknown", audiobookFile.FindProperty("PathCaseSensitivity")?.GetDefaultValue());
        Assert.Equal("Unavailable", audiobookFile.FindProperty("PathIdentityState")?.GetDefaultValue());

        var fileMutationJournal = AssertEntity(
            model,
            "Listenarr.Domain.Downloads.FileMutationJournal");
        Assert.NotNull(fileMutationJournal.FindProperty("AudiobookFileId"));
        Assert.NotNull(fileMutationJournal.FindProperty(
            "SourceParentDirectoryObjectIdentity"));
        Assert.NotNull(fileMutationJournal.FindProperty(
            "DestinationParentDirectoryObjectIdentity"));
        Assert.Equal(
            FileMutationProtocol.MarkerlessDatabaseState,
            fileMutationJournal.FindProperty("ProtocolVersion")?.GetDefaultValue());
        Assert.NotNull(model.FindEntityType(
            "Listenarr.Domain.Audiobooks.AudiobookDeletionIntent"));

        Assert.Null(model.FindEntityType(
            "Listenarr.Domain.Audiobooks.LibraryDirectoryOwnershipRetiredMarker"));
    }

    [Fact]
    public void AddAutomaticSearchTiming_IsDiscoverableAndAddsSettingsColumnsWithDefaults()
    {
        AssertMigrationId<AddAutomaticSearchTiming>(
            "20261021212418_AddAutomaticSearchTiming");

        var migration = new AddAutomaticSearchTiming();
        var up = BuildOperations(migration, "Up");
        var down = BuildOperations(migration, "Down");

        var columns = up.Operations.OfType<AddColumnOperation>().ToList();
        Assert.Equal(3, up.Operations.Count);
        Assert.Equal(3, columns.Count);
        Assert.All(columns, column =>
        {
            Assert.Equal("ApplicationSettings", column.Table);
            Assert.False(column.IsNullable);
        });
        Assert.Equal(1, columns.Single(column => column.Name == "AutomaticSearchIntervalHours").DefaultValue);
        Assert.Equal(24, columns.Single(column => column.Name == "AutomaticSearchCooldownHours").DefaultValue);
        Assert.Equal(5, columns.Single(column => column.Name == "AutomaticSearchBookDelaySeconds").DefaultValue);
        Assert.Equal(3, down.Operations.OfType<DropColumnOperation>().Count());
        Assert.Equal(3, down.Operations.Count);

        var applicationSettings = AssertEntity(
            migration.TargetModel,
            "Listenarr.Domain.Configuration.ApplicationSettings");
        Assert.NotNull(applicationSettings.FindProperty("AutomaticSearchIntervalHours"));
        Assert.NotNull(applicationSettings.FindProperty("AutomaticSearchCooldownHours"));
        Assert.NotNull(applicationSettings.FindProperty("AutomaticSearchBookDelaySeconds"));
    }

    [Fact]
    public void AddRecentReleaseDiscovery_AddsOffSettingTablesAndNamedIndexesOnly()
    {
        AssertMigrationId<AddRecentReleaseDiscovery>(
            "20261114012405_AddRecentReleaseDiscovery");

        var migration = new AddRecentReleaseDiscovery();
        var up = BuildOperations(migration, "Up").Operations;
        var down = BuildOperations(migration, "Down").Operations;

        var setting = Assert.Single(up.OfType<AddColumnOperation>());
        Assert.Equal(
            ("ApplicationSettings", "RecentReleaseDiscoveryEnabled", false, (object?)false),
            (setting.Table, setting.Name, setting.IsNullable, setting.DefaultValue));
        var tables = up.OfType<CreateTableOperation>().ToDictionary(table => table.Name);
        Assert.Equal(
            ["DiscoveredReleases", "IndexerCoverageGaps", "IndexerDiscoveryCheckpoints"],
            tables.Keys.Order());
        Assert.All(
            tables["IndexerDiscoveryCheckpoints"].Columns.Where(column => column.Name.EndsWith("ItemCount", StringComparison.Ordinal)),
            column => Assert.Equal((false, (object?)0), (column.IsNullable, column.DefaultValue)));
        AssertForeignKey(tables["IndexerDiscoveryCheckpoints"], "IndexerId", "Indexers", ReferentialAction.Cascade);
        AssertForeignKey(tables["DiscoveredReleases"], "IndexerId", "Indexers", ReferentialAction.Cascade);
        AssertForeignKey(tables["DiscoveredReleases"], "MatchedAudiobookId", "Audiobooks", ReferentialAction.SetNull);
        AssertForeignKey(tables["IndexerCoverageGaps"], "IndexerId", "Indexers", ReferentialAction.Cascade);
        var releaseColumns = tables["DiscoveredReleases"].Columns.ToDictionary(column => column.Name);
        Assert.Equal((false, 512), (releaseColumns["ReleaseId"].IsNullable, releaseColumns["ReleaseId"].MaxLength));
        Assert.Equal((false, 16), (releaseColumns["State"].IsNullable, releaseColumns["State"].MaxLength));
        Assert.Equal((true, 32), (releaseColumns["Outcome"].IsNullable, releaseColumns["Outcome"].MaxLength));

        // The unique pair is the dedup rule; the others are the handler, hand-off and gap access paths.
        var indexes = up.OfType<CreateIndexOperation>()
            .Select(index => (index.Name, index.Table, string.Join(",", index.Columns), index.IsUnique))
            .OrderBy(index => index.Name, StringComparer.Ordinal);
        Assert.Equal(
            [
                ("IX_DiscoveredReleases_IndexerId_ReleaseId", "DiscoveredReleases", "IndexerId,ReleaseId", true),
                ("IX_DiscoveredReleases_MatchedAudiobookId", "DiscoveredReleases", "MatchedAudiobookId", false),
                ("IX_DiscoveredReleases_State_FirstSeenUtc", "DiscoveredReleases", "State,FirstSeenUtc", false),
                ("IX_IndexerCoverageGaps_IndexerId_DetectedUtc", "IndexerCoverageGaps", "IndexerId,DetectedUtc", false)
            ],
            indexes);
        Assert.Equal(8, up.Count);

        // Down drops the tables, then the setting column; no data is migrated either way.
        Assert.Equal(
            tables.Keys.Order(),
            down.Take(3).Cast<DropTableOperation>().Select(table => table.Name).Order());
        Assert.Equal("RecentReleaseDiscoveryEnabled", Assert.IsType<DropColumnOperation>(down[3]).Name);
        Assert.Equal(4, down.Count);

        var model = migration.TargetModel;
        Assert.NotNull(AssertEntity(model, "Listenarr.Domain.Configuration.ApplicationSettings")
            .FindProperty("RecentReleaseDiscoveryEnabled"));
        AssertEntity(model, "Listenarr.Domain.Search.Discovery.IndexerDiscoveryCheckpoint");
        AssertEntity(model, "Listenarr.Domain.Search.Discovery.DiscoveredRelease");
        AssertEntity(model, "Listenarr.Domain.Search.Discovery.IndexerCoverageGap");
    }

    private static void AssertForeignKey(
        CreateTableOperation table,
        string column,
        string principalTable,
        ReferentialAction onDelete)
    {
        var foreignKey = Assert.Single(table.ForeignKeys, key => key.Columns.SequenceEqual([column]));
        Assert.Equal((principalTable, onDelete), (foreignKey.PrincipalTable, foreignKey.OnDelete));
    }

    private static MigrationBuilder BuildOperations(Migration migration, string methodName)
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite");
        migration.GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder;
    }

    private static IEntityType AssertEntity(IModel model, string name) =>
        Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(name));

    private static void AssertMigrationId<TMigration>(string expected)
        where TMigration : Migration
    {
        var attribute = typeof(TMigration).GetCustomAttribute<MigrationAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal(expected, attribute!.Id);
    }
}
