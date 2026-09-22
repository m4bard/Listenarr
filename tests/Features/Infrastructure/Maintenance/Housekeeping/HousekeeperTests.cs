/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Tests.Common;
using Listenarr.Tests.Features.Infrastructure.HostedServices.Scheduling;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Maintenance.Housekeeping;

/// <summary>
/// The housekeepers against a real migrated SQLite database rather than the in-memory provider,
/// because two of these predicates are the sort a provider can quietly disagree about: one has a
/// correlated subquery over another table, and all of them compare a stored DateTime.
/// </summary>
/// <remarks>
/// Every deletion test here is paired with the row that must survive it. Without that pair, a
/// housekeeper that simply emptied its table would pass every other assertion in this file.
/// </remarks>
[Trait("Area", "Housekeeping")]
[Trait("Name", "HousekeeperTests")]
[Trait("Category", "Persistence")]
public sealed class HousekeeperTests : BaseTests, IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ListenArrDbContext> _options;
    private readonly TestDbContextFactory _factory;

    public HousekeeperTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(_connection, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name))
            .Options;
        using var migrator = new ListenArrDbContext(_options);
        migrator.Database.Migrate();
        _factory = new TestDbContextFactory(_options);
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AuthorCache_DeletesRowsOutsideTheWindowAndKeepsRowsInsideIt()
    {
        await SeedAsync(context =>
        {
            context.AuthorCacheEntries.Add(Author("stale", Now.AddDays(-40)));
            context.AuthorCacheEntries.Add(Author("fresh", Now.AddDays(-20)));
        });

        var outcome = await new AuthorCacheHousekeeper(_factory).RunAsync(Cycle(retentionDays: 30), default);

        Assert.Equal(1, outcome.Matched);
        Assert.Equal(1, outcome.Deleted);
        Assert.False(outcome.CeilingReached);
        Assert.Equal(["fresh"], await SurvivingAuthorNamesAsync());
    }

    /// <summary>
    /// Two rows sharing an ASIN at different ages is a real shape here: the unique index is on
    /// name and region, and ASIN with region is not unique. A per-row cutoff removes the stale
    /// duplicate and leaves the key answerable.
    /// </summary>
    [Fact]
    public async Task AuthorCache_RemovesAStaleDuplicateAndLeavesTheFreshOneAnsweringForTheAsin()
    {
        await SeedAsync(context =>
        {
            context.AuthorCacheEntries.Add(Author("old spelling", Now.AddDays(-40), asin: "B000000001"));
            context.AuthorCacheEntries.Add(Author("current spelling", Now.AddDays(-1), asin: "B000000001"));
        });

        await new AuthorCacheHousekeeper(_factory).RunAsync(Cycle(retentionDays: 30), default);

        Assert.Equal(["current spelling"], await SurvivingAuthorNamesAsync());
    }

    [Fact]
    public async Task SeriesCache_DeletesRowsOutsideTheWindowAndKeepsRowsInsideIt()
    {
        await SeedAsync(context =>
        {
            context.SeriesCacheEntries.Add(Series("stale", Now.AddDays(-40)));
            context.SeriesCacheEntries.Add(Series("fresh", Now.AddDays(-20)));
        });

        var outcome = await new SeriesCacheHousekeeper(_factory).RunAsync(Cycle(retentionDays: 30), default);

        Assert.Equal(1, outcome.Deleted);
        await using var context = new ListenArrDbContext(_options);
        Assert.Equal(["fresh"], await context.SeriesCacheEntries.Select(entry => entry.SeriesName).ToListAsync());
    }

    [Fact]
    public async Task DryRun_CountsWithoutDeleting_AndRepeatsWithTheSameAnswer()
    {
        await SeedAsync(context =>
        {
            context.AuthorCacheEntries.Add(Author("stale", Now.AddDays(-40)));
            context.AuthorCacheEntries.Add(Author("fresh", Now.AddDays(-20)));
        });

        var housekeeper = new AuthorCacheHousekeeper(_factory);
        var first = await housekeeper.RunAsync(Cycle(retentionDays: 30, dryRun: true), default);
        var second = await housekeeper.RunAsync(Cycle(retentionDays: 30, dryRun: true), default);

        Assert.Equal(1, first.Deleted);
        Assert.Equal(1, second.Deleted);
        Assert.Equal(2, await CountAuthorsAsync());

        // The control. The identical fixture with the switch off does delete, so the two runs
        // above are a preview rather than a predicate that matches nothing.
        await housekeeper.RunAsync(Cycle(retentionDays: 30, dryRun: false), default);
        Assert.Equal(1, await CountAuthorsAsync());
    }

    [Fact]
    public async Task TheCeiling_CapsOneCycle_AndSaysThereIsMoreToCome()
    {
        await SeedAsync(context =>
        {
            for (var index = 0; index < 5; index++)
            {
                context.AuthorCacheEntries.Add(Author($"stale {index}", Now.AddDays(-40 - index)));
            }
        });

        var capped = await new AuthorCacheHousekeeper(_factory)
            .RunAsync(Cycle(retentionDays: 30, maxRowsPerTask: 2), default);

        Assert.Equal(5, capped.Matched);
        Assert.Equal(2, capped.Deleted);
        Assert.True(capped.CeilingReached);
        Assert.Equal(3, await CountAuthorsAsync());

        // The control. The same fixture under a ceiling high enough not to bite takes the rest
        // and reports that nothing was held back.
        var uncapped = await new AuthorCacheHousekeeper(_factory)
            .RunAsync(Cycle(retentionDays: 30, maxRowsPerTask: 1000), default);

        Assert.Equal(3, uncapped.Deleted);
        Assert.False(uncapped.CeilingReached);
        Assert.Equal(0, await CountAuthorsAsync());
    }

    /// <summary>
    /// The ceiling takes the oldest rows, so a capped cycle drains a backlog from the far end
    /// rather than revisiting the same arbitrary slice.
    /// </summary>
    [Fact]
    public async Task TheCeiling_TakesTheOldestRowsFirst()
    {
        await SeedAsync(context =>
        {
            context.AuthorCacheEntries.Add(Author("oldest", Now.AddDays(-90)));
            context.AuthorCacheEntries.Add(Author("middle", Now.AddDays(-60)));
            context.AuthorCacheEntries.Add(Author("newest eligible", Now.AddDays(-31)));
        });

        await new AuthorCacheHousekeeper(_factory).RunAsync(Cycle(retentionDays: 30, maxRowsPerTask: 1), default);

        Assert.Equal(["middle", "newest eligible"], await SurvivingAuthorNamesAsync());
    }

    /// <summary>
    /// The floor through the real processor rather than through a stand-in, because a floor that
    /// exists on the housekeeper and is not applied by the sweep looks exactly like a floor that
    /// works.
    /// </summary>
    /// <remarks>
    /// The operator has asked for thirty days. The cache floor is a hundred and eighty, so the
    /// hundred-day-old row stays. The control on the same run is the two-hundred-day-old row,
    /// which is past the floor and goes: without it, "the row survived" would also be what a
    /// sweep that never reached this table looks like.
    /// </remarks>
    [Fact]
    public async Task TheCacheFloor_KeepsARowTheConfiguredWindowWouldHaveDeleted()
    {
        await SeedAsync(context =>
        {
            context.AuthorCacheEntries.Add(Author("inside the floor", Now.AddDays(-100)));
            context.AuthorCacheEntries.Add(Author("past the floor", Now.AddDays(-200)));
        });

        var configuration = new Mock<IConfigurationService>();
        configuration.Setup(service => service.GetApplicationSettingsAsync())
            .ReturnsAsync(new ApplicationSettings
            {
                HousekeepingRetentionDays = 30,
                HousekeepingDryRun = false
            });
        var services = new ServiceCollection();
        services.AddSingleton(configuration.Object);
        await using var provider = services.BuildServiceProvider();

        await new HousekeepingProcessor(
            [new AuthorCacheHousekeeper(_factory)],
            new HousekeepingOptionsHolder(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedClock(Now),
            Mock.Of<ILogger<HousekeepingProcessor>>())
            .RunCycleAsync(CancellationToken.None);

        Assert.Equal(["inside the floor"], await SurvivingAuthorNamesAsync());
    }

    [Fact]
    public async Task Journal_DeletesReconciledRowsOutsideTheWindowAndKeepsOneInsideIt()
    {
        await SeedAsync(context =>
        {
            context.FileMutationJournals.Add(Journal(
                "stale reconciled", FileMutationJournalState.OwnerMetadataReconciled, Now.AddDays(-100), audiobookFileId: 7));
            context.FileMutationJournals.Add(Journal(
                "fresh reconciled", FileMutationJournalState.OwnerMetadataReconciled, Now.AddDays(-10), audiobookFileId: 7));
        });

        var outcome = await new FileMutationJournalHousekeeper(_factory).RunAsync(Cycle(retentionDays: 90), default);

        Assert.Equal(1, outcome.Deleted);
        Assert.Equal(["fresh reconciled"], await SurvivingJournalPathsAsync());
    }

    /// <summary>
    /// The predicate this branch exists to get right. A Completed row owned by a tracked
    /// AudiobookFile is mid-flight, not terminal: the rename commit loads it by OperationId with
    /// no state filter and throws if it is gone.
    /// </summary>
    [Fact]
    public async Task Journal_KeepsAnOwnerBoundCompletedRow_HoweverOldItIs()
    {
        await SeedAsync(context =>
        {
            context.FileMutationJournals.Add(Journal(
                "owner bound, tracked file", FileMutationJournalState.Completed, Now.AddDays(-900), audiobookFileId: 42));
            context.FileMutationJournals.Add(Journal(
                "owner bound, legacy path", FileMutationJournalState.Completed, Now.AddDays(-900), audiobookFileId: 0));
        });

        var outcome = await new FileMutationJournalHousekeeper(_factory).RunAsync(Cycle(retentionDays: 90), default);

        Assert.Equal(0, outcome.Matched);
        Assert.Equal(2, await CountJournalsAsync());
    }

    /// <summary>
    /// The control for the test above. The same age, the same Completed state, a companion owner
    /// class instead, and it goes. Without this pair, "the row survived" would also be what a
    /// housekeeper matching nothing at all looks like.
    /// </summary>
    [Fact]
    public async Task Journal_DeletesACompanionCompletedRowOfTheSameAge()
    {
        await SeedAsync(context =>
        {
            context.FileMutationJournals.Add(Journal(
                "legacy companion", FileMutationJournalState.Completed, Now.AddDays(-900),
                audiobookFileId: FileMutationOwner.CompanionFile));
            context.FileMutationJournals.Add(Journal(
                "registration companion", FileMutationJournalState.Completed, Now.AddDays(-900),
                audiobookFileId: FileMutationOwner.RegistrationCompanionFile));
            context.FileMutationJournals.Add(Journal(
                "owner bound", FileMutationJournalState.Completed, Now.AddDays(-900), audiobookFileId: 42));
        });

        var outcome = await new FileMutationJournalHousekeeper(_factory).RunAsync(Cycle(retentionDays: 90), default);

        Assert.Equal(2, outcome.Deleted);
        Assert.Equal(["owner bound"], await SurvivingJournalPathsAsync());
    }

    [Fact]
    public async Task Journal_KeepsNeedsAttention_HoweverOldItIs()
    {
        await SeedAsync(context =>
        {
            context.FileMutationJournals.Add(Journal(
                "needs attention", FileMutationJournalState.NeedsAttention, Now.AddDays(-3650), audiobookFileId: 7));
            context.FileMutationJournals.Add(Journal(
                "reconciled", FileMutationJournalState.OwnerMetadataReconciled, Now.AddDays(-3650), audiobookFileId: 7));
        });

        var outcome = await new FileMutationJournalHousekeeper(_factory).RunAsync(Cycle(retentionDays: 90), default);

        // The reconciled row of the identical age is the control: the sweep did reach this table.
        Assert.Equal(1, outcome.Deleted);
        Assert.Equal(["needs attention"], await SurvivingJournalPathsAsync());
    }

    [Theory]
    [InlineData(FileMutationJournalState.Planned)]
    [InlineData(FileMutationJournalState.TargetIdentityPersisted)]
    [InlineData(FileMutationJournalState.TargetVerified)]
    [InlineData(FileMutationJournalState.RegistrationCommitted)]
    [InlineData(FileMutationJournalState.SourceDeletionAuthorized)]
    [InlineData(FileMutationJournalState.SourceDeleted)]
    public async Task Journal_KeepsEveryMidFlightState_HoweverOldItIs(FileMutationJournalState state)
    {
        await SeedAsync(context => context.FileMutationJournals.Add(
            Journal($"mid flight {state}", state, Now.AddDays(-3650), audiobookFileId: null)));

        var outcome = await new FileMutationJournalHousekeeper(_factory).RunAsync(Cycle(retentionDays: 90), default);

        Assert.Equal(0, outcome.Matched);
        Assert.Equal(1, await CountJournalsAsync());
    }

    /// <summary>
    /// A registration publication whose audiobook has been deleted is unreachable, so it goes.
    /// Its control is the identical row whose audiobook is still there, which is the receipt
    /// population this branch deliberately leaves alone.
    /// </summary>
    [Fact]
    public async Task Journal_DeletesAnOrphanedRegistrationRow_AndKeepsTheOneWhoseAudiobookIsStillThere()
    {
        var liveAudiobookId = 0;
        await SeedAsync(context =>
        {
            var audiobook = new Audiobook { Title = "Still here" };
            context.Audiobooks.Add(audiobook);
            context.SaveChanges();
            liveAudiobookId = audiobook.Id;

            context.FileMutationJournals.Add(Journal(
                "orphaned registration", FileMutationJournalState.Completed, Now.AddDays(-900),
                audiobookFileId: null, audiobookId: liveAudiobookId + 1000));
            context.FileMutationJournals.Add(Journal(
                "live registration receipt", FileMutationJournalState.Completed, Now.AddDays(-900),
                audiobookFileId: null, audiobookId: liveAudiobookId));
            context.FileMutationJournals.Add(Journal(
                "registration with no audiobook at all", FileMutationJournalState.Completed, Now.AddDays(-900),
                audiobookFileId: null, audiobookId: null));
        });

        var outcome = await new FileMutationJournalHousekeeper(_factory).RunAsync(Cycle(retentionDays: 90), default);

        Assert.Equal(1, outcome.Deleted);
        Assert.Equal(
            ["live registration receipt", "registration with no audiobook at all"],
            await SurvivingJournalPathsAsync());
    }

    /// <summary>
    /// The slice of the old population 4 that is provably unreachable: a registration publication
    /// made by Copy. Its controls are the two actions that keep the population alive, both seeded
    /// against the same live audiobook so that nothing but the action distinguishes them.
    /// </summary>
    [Fact]
    public async Task Journal_DeletesACompletedCopyRegistration_AndKeepsTheMoveAndHardlinkRowsBesideIt()
    {
        var liveAudiobookId = 0;
        await SeedAsync(context =>
        {
            var audiobook = new Audiobook { Title = "Still here" };
            context.Audiobooks.Add(audiobook);
            context.SaveChanges();
            liveAudiobookId = audiobook.Id;

            context.FileMutationJournals.Add(Journal(
                "copy registration", FileMutationJournalState.Completed, Now.AddDays(-900),
                audiobookFileId: null, audiobookId: liveAudiobookId, action: FileAction.Copy));
            context.FileMutationJournals.Add(Journal(
                "move registration receipt", FileMutationJournalState.Completed, Now.AddDays(-900),
                audiobookFileId: null, audiobookId: liveAudiobookId, action: FileAction.Move));
            context.FileMutationJournals.Add(Journal(
                "hardlink resume signal", FileMutationJournalState.Completed, Now.AddDays(-900),
                audiobookFileId: null, audiobookId: liveAudiobookId,
                action: FileAction.HardlinkCopy));
        });

        var outcome = await new FileMutationJournalHousekeeper(_factory).RunAsync(Cycle(retentionDays: 90), default);

        Assert.Equal(1, outcome.Deleted);
        Assert.Equal(
            ["hardlink resume signal", "move registration receipt"],
            await SurvivingJournalPathsAsync());
    }

    /// <summary>
    /// The Copy clause is bound to the registration owner class and not to the action alone.
    /// Completed on an owner bound row means the filesystem mutation is done and the owner metadata
    /// is not, and FileRenameCommitStore loads exactly those by operation ID with no state filter.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(FileMutationOwner.CompanionFile)]
    public async Task Journal_KeepsACompletedCopyRowThatIsOwnerBound(int audiobookFileId)
    {
        await SeedAsync(context => context.FileMutationJournals.Add(Journal(
            "owner bound copy", FileMutationJournalState.Completed, Now.AddDays(-900),
            audiobookFileId: audiobookFileId, action: FileAction.Copy)));

        var outcome = await new FileMutationJournalHousekeeper(_factory).RunAsync(Cycle(retentionDays: 90), default);

        Assert.Equal(audiobookFileId == FileMutationOwner.CompanionFile ? 1 : 0, outcome.Deleted);
        Assert.Equal(
            audiobookFileId == FileMutationOwner.CompanionFile ? 0 : 1,
            await CountJournalsAsync());
    }

    /// <summary>
    /// The compatibility table, on the one predicate that spends its operation ID rather than
    /// merely ageing it. The control is the identical row whose audiobook is still there.
    /// </summary>
    [Fact]
    public async Task CompatibilityJournal_DeletesACompletedRowWhoseAudiobookIsGone_AndKeepsTheLiveOne()
    {
        var liveAudiobookId = 0;
        await SeedAsync(context =>
        {
            var audiobook = new Audiobook { Title = "Still here" };
            context.Audiobooks.Add(audiobook);
            context.SaveChanges();
            liveAudiobookId = audiobook.Id;

            context.CompatibilityFilePublicationJournals.Add(CompatibilityJournal(
                "orphaned", CompatibilityFilePublicationState.Completed, Now.AddDays(-900),
                audiobookId: liveAudiobookId + 1000));
            context.CompatibilityFilePublicationJournals.Add(CompatibilityJournal(
                "live", CompatibilityFilePublicationState.Completed, Now.AddDays(-900),
                audiobookId: liveAudiobookId));
            context.CompatibilityFilePublicationJournals.Add(CompatibilityJournal(
                "never registered", CompatibilityFilePublicationState.Completed, Now.AddDays(-900),
                audiobookId: null));
        });

        var outcome = await new CompatibilityFilePublicationJournalHousekeeper(_factory)
            .RunAsync(Cycle(retentionDays: 90), default);

        Assert.Equal(1, outcome.Matched);
        Assert.Equal(1, outcome.Deleted);
        Assert.Equal(["live", "never registered"], await SurvivingCompatibilityPathsAsync());
    }

    /// <summary>
    /// Every state but Completed stays at any age, including NeedsAttention, which carries the only
    /// diagnosis there is for a publication nothing else will ever report.
    /// </summary>
    [Theory]
    [InlineData(CompatibilityFilePublicationState.Planned)]
    [InlineData(CompatibilityFilePublicationState.TargetVerified)]
    [InlineData(CompatibilityFilePublicationState.RegistrationCommitted)]
    [InlineData(CompatibilityFilePublicationState.NeedsAttention)]
    [InlineData(CompatibilityFilePublicationState.SourceDeleteAuthorized)]
    [InlineData(CompatibilityFilePublicationState.SourceQuarantinePlanned)]
    [InlineData(CompatibilityFilePublicationState.SourceQuarantined)]
    [InlineData(CompatibilityFilePublicationState.SourceDeleted)]
    public async Task CompatibilityJournal_KeepsEveryStateButCompleted_HoweverOldItIs(
        CompatibilityFilePublicationState state)
    {
        await SeedAsync(context => context.CompatibilityFilePublicationJournals.Add(
            CompatibilityJournal("unreachable audiobook", state, Now.AddDays(-3650), audiobookId: 999)));

        var outcome = await new CompatibilityFilePublicationJournalHousekeeper(_factory)
            .RunAsync(Cycle(retentionDays: 90), default);

        Assert.Equal(0, outcome.Matched);
        Assert.Equal(1, await CountCompatibilityJournalsAsync());
    }

    /// <summary>
    /// The cleanup coordinator retains a batch's sources unless every row it loads is at
    /// RegistrationCommitted. Deleting the one Completed row out of such a batch would flip it from
    /// retaining sources to deleting them, so the batch clause holds the row back.
    /// </summary>
    [Fact]
    public async Task CompatibilityJournal_KeepsACompletedRowWhoseBatchStillHasARegistrationCommittedSibling()
    {
        var batchId = Guid.NewGuid();
        await SeedAsync(context =>
        {
            context.CompatibilityFilePublicationJournals.Add(CompatibilityJournal(
                "completed member", CompatibilityFilePublicationState.Completed, Now.AddDays(-900),
                audiobookId: 999, batchId: batchId));
            context.CompatibilityFilePublicationJournals.Add(CompatibilityJournal(
                "committed sibling", CompatibilityFilePublicationState.RegistrationCommitted,
                Now.AddDays(-900), audiobookId: 999, batchId: batchId));
            context.CompatibilityFilePublicationJournals.Add(CompatibilityJournal(
                "completed member of a settled batch", CompatibilityFilePublicationState.Completed,
                Now.AddDays(-900), audiobookId: 999, batchId: Guid.NewGuid()));
        });

        var outcome = await new CompatibilityFilePublicationJournalHousekeeper(_factory)
            .RunAsync(Cycle(retentionDays: 90), default);

        Assert.Equal(1, outcome.Deleted);
        Assert.Equal(
            ["committed sibling", "completed member"],
            await SurvivingCompatibilityPathsAsync());
    }

    [Fact]
    public async Task CompatibilityJournal_KeepsACompletedOrphanInsideTheWindow()
    {
        await SeedAsync(context => context.CompatibilityFilePublicationJournals.Add(
            CompatibilityJournal(
                "recent orphan", CompatibilityFilePublicationState.Completed, Now.AddDays(-10),
                audiobookId: 999)));

        var outcome = await new CompatibilityFilePublicationJournalHousekeeper(_factory)
            .RunAsync(Cycle(retentionDays: 90), default);

        Assert.Equal(0, outcome.Matched);
        Assert.Equal(1, await CountCompatibilityJournalsAsync());
    }

    /// <summary>
    /// The compatibility predicate rests on a deleted audiobook's ID never coming back, because the
    /// ID is one of the inputs hashed into the operation ID a repeat would have to present. That is
    /// a schema fact rather than a code fact, so it is asserted against the migrated schema.
    /// </summary>
    [Fact]
    public async Task AudiobookIdsAreNotReusedAfterDeletion()
    {
        await using var context = new ListenArrDbContext(_options);
        var definition = await context.Database
            .SqlQuery<string>(
                $"SELECT sql AS Value FROM sqlite_master WHERE type = 'table' AND name = 'Audiobooks'")
            .SingleAsync();

        Assert.Contains("AUTOINCREMENT", definition, StringComparison.Ordinal);
    }

    /// <summary>
    /// The floors, as one table. Each is argued where it is declared; what this pins is that they
    /// are not all the same number, which is what a floor mechanism quietly reverting to a single
    /// global window would look like.
    /// </summary>
    [Fact]
    public void EachHousekeeperDeclaresItsOwnFloor()
    {
        Assert.Equal(90, new FileMutationJournalHousekeeper(_factory).MinimumRetentionDays);
        Assert.Equal(
            90,
            new CompatibilityFilePublicationJournalHousekeeper(_factory).MinimumRetentionDays);
        Assert.Equal(180, new AuthorCacheHousekeeper(_factory).MinimumRetentionDays);
        Assert.Equal(180, new SeriesCacheHousekeeper(_factory).MinimumRetentionDays);
        Assert.Equal(0, new MoveJobHousekeeper(_factory).MinimumRetentionDays);
    }

    private static HousekeepingCycle Cycle(
        int retentionDays,
        bool dryRun = false,
        int maxRowsPerTask = HousekeepingProcessor.MaxRowsPerTaskPerCycle) =>
        new(Now.AddDays(-retentionDays), retentionDays, dryRun, maxRowsPerTask);

    private static AuthorCacheEntry Author(string name, DateTime updatedAt, string? asin = null) =>
        new()
        {
            AuthorName = name,
            AuthorNameNormalized = name.ToLowerInvariant(),
            AuthorAsin = asin,
            Region = "us",
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt
        };

    private static SeriesCacheEntry Series(string name, DateTime updatedAt) =>
        new()
        {
            SeriesName = name,
            SeriesNameNormalized = name.ToLowerInvariant(),
            Region = "us",
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt
        };

    private static FileMutationJournal Journal(
        string sourcePath,
        FileMutationJournalState state,
        DateTime updatedAt,
        int? audiobookFileId,
        int? audiobookId = null,
        FileAction action = FileAction.Move) =>
        new()
        {
            OperationId = Guid.NewGuid(),
            Action = action,
            SourcePath = sourcePath,
            DestinationPath = $"{sourcePath} (destination)",
            SourceParentDirectoryObjectIdentity = "source-parent",
            DestinationParentDirectoryObjectIdentity = "destination-parent",
            SourcePhysicalObjectIdentity = "source-object",
            State = state,
            AudiobookFileId = audiobookFileId,
            AudiobookId = audiobookId,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt
        };

    private static CompatibilityFilePublicationJournal CompatibilityJournal(
        string sourcePath,
        CompatibilityFilePublicationState state,
        DateTime updatedAt,
        int? audiobookId,
        Guid? batchId = null) =>
        new()
        {
            OperationId = Guid.NewGuid(),
            BatchId = batchId,
            RequestedAction = FileAction.Move,
            EffectiveAction = FileAction.Copy,
            SourcePath = sourcePath,
            DestinationPath = $"{sourcePath} (destination)",
            SourceLength = 1,
            SourceSha256 = new string('a', 64),
            State = state,
            AudiobookId = audiobookId,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt
        };

    private async Task<List<string>> SurvivingCompatibilityPathsAsync()
    {
        await using var context = new ListenArrDbContext(_options);
        return await context.CompatibilityFilePublicationJournals
            .OrderBy(journal => journal.SourcePath)
            .Select(journal => journal.SourcePath)
            .ToListAsync();
    }

    private async Task<int> CountCompatibilityJournalsAsync()
    {
        await using var context = new ListenArrDbContext(_options);
        return await context.CompatibilityFilePublicationJournals.CountAsync();
    }

    private async Task SeedAsync(Action<ListenArrDbContext> seed)
    {
        await using var context = new ListenArrDbContext(_options);
        seed(context);
        await context.SaveChangesAsync();
    }

    private async Task<List<string>> SurvivingAuthorNamesAsync()
    {
        await using var context = new ListenArrDbContext(_options);
        return await context.AuthorCacheEntries
            .OrderBy(entry => entry.AuthorName)
            .Select(entry => entry.AuthorName)
            .ToListAsync();
    }

    private async Task<List<string>> SurvivingJournalPathsAsync()
    {
        await using var context = new ListenArrDbContext(_options);
        return await context.FileMutationJournals
            .OrderBy(journal => journal.SourcePath)
            .Select(journal => journal.SourcePath)
            .ToListAsync();
    }

    private async Task<int> CountAuthorsAsync()
    {
        await using var context = new ListenArrDbContext(_options);
        return await context.AuthorCacheEntries.CountAsync();
    }

    private async Task<int> CountJournalsAsync()
    {
        await using var context = new ListenArrDbContext(_options);
        return await context.FileMutationJournals.CountAsync();
    }

    /// <summary>
    /// Only CreateDbContext is implemented. The interface's CreateDbContextAsync overloads
    /// default to it, and declaring a parameterless one here would not override the one the
    /// housekeepers call, which takes a cancellation token.
    /// </summary>
    private sealed class TestDbContextFactory(DbContextOptions<ListenArrDbContext> options)
        : IDbContextFactory<ListenArrDbContext>
    {
        public ListenArrDbContext CreateDbContext() => new(options);
    }
}
