/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Infrastructure.DependencyInjection;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// Exercises the real SQLite migration pipeline. These tests intentionally
/// validate final migration contracts rather than intermediate PR-only schema.
/// </summary>
[Trait("Area", "Persistence")]
[Trait("Name", "SqliteMigrationSchemaTests")]
[Trait("Category", "Infrastructure")]
public class SqliteMigrationSchemaTests : BaseTests
{
    // Migrations this branch adds, declared apart from the consolidated list below and
    // asserted apart from it. Two branches that each add a migration would otherwise rewrite
    // the same two lines of this file and conflict on merge in either order.
    private const string EmbedCoverArtSettingMigrationId =
        "20260828190320_AddEmbedCoverArtInAudioFilesSetting";

    private const string AuthorIdentityRepairMigrationId =
        "20260921202317_AddAuthorIdentityRepair";

    private const string AuthorIdentityRecheckWindowMigrationId =
        "20260921210504_AddAuthorIdentityRecheckWindow";

    private static readonly string[] BranchMigrationIds =
    [
        EmbedCoverArtSettingMigrationId,
        AuthorIdentityRepairMigrationId,
        AuthorIdentityRecheckWindowMigrationId
    ];

    private const string CanaryMigrationFrontierId =
        "20260621002226_AddApplicationSettingsConcurrency";
    private const string MoveJobSourcePathRepairId =
        "20251124102000_AddMoveJobSourcePath";
    private const string ProcessExecutionLogRepairId =
        "20260809121006_AddProcessExecutionLogs";
    private const string ConsolidatedMigrationId =
        "20260810160602_AddDurableFilesystemRecovery";
    private const string MoveJobRelocationForeignKeyMigrationId =
        "20260810160640_AddMoveJobRelocationForeignKey";
    private const string FileMutationParentGenerationProofsMigrationId =
        "20260818132300_AddFileMutationParentGenerationProofs";
    private const string CompatibilityFilePublicationMigrationId =
        "20260821141235_AddCompatibilityFilePublication";
    private const string WeakStorageVerifiedCleanupMigrationId =
        "20260825021432_AddWeakStorageVerifiedCleanup";
    private const string ReleaseBlocklistMigrationId =
        "20260828191810_AddReleaseBlocklist";
    private const string HistoryProtocolMigrationId =
        "20260911172407_AddHistoryProtocol";
    private const string IndexerFailureBackoffMigrationId =
        "20260914152223_AddIndexerFailureBackoff";
    private const string PreferredReleaseShapeMigrationId =
        "20260914153043_AddPreferredReleaseShapeToQualityProfile";
    private const string HistoryReleaseMetadataMigrationId =
        "20260914171829_AddHistoryReleaseMetadata";
    private const string CustomScriptNotificationsMigrationId =
        "20260916112317_AddCustomScriptNotifications";
    private const string QualityProfileUpgradeAllowedMigrationId =
        "20260920025621_AddQualityProfileUpgradeAllowed";
    private const string HousekeepingRetentionMigrationId =
        "20260922220833_AddHousekeepingRetention";

