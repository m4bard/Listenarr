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

    [Fact]
    public async Task Journal_DeclaresANinetyDayFloor()
    {
        Assert.Equal(90, new FileMutationJournalHousekeeper(_factory).MinimumRetentionDays);
        Assert.Equal(0, new AuthorCacheHousekeeper(_factory).MinimumRetentionDays);
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
        int? audiobookId = null) =>
        new()
        {
            OperationId = Guid.NewGuid(),
            Action = FileAction.Move,
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

    private sealed class TestDbContextFactory(DbContextOptions<ListenArrDbContext> options)
        : IDbContextFactory<ListenArrDbContext>
    {
        public ListenArrDbContext CreateDbContext() => new(options);

        public Task<ListenArrDbContext> CreateDbContextAsync() =>
            Task.FromResult(new ListenArrDbContext(options));
    }
}
