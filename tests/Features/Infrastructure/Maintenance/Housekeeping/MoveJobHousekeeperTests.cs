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
/// The move job sweep, against a real migrated SQLite database, because the predicate carries
/// three correlated subqueries and the delete has to take three child tables with it.
/// </summary>
[Trait("Area", "Housekeeping")]
[Trait("Name", "MoveJobHousekeeperTests")]
[Trait("Category", "Persistence")]
public sealed class MoveJobHousekeeperTests : BaseTests, IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ListenArrDbContext> _options;
    private readonly TestDbContextFactory _factory;

    public MoveJobHousekeeperTests()
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

    /// <summary>
    /// Whether a bare DELETE cascades on this stack is an inference about a library default
    /// until somebody runs it, and being wrong orphans the largest of the three child tables
    /// forever. So it is measured rather than assumed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The answer, as of this commit: the pragma reports on and the cascade fires, so the three
    /// child tables go to zero. Nothing in this repository sets PRAGMA foreign_keys, so that is
    /// the Microsoft.Data.Sqlite default rather than anything this application asked for, and a
    /// connection string keyword is all it would take to flip it. The sweep deletes children
    /// explicitly anyway, and this test is what would notice if the default moved.
    /// </para>
    /// <para>
    /// Two controls. The pragma is read back on the same connection and the two arms must agree
    /// with it, so an apparatus that answered the same way regardless fails one of them. And the
    /// children are counted before the delete, so a fixture that never wrote any cannot pass the
    /// cascade arm by having nothing to cascade to.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Measured_WhetherABareDeleteCascadesToTheChildTables()
    {
        var jobId = await SeedJobAsync(MoveJobStatus.Completed, Now.AddDays(-100), children: 3);

        await using (var precondition = new ListenArrDbContext(_options))
        {
            Assert.Equal(3, await precondition.MoveJobEntries.CountAsync(e => e.MoveJobId == jobId));
            Assert.Equal(3, await precondition.MoveJobCreatedDirectories.CountAsync(d => d.MoveJobId == jobId));
            Assert.Equal(1, await precondition.MoveScanHandoffs.CountAsync(h => h.MoveJobId == jobId));
        }

        bool foreignKeysEnforced;
        await using (var command = _connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys;";
            foreignKeysEnforced = Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
        }

        await using (var context = new ListenArrDbContext(_options))
        {
            await context.MoveJobs.Where(job => job.Id == jobId).ExecuteDeleteAsync();
        }

        await using var reader = new ListenArrDbContext(_options);
        var survivingEntries = await reader.MoveJobEntries.CountAsync(e => e.MoveJobId == jobId);
        var survivingDirectories = await reader.MoveJobCreatedDirectories.CountAsync(d => d.MoveJobId == jobId);
        var survivingHandoffs = await reader.MoveScanHandoffs.CountAsync(h => h.MoveJobId == jobId);
        Assert.Equal(0, await reader.MoveJobs.CountAsync(job => job.Id == jobId));

        if (foreignKeysEnforced)
        {
            Assert.Equal(0, survivingEntries);
            Assert.Equal(0, survivingDirectories);
            Assert.Equal(0, survivingHandoffs);
        }
        else
        {
            // The outcome the explicit children-then-parents delete exists to avoid.
            Assert.Equal(3, survivingEntries);
            Assert.Equal(3, survivingDirectories);
            Assert.Equal(1, survivingHandoffs);
        }
    }

    [Fact]
    public async Task DeletesAFinishedJobOutsideTheWindowAndTakesItsChildrenWithIt()
    {
        var doomed = await SeedJobAsync(MoveJobStatus.Completed, Now.AddDays(-100), children: 3);
        var kept = await SeedJobAsync(MoveJobStatus.Completed, Now.AddDays(-10), children: 2);

        var outcome = await new MoveJobHousekeeper(_factory).RunAsync(Cycle(retentionDays: 30), default);

        Assert.Equal(1, outcome.Matched);
        Assert.Equal(1, outcome.Deleted);
        await using var context = new ListenArrDbContext(_options);
        Assert.Equal([kept], await context.MoveJobs.Select(job => job.Id).ToListAsync());
        Assert.Equal(0, await context.MoveJobEntries.CountAsync(entry => entry.MoveJobId == doomed));
        Assert.Equal(0, await context.MoveJobCreatedDirectories.CountAsync(d => d.MoveJobId == doomed));
        Assert.Equal(0, await context.MoveScanHandoffs.CountAsync(h => h.MoveJobId == doomed));

        // The control that makes the three zeroes above mean something: the surviving job still
        // has all of its own children, so the sweep deleted by parent rather than by table.
        Assert.Equal(2, await context.MoveJobEntries.CountAsync(entry => entry.MoveJobId == kept));
        Assert.Equal(2, await context.MoveJobCreatedDirectories.CountAsync(d => d.MoveJobId == kept));
        Assert.Equal(1, await context.MoveScanHandoffs.CountAsync(h => h.MoveJobId == kept));
    }

    /// <summary>
    /// The plainest job there is: no relocation and no scan handoff at all, which is what an
    /// ordinary single-audiobook move leaves behind once its rescan row has been cleared. Both
    /// subquery clauses have to pass on absence rather than only on a satisfied row, and every
    /// other test in this file seeds a handoff, so without this the absent-handoff half of that
    /// clause is never exercised.
    /// </summary>
    [Fact]
    public async Task DeletesAJobWithNoRelocationAndNoHandoffAtAll()
    {
        var doomed = await SeedJobAsync(
            MoveJobStatus.Completed, Now.AddDays(-100), children: 2, handoffStatus: null);
        var kept = await SeedJobAsync(
            MoveJobStatus.Completed, Now.AddDays(-10), children: 2, handoffStatus: null);

        var outcome = await new MoveJobHousekeeper(_factory).RunAsync(Cycle(retentionDays: 30), default);

        Assert.Equal(1, outcome.Matched);
        Assert.Equal(1, outcome.Deleted);
        await using var context = new ListenArrDbContext(_options);
        Assert.Equal([kept], await context.MoveJobs.Select(job => job.Id).ToListAsync());
        Assert.Equal(0, await context.MoveJobEntries.CountAsync(entry => entry.MoveJobId == doomed));
        Assert.Equal(2, await context.MoveJobEntries.CountAsync(entry => entry.MoveJobId == kept));
    }

    [Fact]
    public async Task DeletesASupersededJob()
    {
        await SeedJobAsync(MoveJobStatus.Superseded, Now.AddDays(-100), children: 1);

        var outcome = await new MoveJobHousekeeper(_factory).RunAsync(Cycle(retentionDays: 30), default);

        Assert.Equal(1, outcome.Deleted);
    }

    [Theory]
    [InlineData(MoveJobStatus.Queued)]
    [InlineData(MoveJobStatus.Running)]
    [InlineData(MoveJobStatus.RetryScheduled)]
    [InlineData(MoveJobStatus.Failed)]
    [InlineData(MoveJobStatus.NeedsAttention)]
    public async Task KeepsANonTerminalOrOperatorOwnedJob_HoweverOldItIs(MoveJobStatus status)
    {
        await SeedJobAsync(status, Now.AddDays(-3650), children: 1);

        var outcome = await new MoveJobHousekeeper(_factory).RunAsync(Cycle(retentionDays: 30), default);

        Assert.Equal(0, outcome.Matched);
        await using var context = new ListenArrDbContext(_options);
        Assert.Equal(1, await context.MoveJobs.CountAsync());
    }

    /// <summary>
    /// The control for the theory above. A Completed job of exactly the same age does go, so the
    /// five survivals are the status predicate and not a sweep that matches nothing.
    /// </summary>
    [Fact]
    public async Task ACompletedJobOfTheSameAgeAsTheKeptStatusesIsDeleted()
    {
        await SeedJobAsync(MoveJobStatus.NeedsAttention, Now.AddDays(-3650), children: 1);
        var doomed = await SeedJobAsync(MoveJobStatus.Completed, Now.AddDays(-3650), children: 1);

        await new MoveJobHousekeeper(_factory).RunAsync(Cycle(retentionDays: 30), default);

        await using var context = new ListenArrDbContext(_options);
        Assert.DoesNotContain(doomed, await context.MoveJobs.Select(job => job.Id).ToListAsync());
        Assert.Equal(1, await context.MoveJobs.CountAsync());
    }

    /// <summary>
    /// A job whose relocation has not finished is never swept, whatever its own status says. The
    /// reconciliation pass recomputes its completed-job count from the surviving rows, so
    /// removing part of a relocation's job set changes what that pass concludes, and the abandon
    /// path performs filesystem retirement when the set is empty.
    /// </summary>
    [Fact]
    public async Task KeepsAJobWhoseRelocationHasNotFinished()
    {
        var unfinished = await SeedRelocationAsync(RootFolderRelocationStatus.Running, activeRootFolderId: 1);
        await SeedJobAsync(MoveJobStatus.Completed, Now.AddDays(-100), children: 1, relocationId: unfinished);

        var outcome = await new MoveJobHousekeeper(_factory).RunAsync(Cycle(retentionDays: 30), default);

        Assert.Equal(0, outcome.Matched);
    }

    [Fact]
    public async Task KeepsAJobWhoseRelocationIsCompletedButStillHoldsAnActiveRootFolder()
    {
        var stillActive = await SeedRelocationAsync(RootFolderRelocationStatus.Completed, activeRootFolderId: 1);
        await SeedJobAsync(MoveJobStatus.Completed, Now.AddDays(-100), children: 1, relocationId: stillActive);

        var outcome = await new MoveJobHousekeeper(_factory).RunAsync(Cycle(retentionDays: 30), default);

        Assert.Equal(0, outcome.Matched);
    }

    /// <summary>
    /// The control for the two relocation tests. The same job under a relocation that is finished
    /// on both counts is swept.
    /// </summary>
    [Fact]
    public async Task DeletesAJobWhoseRelocationIsFinished()
    {
        var finished = await SeedRelocationAsync(RootFolderRelocationStatus.Completed, activeRootFolderId: null);
        await SeedJobAsync(MoveJobStatus.Completed, Now.AddDays(-100), children: 1, relocationId: finished);

        var outcome = await new MoveJobHousekeeper(_factory).RunAsync(Cycle(retentionDays: 30), default);

        Assert.Equal(1, outcome.Deleted);

        // And the relocation itself is untouched. Nothing here deletes one, and the Restrict
        // between them would refuse anyway while any job still referenced it.
        await using var context = new ListenArrDbContext(_options);
        Assert.Equal(1, await context.RootFolderRelocations.CountAsync());
    }

    [Theory]
    [InlineData(MoveScanHandoffStatus.Pending)]
    [InlineData(MoveScanHandoffStatus.Claimed)]
    [InlineData(MoveScanHandoffStatus.Failed)]
    public async Task KeepsACompletedJobWhoseRescanHasNotFinished(MoveScanHandoffStatus status)
    {
        await SeedJobAsync(MoveJobStatus.Completed, Now.AddDays(-100), children: 1, handoffStatus: status);

        var outcome = await new MoveJobHousekeeper(_factory).RunAsync(Cycle(retentionDays: 30), default);

        Assert.Equal(0, outcome.Matched);
    }

    [Theory]
    [InlineData(MoveScanHandoffStatus.Succeeded)]
    [InlineData(MoveScanHandoffStatus.Superseded)]
    public async Task DeletesACompletedJobWhoseRescanIsDone(MoveScanHandoffStatus status)
    {
        await SeedJobAsync(MoveJobStatus.Completed, Now.AddDays(-100), children: 1, handoffStatus: status);

        var outcome = await new MoveJobHousekeeper(_factory).RunAsync(Cycle(retentionDays: 30), default);

        Assert.Equal(1, outcome.Deleted);
    }

    /// <summary>
    /// A terminal job that predates the CompletedAt column and was never backfilled is left
    /// alone rather than treated as infinitely old.
    /// </summary>
    [Fact]
    public async Task KeepsATerminalJobWithNoTerminalTimestamp()
    {
        var undated = await SeedJobAsync(MoveJobStatus.Completed, completedAt: null, children: 1);

        var outcome = await new MoveJobHousekeeper(_factory).RunAsync(Cycle(retentionDays: 30), default);

        Assert.Equal(0, outcome.Matched);
        await using var context = new ListenArrDbContext(_options);
        Assert.Equal([undated], await context.MoveJobs.Select(job => job.Id).ToListAsync());
    }

    [Fact]
    public async Task DryRun_CountsWithoutDeleting()
    {
        await SeedJobAsync(MoveJobStatus.Completed, Now.AddDays(-100), children: 3);

        var preview = await new MoveJobHousekeeper(_factory)
            .RunAsync(Cycle(retentionDays: 30, dryRun: true), default);

        Assert.Equal(1, preview.Deleted);
        await using var context = new ListenArrDbContext(_options);
        Assert.Equal(1, await context.MoveJobs.CountAsync());
        Assert.Equal(3, await context.MoveJobEntries.CountAsync());
    }

    private static HousekeepingCycle Cycle(
        int retentionDays,
        bool dryRun = false,
        int maxRowsPerTask = HousekeepingProcessor.MaxRowsPerTaskPerCycle) =>
        new(Now.AddDays(-retentionDays), retentionDays, dryRun, maxRowsPerTask);

    private async Task<Guid> SeedRelocationAsync(
        RootFolderRelocationStatus status,
        int? activeRootFolderId)
    {
        await using var context = new ListenArrDbContext(_options);
        var relocation = new RootFolderRelocation
        {
            Id = Guid.NewGuid(),
            Status = status,
            ActiveRootFolderId = activeRootFolderId,
            SourcePath = "source",
            TargetPath = "target"
        };
        context.RootFolderRelocations.Add(relocation);
        await context.SaveChangesAsync();
        return relocation.Id;
    }

    private async Task<Guid> SeedJobAsync(
        MoveJobStatus status,
        DateTime? completedAt,
        int children,
        Guid? relocationId = null,
        MoveScanHandoffStatus? handoffStatus = MoveScanHandoffStatus.Succeeded)
    {
        await using var context = new ListenArrDbContext(_options);
        var job = new MoveJob
        {
            Id = Guid.NewGuid(),
            AudiobookId = 1,
            RequestedPath = "requested",
            EnqueuedAt = Now.AddDays(-200),
            Status = status,
            CompletedAt = completedAt,
            RelocationId = relocationId
        };
        context.MoveJobs.Add(job);

        for (var index = 0; index < children; index++)
        {
            context.MoveJobEntries.Add(new MoveJobEntry
            {
                MoveJobId = job.Id,
                RelativePath = $"file-{index}.m4b",
                EntryType = MoveJobEntryType.File
            });
            context.MoveJobCreatedDirectories.Add(new MoveJobCreatedDirectory
            {
                MoveJobId = job.Id,
                Path = $"directory-{index}"
            });
        }

        if (handoffStatus is { } status_)
        {
            context.MoveScanHandoffs.Add(new MoveScanHandoff
            {
                Id = Guid.NewGuid(),
                MoveJobId = job.Id,
                AudiobookId = 1,
                TargetPath = "target",
                Status = status_
            });
        }

        await context.SaveChangesAsync();
        return job.Id;
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