    private static (SqliteConnection Connection, ListenArrDbContext Context)
        CreateMigratedSqliteContext()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var context = new ListenArrDbContext(CreateOptions(connection));
        context.Database.Migrate();
        return (connection, context);
    }

    private static DbContextOptions<ListenArrDbContext> CreateOptions(
        SqliteConnection connection) =>
        new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name))
            .Options;

    [Fact]
    [Trait("Scenario", "EveryModelColumnExistsAfterMigrate")]
    public void EveryMappedColumn_ExistsInMigratedSqliteSchema()
    {
        var (connection, context) = CreateMigratedSqliteContext();
        using var _conn = connection;
        using var _ctx = context;
        var failures = new List<string>();

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (string.IsNullOrEmpty(tableName))
            {
                continue;
            }

            var storeObject = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(
                tableName,
                entityType.GetSchema());
            var columns = entityType.GetProperties()
                .Select(property => property.GetColumnName(storeObject))
                .Where(column => !string.IsNullOrEmpty(column))
                .Distinct()
                .ToList();
            if (columns.Count == 0)
            {
                continue;
            }

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {string.Join(", ", columns.Select(column => $"\"{column}\""))} FROM \"{tableName}\" LIMIT 0";
            try
            {
                using var reader = command.ExecuteReader();
            }
            catch (SqliteException exception)
            {
                failures.Add($"{tableName}: {exception.Message}");
            }
        }

        Assert.True(
            failures.Count == 0,
            "The EF model maps columns absent from the migrated SQLite schema:\n"
            + string.Join("\n", failures));
    }

    [Fact]
    [Trait("Scenario", "PullRequestMigrationsHaveNoNonTransactionalOperationWarnings")]
    public async Task PullRequestMigrations_AfterCanaryFrontier_HaveNoNonTransactionalOperationWarnings()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using (var baseline = new ListenArrDbContext(CreateOptions(connection)))
        {
            await baseline.GetService<IMigrator>().MigrateAsync(CanaryMigrationFrontierId);
        }

        var guardedOptions = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name))
            .ConfigureWarnings(warnings => warnings.Throw(
                RelationalEventId.NonTransactionalMigrationOperationWarning))
            .Options;
        await using var guarded = new ListenArrDbContext(guardedOptions);

        await guarded.Database.MigrateAsync();
    }

    [Fact]
    [Trait("Scenario", "MigrationHistoryMatchesModel")]
    public async Task MigrationHistory_HasNoPendingModelChanges()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(CreateOptions(connection));

        await context.Database.MigrateAsync();

        Assert.False(
            context.Database.HasPendingModelChanges(),
            "The configured EF model differs from the final migration snapshot.");
    }

    [Fact]
    [Trait("Scenario", "MoveSourceCleanupPolicySnapshot")]
    public async Task WeakStorageMigration_AddsFailClosedMovePolicySnapshot()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(CreateOptions(connection));

        await context.Database.MigrateAsync();

        Assert.True(await ColumnExistsAsync(connection, "MoveJobs", "SourceCleanupMode"));
        Assert.True(await ColumnExistsAsync(connection, "MoveJobs", "ForceCopyAndRetainSource"));
        Assert.True(await ColumnExistsAsync(connection, "MoveJobs", "SourceRootFolderId"));
        Assert.True(await ColumnExistsAsync(connection, "MoveJobs", "SourcePolicyRevision"));
        Assert.True(await ColumnExistsAsync(connection, "MoveJobs", "SourceStorageContractRevision"));
        Assert.True(await ColumnExistsAsync(connection, "MoveJobs", "TargetRootFolderId"));
        Assert.True(await ColumnExistsAsync(connection, "MoveJobs", "TargetPolicyRevision"));
        Assert.True(await ColumnExistsAsync(connection, "MoveJobs", "TargetStorageContractRevision"));
        Assert.True(await ColumnExistsAsync(connection, "RootFolders", "StorageContractRevision"));
        Assert.True(await ColumnExistsAsync(
            connection,
            "CompatibilityFilePublicationJournals",
            "SourceStorageContractRevision"));
        Assert.True(await ColumnExistsAsync(
            connection,
            "CompatibilityFilePublicationJournals",
            "DestinationStorageContractRevision"));
        Assert.Equal(
            "'RetainSource'",
            await ColumnDefaultAsync(connection, "MoveJobs", "SourceCleanupMode"));
    }

    // The protocol column is what makes a recorded protocol survive the write. Without it the
    // value is built in DownloadHistoryService and then dropped by the mapping, so a test of the
    // construction alone would pass while nothing reached the database.
    [Fact]
    [Trait("Scenario", "HistoryProtocolColumn")]
    public async Task HistoryProtocolMigration_AddsTheNullableProtocolColumn()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(CreateOptions(connection));

        await context.Database.MigrateAsync();

        Assert.True(await ColumnExistsAsync(connection, "History", "Protocol"));
    }

    // Indexer, quality and size are in scope at the one place a grab is recorded and were
    // discarded there. The columns are the half that has to exist before the call site can stop
    // throwing them away, and a test of the call site alone would pass with nowhere to store them.
    [Fact]
    [Trait("Scenario", "HistoryReleaseMetadataColumns")]
    public async Task HistoryReleaseMetadataMigration_AddsTheNullableReleaseColumns()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(CreateOptions(connection));

        await context.Database.MigrateAsync();

        Assert.True(await ColumnExistsAsync(connection, "History", "Indexer"));
        Assert.True(await ColumnExistsAsync(connection, "History", "Quality"));
        Assert.True(await ColumnExistsAsync(connection, "History", "Size"));
    }

    // The columns have to survive a real SQLite round trip, not just exist. An in-memory
    // provider would accept a write to a column the migration never created.
    [Fact]
    [Trait("Scenario", "HistoryReleaseMetadataRoundTrip")]
    public async Task HistoryReleaseMetadata_SurvivesAWriteAndAReadBack()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(CreateOptions(connection));

        await context.Database.MigrateAsync();

        context.History.Add(new History
        {
            EventType = HistoryEvents.Grabbed,
            CorrelationId = "round-trip",
            Indexer = "Example Indexer",
            Quality = "M4B 128kbps",
            Size = 734003200L
        });
        await context.SaveChangesAsync();

        await using var reader = new ListenArrDbContext(CreateOptions(connection));
        var stored = Assert.Single(await reader.History.AsNoTracking().ToListAsync());
        Assert.Equal("Example Indexer", stored.Indexer);
        Assert.Equal("M4B 128kbps", stored.Quality);
        Assert.Equal(734003200L, stored.Size);
    }

    // A grab that knew none of the three leaves them null rather than zero or empty, so an
    // unreported size does not read back as an empty release.
    [Fact]
    [Trait("Scenario", "HistoryReleaseMetadataStaysNull")]
    public async Task HistoryReleaseMetadata_IsNullForAnEventWithNoReleaseBehindIt()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(CreateOptions(connection));

        await context.Database.MigrateAsync();

        context.History.Add(new History
        {
            EventType = "Added",
            CorrelationId = "library-row"
        });
        await context.SaveChangesAsync();

        await using var reader = new ListenArrDbContext(CreateOptions(connection));
        var stored = Assert.Single(await reader.History.AsNoTracking().ToListAsync());
        Assert.Null(stored.Indexer);
        Assert.Null(stored.Quality);
        Assert.Null(stored.Size);
    }

    [Fact]
    [Trait("Scenario", "IndexerFailureBackoffColumns")]
    public async Task IndexerFailureBackoffMigration_AddsPerIndexerBackoffColumns()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(CreateOptions(connection));

        await context.Database.MigrateAsync();

        Assert.True(await ColumnExistsAsync(connection, "Indexers", "InitialFailure"));
        Assert.True(await ColumnExistsAsync(connection, "Indexers", "MostRecentFailure"));
        Assert.True(await ColumnExistsAsync(connection, "Indexers", "EscalationLevel"));
        Assert.True(await ColumnExistsAsync(connection, "Indexers", "DisabledTill"));
        Assert.True(await ColumnExistsAsync(connection, "Indexers", "LastFailureReason"));

        // An existing install's indexers have to come up healthy, not blocked.
        Assert.Equal("0", await ColumnDefaultAsync(connection, "Indexers", "EscalationLevel"));
    }

    [Fact]
    [Trait("Scenario", "CustomScriptNotificationStorage")]
    public async Task CustomScriptMigration_AddsTheCustomScriptsColumn()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(CreateOptions(connection));

        await context.Database.MigrateAsync();

        Assert.True(await ColumnExistsAsync(connection, "ApplicationSettings", "CustomScripts"));
    }

    [Fact]
    [Trait("Scenario", "FinalMigrationHistoryIsConsolidated")]
    public async Task MigrationHistory_ContainsOnlyRetainedRepairsAndConsolidatedPrMigrationAfterCanary()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(CreateOptions(connection));

        await context.Database.MigrateAsync();
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
        var allPostCanary = applied
            .Where(id => string.CompareOrdinal(id, CanaryMigrationFrontierId) > 0)
            .ToArray();
        Assert.Equal(
            BranchMigrationIds,
            allPostCanary.Where(id => BranchMigrationIds.Contains(id, StringComparer.Ordinal)));
        Assert.All(
            BranchMigrationIds,
            id => Assert.True(
                string.CompareOrdinal(id, WeakStorageVerifiedCleanupMigrationId) > 0,
                "A migration this branch adds has to sort after the consolidated history."));
        var postCanary = allPostCanary
            .Where(id => !BranchMigrationIds.Contains(id, StringComparer.Ordinal))
            .ToArray();

        // Pinned on their own rather than appended to the list below. That list is
        // ordered and every branch that adds a migration has to extend its last line, so
        // two of them in flight at once is a conflict in a file neither branch is about.
        // Taking this branch's own out first leaves the check below exactly as strict:
        // anything else unpinned still fails it.
        string[] metadataRefreshMigrationIds =
        [
            "20260910120000_AddAudiobookLastMetadataRefreshAt",
            "20260910120500_AddMetadataRefreshSettings",
            "20260910121000_AddAudiobookLastMetadataRefreshAtIndex"
        ];
        Assert.All(metadataRefreshMigrationIds, id => Assert.Contains(id, postCanary));
        postCanary = [.. postCanary.Except(metadataRefreshMigrationIds)];

        Assert.Equal(
            [
                ProcessExecutionLogRepairId,
                ConsolidatedMigrationId,
                MoveJobRelocationForeignKeyMigrationId,
                FileMutationParentGenerationProofsMigrationId,
                CompatibilityFilePublicationMigrationId,
                WeakStorageVerifiedCleanupMigrationId,
                ReleaseBlocklistMigrationId,
                HistoryProtocolMigrationId,
                IndexerFailureBackoffMigrationId,
                PreferredReleaseShapeMigrationId,
                HistoryReleaseMetadataMigrationId,
                CustomScriptNotificationsMigrationId,
                QualityProfileUpgradeAllowedMigrationId,
                HousekeepingRetentionMigrationId
            ],
            postCanary);
        Assert.Contains("20251124102000_AddMoveJobSourcePath", applied);
    }

    [Fact]
    [Trait("Scenario", "ExactCanaryUpgrade")]
    public async Task ExactCanarySchema_UpgradesAndFencesReleasedActiveMoveJobs()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using (var canary = new ListenArrDbContext(CreateOptions(connection)))
        {
            await canary.GetService<IMigrator>().MigrateAsync(CanaryMigrationFrontierId);
        }

        // Exact canary did not discover AddMoveJobSourcePath because it shipped
        // without migration metadata. Recreate that released schema/history gap.
        await ExecuteNonQueryAsync(
            connection,
            $"""
            ALTER TABLE "MoveJobs" DROP COLUMN "SourcePath";
            DELETE FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = '{MoveJobSourcePathRepairId}';
            """);
        Assert.False(await ColumnExistsAsync(connection, "MoveJobs", "SourcePath"));
        Assert.False(await TableExistsAsync(connection, "ProcessExecutionLogs"));

        var queuedId = Guid.NewGuid();
        var processingId = Guid.NewGuid();
        var completedId = Guid.NewGuid();
        var failedId = Guid.NewGuid();
        await InsertCanaryMoveJobAsync(connection, queuedId, 1001, "Queued", "1001:queued");
        await InsertCanaryMoveJobAsync(connection, processingId, 1002, "Processing", "1002:processing");
        await InsertCanaryMoveJobAsync(connection, completedId, 1003, "Completed", null);
        await InsertCanaryMoveJobAsync(connection, failedId, 1004, "Failed", null);

        var services = new ServiceCollection();
        services.AddDbContextFactory<ListenArrDbContext>(options =>
            options.UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name)));
        await using var provider = services.BuildServiceProvider();
        provider.ApplyListenarrDatabaseMigrations();
        var factory = provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
        await using var upgraded = await factory.CreateDbContextAsync();

        Assert.True(await ColumnExistsAsync(connection, "MoveJobs", "SourcePath"));
        Assert.True(await TableExistsAsync(connection, "ProcessExecutionLogs"));
        Assert.True(await TableExistsAsync(connection, "AudiobookDeletionIntents"));
        Assert.True(await ColumnExistsAsync(connection, "FileMutationJournals", "AudiobookFileId"));
        Assert.Equal(
            ("NeedsAttention", "Verification", 0, null),
            await ReadMoveJobUpgradeStateAsync(connection, queuedId));
        Assert.Equal(
            ("NeedsAttention", "Verification", 0, null),
            await ReadMoveJobUpgradeStateAsync(connection, processingId));
        Assert.Equal(
            ("Completed", "None", 0, (string?)null),
            await ReadMoveJobUpgradeStateAsync(connection, completedId));
        Assert.Equal(
            ("Failed", "None", 0, (string?)null),
            await ReadMoveJobUpgradeStateAsync(connection, failedId));

        var materialized = await upgraded.MoveJobs
            .OrderBy(job => job.AudiobookId)
            .ToListAsync();
        Assert.Equal(4, materialized.Count);
        Assert.False(upgraded.Database.HasPendingModelChanges());
    }

    [Fact]
    [Trait("Scenario", "ConsolidatedMigrationDowngradeReapply")]
    public async Task ConsolidatedMigration_DowngradesOneStepAndReappliesCleanly()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(CreateOptions(connection));
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync();
        Assert.True(await TableExistsAsync(connection, "FileMutationJournals"));
        Assert.True(await ColumnExistsAsync(connection, "MoveJobs", "ExecutionProtocolVersion"));
        Assert.True(await TableExistsAsync(connection, "AudiobookDeletionIntents"));
        Assert.True(await ColumnExistsAsync(connection, "FileMutationJournals", "AudiobookFileId"));

        await migrator.MigrateAsync(ConsolidatedMigrationId);
        Assert.True(await TableExistsAsync(connection, "AudiobookDeletionIntents"));
        Assert.True(await TableExistsAsync(connection, "FileMutationJournals"));
        Assert.True(await ColumnExistsAsync(connection, "FileMutationJournals", "AudiobookFileId"));
        Assert.True(await ColumnExistsAsync(connection, "MoveJobs", "ExecutionProtocolVersion"));
        Assert.False(await ForeignKeyHasDeleteActionAsync(
            connection,
            "MoveJobs",
            "RootFolderRelocations",
            "RelocationId",
            "RESTRICT"));

        await migrator.MigrateAsync(ProcessExecutionLogRepairId);
        Assert.False(await TableExistsAsync(connection, "AudiobookDeletionIntents"));
        Assert.False(await TableExistsAsync(connection, "FileMutationJournals"));
        Assert.False(await ColumnExistsAsync(connection, "MoveJobs", "ExecutionProtocolVersion"));
        Assert.True(await ColumnExistsAsync(connection, "MoveJobs", "SourcePath"));
        Assert.True(await TableExistsAsync(connection, "ProcessExecutionLogs"));

        await migrator.MigrateAsync();
        Assert.True(await TableExistsAsync(connection, "FileMutationJournals"));
        Assert.True(await ColumnExistsAsync(connection, "MoveJobs", "ExecutionProtocolVersion"));
        Assert.True(await TableExistsAsync(connection, "AudiobookDeletionIntents"));
        Assert.True(await ColumnExistsAsync(connection, "FileMutationJournals", "AudiobookFileId"));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    [Trait("Scenario", "PathIdentityDefaultSentinels")]
    public async Task ExplicitValidPathIdentity_IsNotReplacedByUpgradeDefaultsOnInsert()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(CreateOptions(connection));
        await context.Database.MigrateAsync();

        var rootPath = Path.Join(Path.GetTempPath(), $"sentinel-root-{Guid.NewGuid():N}");
        var semantics = FileSystemPathSemantics.CurrentHostDefault;
        var root = new RootFolder
        {
            Name = "Sentinel Root",
            Path = rootPath,
            CaseSensitivityMode = FileSystemCaseSensitivityMode.Auto,
            ResolvedCaseSensitivity = semantics.CaseSensitivity,
            PathIdentityKey = $"sentinel-root-{Guid.NewGuid():N}",
            PathIdentityState = PathIdentityState.Valid
        };
        var audiobook = new Audiobook
        {
            Title = "Sentinel Audiobook",
            BasePath = Path.Join(rootPath, "Author", "Title")
        };
        context.RootFolders.Add(root);
        context.Audiobooks.Add(audiobook);
        await context.SaveChangesAsync();

        var filePath = Path.Join(audiobook.BasePath!, "book.m4b");
        var trackedFile = AudiobookFile.CreateUnresolved(filePath);
        trackedFile.AudiobookId = audiobook.Id;
        trackedFile.ApplyPathIdentity(
            filePath,
            AudiobookFilePathIdentity.CreateValid(
                filePath,
                semantics,
                FileSystemCaseSensitivityMode.Auto,
                rootPath));
        context.AudiobookFiles.Add(trackedFile);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var persistedRoot = await context.RootFolders.SingleAsync();
        var persistedFile = await context.AudiobookFiles.SingleAsync();
        Assert.Equal(PathIdentityState.Valid, persistedRoot.PathIdentityState);
        Assert.Equal(PathIdentityState.Valid, persistedFile.PathIdentityState);
        Assert.Equal(semantics.CaseSensitivity, persistedRoot.ResolvedCaseSensitivity);
        Assert.Equal(semantics.CaseSensitivity, persistedFile.PathCaseSensitivity);
    }

    [Fact]
    [Trait("Scenario", "FinalSchemaContracts")]
    public async Task FinalSchema_HasDurableDefaultsIndexesAndSetNullOwnershipRootForeignKey()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(CreateOptions(connection));
        await context.Database.MigrateAsync();

        Assert.Equal("0", await ColumnDefaultAsync(connection, "MoveJobs", "ExecutionProtocolVersion"));
        Assert.Equal("'None'", await ColumnDefaultAsync(connection, "MoveJobs", "FailureKind"));
        Assert.Equal("'Auto'", await ColumnDefaultAsync(connection, "RootFolders", "CaseSensitivityMode"));
        Assert.Equal("'Unknown'", await ColumnDefaultAsync(connection, "RootFolders", "ResolvedCaseSensitivity"));
        Assert.Equal("'Unavailable'", await ColumnDefaultAsync(connection, "RootFolders", "PathIdentityState"));
        Assert.Equal("'Auto'", await ColumnDefaultAsync(connection, "AudiobookFiles", "PathCaseSensitivityMode"));
        Assert.Equal("'Unknown'", await ColumnDefaultAsync(connection, "AudiobookFiles", "PathCaseSensitivity"));
        Assert.Equal("'Unavailable'", await ColumnDefaultAsync(connection, "AudiobookFiles", "PathIdentityState"));

        Assert.True(await IndexExistsAsync(connection, "IX_RootFolders_SingleDefault"));
        Assert.True(await IndexExistsAsync(connection, "IX_AudiobookFiles_PathOwnershipKey"));
        Assert.True(await IndexExistsAsync(connection, "IX_LibraryDirectoryOwnerships_PathOwnershipKey"));

        // The refresh queue orders by this column and takes the head of it on every cycle and
        // every API trigger. Unindexed that is a full scan and a sort, which is exactly the
        // shape LastSearchTime next to it has always been indexed for.
        Assert.True(await IndexExistsAsync(connection, "IX_Audiobooks_LastMetadataRefreshAt"));
        Assert.True(await IndexExistsAsync(connection, "IX_Audiobooks_LastSearchTime"));

        // On, for new installs and upgrades alike. What makes that safe on an upgrade is the
        // startup backfill of LastMetadataRefreshAt, not a default of off: an untouched null
        // reads as never refreshed, and a library of those is due all at once with nothing to
        // order it by. AudiobookRepository_MetadataRefreshQueryTests pins the backfill.
        Assert.Equal("1", await ColumnDefaultAsync(connection, "ApplicationSettings", "MetadataRefreshEnabled"));
        Assert.Equal("24", await ColumnDefaultAsync(connection, "ApplicationSettings", "MetadataRefreshIntervalHours"));
        Assert.Equal("30", await ColumnDefaultAsync(connection, "ApplicationSettings", "MetadataRefreshStaleAfterDays"));
        Assert.Equal("60", await ColumnDefaultAsync(connection, "ApplicationSettings", "MetadataRefreshRequestsPerHour"));
        Assert.Equal("1000", await ColumnDefaultAsync(connection, "ApplicationSettings", "MetadataRefreshMinimumSpacingMs"));

        // Off, and previewing. The scaffolder writes a column default from the CLR default
        // rather than from the property initializer, so the dry-run switch was generated as 0
        // and had to be corrected by hand; an upgraded database landing on 0 here would rewrite
        // author identities on its first enabled cycle with nothing shown first. That is the
        // whole reason this assertion exists, and it is why it asserts 1 rather than "whatever
        // the entity says".
        Assert.Equal("0", await ColumnDefaultAsync(connection, "ApplicationSettings", "AuthorIdentityRepairEnabled"));
        Assert.Equal("1", await ColumnDefaultAsync(connection, "ApplicationSettings", "AuthorIdentityRepairDryRun"));
        Assert.Equal("24", await ColumnDefaultAsync(connection, "ApplicationSettings", "AuthorIdentityRepairIntervalHours"));
        Assert.Equal("25", await ColumnDefaultAsync(connection, "ApplicationSettings", "AuthorIdentityRepairMaxRowsPerRun"));
        Assert.Equal("30", await ColumnDefaultAsync(connection, "ApplicationSettings", "AuthorIdentityRepairRecheckAfterDays"));
        Assert.True(await ForeignKeyHasDeleteActionAsync(
            connection,
            "LibraryDirectoryOwnerships",
            "RootFolders",
            "ManagedRootFolderId",
            "SET NULL"));
        Assert.True(await ForeignKeyHasDeleteActionAsync(
            connection,
            "MoveJobs",
            "RootFolderRelocations",
            "RelocationId",
            "RESTRICT"));
    }

    [Fact]
    [Trait("Scenario", "MoveJobsSourcePathRepair")]
    public async Task MoveJobs_SourcePathColumn_ExistsAfterMigrate()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(CreateOptions(connection));

        await context.Database.MigrateAsync();

        Assert.True(await ColumnExistsAsync(connection, "MoveJobs", "SourcePath"));
    }

    /// <summary>
    /// The one test in this branch that catches the scaffolder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EF writes a bool column default from the CLR default rather than from the property
    /// initializer, so <c>HousekeepingDryRun</c> scaffolded as <c>false</c> even though the
    /// entity declares it <c>true</c>. Every upgraded database would then have arrived at its
    /// first sweep in the deleting state, and no test that reads the entity could have seen it,
    /// because the entity was never wrong. So this reads the migrated SQLite schema.
    /// </para>
    /// <para>
    /// The second assertion is the control that makes the first one evidence. A helper that had
    /// quietly stopped reading SQLite, or one that returned a single constant, would still pass
    /// an isolated "the default is true". The two columns are read by the same helper on the
    /// same migrated connection and must come back with different values, so a reader that is
    /// not reading the schema cannot satisfy both.
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Scenario", "HousekeepingDryRunDefaultsOnInAMigratedDatabase")]
    public async Task HousekeepingMigration_DefaultsTheDryRunColumnOn()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(CreateOptions(connection));

        await context.Database.MigrateAsync();

        Assert.Equal("1", await ColumnDefaultAsync(connection, "ApplicationSettings", "HousekeepingDryRun"));
        Assert.Equal("30", await ColumnDefaultAsync(connection, "ApplicationSettings", "HousekeepingRetentionDays"));
    }

    /// <summary>
    /// A row already in the table when the migration runs takes the column defaults, which is
    /// the case the default exists for. An install upgrading into this migration has exactly one
    /// ApplicationSettings row and it was written before either column existed.
    /// </summary>
    [Fact]
    [Trait("Scenario", "HousekeepingDefaultsReachAnExistingSettingsRow")]
    public async Task HousekeepingMigration_LeavesAnExistingSettingsRowPreviewingAtThirtyDays()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using (var beforeHousekeeping = new ListenArrDbContext(CreateOptions(connection)))
        {
            await beforeHousekeeping.GetService<IMigrator>().MigrateAsync(WeakStorageVerifiedCleanupMigrationId);
        }

        Assert.False(await ColumnExistsAsync(connection, "ApplicationSettings", "HousekeepingDryRun"));
        await InsertRowWithColumnDefaultsAsync(connection, "ApplicationSettings", 1);

        await using (var upgraded = new ListenArrDbContext(CreateOptions(connection)))
        {
            await upgraded.Database.MigrateAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "HousekeepingDryRun", "HousekeepingRetentionDays"
            FROM "ApplicationSettings" WHERE "Id" = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.Equal(30, reader.GetInt32(1));
    }

    /// <summary>
    /// Inserts one row into <paramref name="table" /> using each column's own default, supplying
    /// a placeholder only where the schema demands a value and offers none. It exists so a test
    /// can write a row at an older migration without listing whatever the not-null columns
    /// happened to be at that point in history.
    /// </summary>
    private static async Task InsertRowWithColumnDefaultsAsync(
        SqliteConnection connection,
        string table,
        int id)
    {
        var columns = new List<(string Name, string Type)>();
        await using (var inspect = connection.CreateCommand())
        {
            inspect.CommandText =
                $"SELECT name, type, \"notnull\", dflt_value FROM pragma_table_info('{table}')";
            await using var reader = await inspect.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(0);
                var required = reader.GetInt32(2) == 1;
                var hasDefault = !reader.IsDBNull(3);
                if (name != "Id" && required && !hasDefault)
                {
                    columns.Add((name, reader.GetString(1)));
                }
            }
        }

        var names = string.Join(", ", columns.Select(column => $"\"{column.Name}\"").Prepend("\"Id\""));
        var placeholders = string.Join(", ", columns.Select((_, index) => $"$p{index}").Prepend("$id"));
        await using var insert = connection.CreateCommand();
        insert.CommandText = $"INSERT INTO \"{table}\" ({names}) VALUES ({placeholders});";
        insert.Parameters.AddWithValue("$id", id);
        for (var index = 0; index < columns.Count; index++)
        {
            var type = columns[index].Type.ToUpperInvariant();
            object value = type is "INTEGER" or "REAL" or "NUMERIC" ? 0 : string.Empty;
            insert.Parameters.AddWithValue($"$p{index}", value);
        }

        await insert.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// The startup backfill that stops the move job sweep being a no-op on every existing
    /// install. A row that was already terminal when the upgrade ran has no CompletedAt, and
    /// without a value it can never match a retention predicate.
    /// </summary>
    /// <remarks>
    /// Three arms, and the second and third are the controls. A Completed row with no timestamp
    /// is stamped from UpdatedAt. A Superseded row whose UpdatedAt is null, which is the case the
    /// column exists for, falls back to EnqueuedAt rather than staying null. And an active row is
    /// left alone, so the repair is bounded by status rather than stamping the whole table.
    /// </remarks>
    [Fact]
    [Trait("Scenario", "TerminalMoveJobsGetATerminalTimestampAtStartup")]
    public async Task StartupRepair_StampsTerminalMoveJobsThatPredateTheColumn()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(CreateOptions(connection));
        await context.Database.MigrateAsync();

        var completed = Guid.NewGuid();
        var superseded = Guid.NewGuid();
        var running = Guid.NewGuid();
        var enqueuedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var updatedAt = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc);
        await InsertTerminalMoveJobAsync(connection, completed, "Completed", enqueuedAt, updatedAt);
        await InsertTerminalMoveJobAsync(connection, superseded, "Superseded", enqueuedAt, null);
        await InsertTerminalMoveJobAsync(connection, running, "Running", enqueuedAt, updatedAt);

        var repaired = ListenarrDatabaseMigrationPreflight.RepairPostMigrationData(context);

        Assert.Equal(2, repaired.MoveJobTerminalTimestampsBackfilled);
        Assert.Equal(updatedAt, await ReadCompletedAtAsync(connection, completed));
        Assert.Equal(enqueuedAt, await ReadCompletedAtAsync(connection, superseded));
        Assert.Null(await ReadCompletedAtAsync(connection, running));

        // Idempotent: it runs on every start, and a second pass must find nothing left to do.
        Assert.Equal(0, ListenarrDatabaseMigrationPreflight.RepairPostMigrationData(context)
            .MoveJobTerminalTimestampsBackfilled);
    }

    private static async Task InsertTerminalMoveJobAsync(
        SqliteConnection connection,
        Guid id,
        string status,
        DateTime enqueuedAt,
        DateTime? updatedAt)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "MoveJobs"
                ("Id", "AudiobookId", "RequestedPath", "EnqueuedAt", "Status", "Phase",
                 "ExecutionProtocolVersion", "SourceDirectoryCleanupState", "FailureKind",
                 "AttemptCount", "UpdatedAt", "CompletedAt", "IdentityKeyVersion",
                 "LeaseGeneration", "DeleteEmptySource", "SourceCleanupMode",
                 "ForceCopyAndRetainSource")
            VALUES
                ($id, 1, 'requested', $enqueuedAt, $status, 'None', 1, 'Pending', 'None',
                 0, $updatedAt, NULL, 1, 0, 1, 'RetainSource', 0);
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$enqueuedAt", enqueuedAt);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue(
            "$updatedAt",
            updatedAt is null ? DBNull.Value : updatedAt.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<DateTime?> ReadCompletedAtAsync(
        SqliteConnection connection,
        Guid id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "CompletedAt" FROM "MoveJobs" WHERE "Id" = $id;""";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return reader.IsDBNull(0) ? null : reader.GetDateTime(0);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertCanaryMoveJobAsync(
        SqliteConnection connection,
        Guid id,
        int audiobookId,
        string status,
        string? activeDeduplicationKey)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "MoveJobs"
                ("Id", "AudiobookId", "RequestedPath", "EnqueuedAt", "Status",
                 "Error", "AttemptCount", "UpdatedAt", "ActiveDeduplicationKey")
            VALUES
                ($id, $audiobookId, $requestedPath, CURRENT_TIMESTAMP, $status,
                 NULL, 0, CURRENT_TIMESTAMP, $activeDeduplicationKey);
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$audiobookId", audiobookId);
        command.Parameters.AddWithValue("$requestedPath", $"/library/{audiobookId}");
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue(
            "$activeDeduplicationKey",
            activeDeduplicationKey is null ? DBNull.Value : activeDeduplicationKey);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(string Status, string FailureKind, int Protocol, string? ActiveDeduplicationKey)>
        ReadMoveJobUpgradeStateAsync(
            SqliteConnection connection,
            Guid id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "Status", "FailureKind", "ExecutionProtocolVersion", "ActiveDeduplicationKey"
            FROM "MoveJobs"
            WHERE "Id" = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection,
        string table,
        string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name=$name";
        command.Parameters.AddWithValue("$name", column);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<string?> ColumnDefaultAsync(
        SqliteConnection connection,
        string table,
        string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT dflt_value FROM pragma_table_info('{table}') WHERE name=$name";
        command.Parameters.AddWithValue("$name", column);
        return Convert.ToString(await command.ExecuteScalarAsync());
    }

    private static async Task<bool> IndexExistsAsync(
        SqliteConnection connection,
        string index)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name=$name";
        command.Parameters.AddWithValue("$name", index);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<bool> ForeignKeyHasDeleteActionAsync(
        SqliteConnection connection,
        string table,
        string principalTable,
        string fromColumn,
        string deleteAction)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM pragma_foreign_key_list('{table}') WHERE \"table\"=$principal AND \"from\"=$column AND on_delete=$delete";
        command.Parameters.AddWithValue("$principal", principalTable);
        command.Parameters.AddWithValue("$column", fromColumn);
        command.Parameters.AddWithValue("$delete", deleteAction);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }
}
