/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Infrastructure.Downloads.Blocklist;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Downloads.Blocklist;

/// <summary>
/// BlockAsync reads and then writes, and (AudiobookId, ReleaseIdentifier) is uniquely indexed, so
/// two failures for the same release in one poll cycle can both get past the read.
/// </summary>
[Trait("Name", "BlocklistServiceTests")]
[Trait("Category", "Blocklist")]
public sealed class BlocklistServiceTests : BaseTests
{
    private static readonly ReleaseIdentifier Identifier =
        ReleaseIdentifier.FromStorage("btih:abcdef1234567890abcdef1234567890abcdef12");

    private static readonly ReleaseIdentifier OtherIdentifier =
        ReleaseIdentifier.FromStorage("btih:1111111111111111111111111111111111111111");

    private DbContextOptions<ListenArrDbContext> _options = null!;

    // A file-backed SQLite database rather than the shared context from BaseTests, because the
    // race these tests reproduce needs a second connection writing the rival row while the first
    // context still holds its stale read.
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        var databasePath = Path.Join(FileService.GetTempPath(), $"blocklist-{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
        await using var db = new ListenArrDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    [Fact]
    public async Task BlockAsync_LosingTheInsertRace_IsTreatedAsAlreadyBlocked()
    {
        // The interleaving the unique index exists to catch, made deterministic: the rival row
        // lands between this call's read and its write. Before the catch, the violation left
        // DownloadMonitorService.OnDownloadFailed halfway through its failure handling.
        await using var context = new RacingDbContext(_options, WriteRivalEntry);
        var service = new BlocklistService(context, NullLogger<BlocklistService>.Instance);

        await service.BlockAsync(7, Identifier, "Mine", 800_000_000, "this call");

        var rows = await ReadAllAsync();
        Assert.Equal("the rival", Assert.Single(rows).Reason);
    }

    [Fact]
    public async Task BlockAsync_AfterLosingTheRace_LeavesTheContextUsable()
    {
        // A caught violation leaves the losing row tracked as Added unless it is detached, and the
        // context is scoped, so the next SaveChangesAsync on it would retry that insert and throw
        // again. This is the assertion that fails if the detach is dropped.
        await using var context = new RacingDbContext(_options, WriteRivalEntry);
        var service = new BlocklistService(context, NullLogger<BlocklistService>.Instance);

        await service.BlockAsync(7, Identifier, "Mine", 800_000_000, "this call");
        await service.BlockAsync(7, OtherIdentifier, "A different release", 1, "a later failure");

        var rows = await ReadAllAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.ReleaseIdentifier == OtherIdentifier);
    }

    [Fact]
    public async Task BlockAsync_WhenTheEntryIsAlreadyThere_WritesNothingFurther()
    {
        await using var context = new ListenArrDbContext(_options);
        var service = new BlocklistService(context, NullLogger<BlocklistService>.Instance);

        await service.BlockAsync(7, Identifier, "Mine", 800_000_000, "first");
        await service.BlockAsync(7, Identifier, "Mine again", 800_000_000, "second");

        Assert.Equal("first", Assert.Single(await ReadAllAsync()).Reason);
    }

    [Fact]
    public async Task TheIdentifierColumn_HoldsTheSameTextAsBeforeItBecameAValueType()
    {
        // ReleaseIdentifier is a value type on the entity and plain TEXT in the column, and the
        // text is the same key it always was. Asserted against the literal rather than against
        // Identifier.Key, because comparing the round trip with itself would pass just as happily
        // if the converter wrote the type name on both sides.
        await using var context = new ListenArrDbContext(_options);
        var service = new BlocklistService(context, NullLogger<BlocklistService>.Instance);
        await service.BlockAsync(7, Identifier, "Mine", 800_000_000, "first");

        await using var db = new ListenArrDbContext(_options);
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ReleaseIdentifier FROM BlockedReleases";

        Assert.Equal(
            "btih:abcdef1234567890abcdef1234567890abcdef12",
            (string?)await command.ExecuteScalarAsync());

        // The control, and the half a materialisation test cannot make on its own: the converter
        // has to be in the query too. A comparison EF could not translate would either throw or
        // quietly match everything, and both show up here rather than in the read above.
        Assert.NotNull(await db.Set<BlockedRelease>()
            .FirstOrDefaultAsync(entry => entry.ReleaseIdentifier == Identifier));
        Assert.Null(await db.Set<BlockedRelease>()
            .FirstOrDefaultAsync(entry => entry.ReleaseIdentifier == OtherIdentifier));
    }

    [Fact]
    public async Task AMalformedRowDoesNotBreakTheSearchPathForThatBook()
    {
        // The read side has to be lenient, and this is why. GetForAudiobookAsync is called on the
        // search path, so anything it throws takes out searching for that book, not just the row.
        // The column is only NOT NULL, which an empty string satisfies, so a hand-edited database
        // or a restored backup can put one there. An earlier version of FromStorage threw
        // ArgumentException on it and produced exactly that outcome.
        await using var db = new ListenArrDbContext(_options);
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText =
                "INSERT INTO BlockedReleases (AudiobookId, ReleaseIdentifier, Title, Size, "
                + "BlockedAt, Reason) VALUES (7, '', 'Some Book', NULL, '2026-01-01', 'hand edited')";
            await insert.ExecuteNonQueryAsync();
        }

        await using var context = new ListenArrDbContext(_options);
        var service = new BlocklistService(context, NullLogger<BlocklistService>.Instance);

        var rows = await service.GetForAudiobookAsync(7);
        var row = Assert.Single(rows);
        Assert.True(row.ReleaseIdentifier.IsEmpty);

        // And the empty key contributes no match of its own. The row's Title column is still a
        // live second key by design, so the candidate here is one that key cannot reach: a
        // different title, which leaves the identifier as the only route to a match.
        var differentRelease = new SearchResult { Title = "Quite Another Book", Size = 1 };
        Assert.False(ReleaseIdentity.Matches(row, differentRelease));

        // The control, so the assertion above is not passing because Matches has stopped working:
        // the same candidate does match a row whose key carries that title.
        Assert.True(ReleaseIdentity.Matches(
            new BlockedRelease
            {
                ReleaseIdentifier = ReleaseIdentity.KeyFor(null, "Quite Another Book")!.Value,
                Title = string.Empty,
                Size = null
            },
            differentRelease));
    }

    [Fact]
    public async Task BlockedRelease_IsMappedWithoutADbSetPropertyOnTheContext()
    {
        // The table is registered by BlockedReleaseConfiguration through
        // ApplyConfigurationsFromAssembly, not by a DbSet property on ListenArrDbContext.
        // Deliberate: that one list of DbSets is edited by every open pull request that adds a
        // table, and this branch does not need to be in the queue for it.
        Assert.Null(typeof(ListenArrDbContext).GetProperty("BlockedReleases"));

        await using var db = new ListenArrDbContext(_options);

        // The control for the assertion above. "No property" has to mean "registered somewhere
        // else", not "not registered", and those two look identical from the context's public
        // surface. A model that had lost the entity fails here instead of passing quietly.
        var entityType = Assert.Single(
            db.Model.GetEntityTypes(),
            candidate => candidate.ClrType == typeof(BlockedRelease));

        // And the table it maps to, which is the part the DbSet property was quietly supplying.
        // Without a property to take the name from, EF falls back to the type name and maps this
        // to "BlockedRelease" while the migration creates "BlockedReleases". Round-tripping a row
        // does not catch it, because EnsureCreated builds whatever the model asked for.
        Assert.Equal("BlockedReleases", entityType.GetTableName());

        db.Set<BlockedRelease>().Add(new BlockedRelease
        {
            AudiobookId = 7,
            ReleaseIdentifier = Identifier,
            Title = "Mine",
            Size = 800_000_000,
            Reason = "mapped without a DbSet"
        });
        await db.SaveChangesAsync();

        Assert.Equal(Identifier, Assert.Single(await ReadAllAsync()).ReleaseIdentifier);
    }

    private async Task<List<BlockedRelease>> ReadAllAsync()
    {
        await using var db = new ListenArrDbContext(_options);
        return await db.Set<BlockedRelease>().AsNoTracking().ToListAsync();
    }

    private void WriteRivalEntry()
    {
        using var rival = new ListenArrDbContext(_options);
        rival.Set<BlockedRelease>().Add(new BlockedRelease
        {
            AudiobookId = 7,
            ReleaseIdentifier = Identifier,
            Title = "Mine",
            Size = 800_000_000,
            Reason = "the rival"
        });
        rival.SaveChanges();
    }

    /// <summary>
    /// Runs a rival insert once, immediately before the first save, so the read this service
    /// already did is stale by the time it writes. A genuine UNIQUE violation from SQLite follows,
    /// which is what the production catch has to handle.
    /// </summary>
    private sealed class RacingDbContext(
        DbContextOptions<ListenArrDbContext> options,
        Action beforeFirstSave) : ListenArrDbContext(options)
    {
        private bool _done;

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_done)
            {
                _done = true;
                beforeFirstSave();
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
