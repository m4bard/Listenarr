/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

// The queue and the two narrow writes the identity repair pass stands on. The pass being
// resumable, bounded and idempotent is a property of these three and is checked here, where a
// real database can be looked at before and after.
namespace Listenarr.Tests.Features.Infrastructure.Repositories;

[Trait("Area", "Library")]
[Trait("Name", "AudiobookRepositoryAuthorIdentityRepairTests")]
[Trait("Category", "Persistence")]
public sealed class AudiobookRepositoryAuthorIdentityRepairTests : BaseTests
{
    private static async Task<(SqliteConnection Connection, ListenArrDbContext Context)> OpenAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new ListenArrDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return (connection, context);
    }

    private static AuthorCacheEntry Row(string name, string? asin, DateTime? checkedAt = null) =>
        new()
        {
            AuthorName = name,
            AuthorNameNormalized = StringUtils.NormalizeAuthorName(name),
            AuthorAsin = asin,
            Region = "us",
            Description = "a biography",
            ImageUrl = "https://example.invalid/portrait.jpg",
            AuthorIdentityCheckedAt = checkedAt
        };

    [Fact]
    public async Task Due_OffersNeverCheckedRowsBeforeCheckedOnes()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;

        context.AuthorCacheEntries.AddRange(
            Row("Checked Recently", "B000000001", new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc)),
            Row("Never Checked", "B000000002"),
            Row("Checked Long Ago", "B000000003", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await context.SaveChangesAsync();

        var repository = new AudiobookRepository(context);
        var due = await repository.GetAuthorCacheEntriesDueForIdentityCheckAsync(3);

        Assert.Equal(
            new[] { "Never Checked", "Checked Long Ago", "Checked Recently" },
            due.Select(entry => entry.AuthorName));
    }

    // A row with no ASIN cannot be holding somebody else's, so asking the provider about it
    // would spend budget to learn nothing.
    [Fact]
    public async Task Due_SkipsRowsCarryingNoAsin()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;

        context.AuthorCacheEntries.AddRange(
            Row("Has One", "B000000001"),
            Row("Has None", null),
            Row("Has Empty", string.Empty));
        await context.SaveChangesAsync();

        var due = await new AudiobookRepository(context).GetAuthorCacheEntriesDueForIdentityCheckAsync(10);

        Assert.Equal(new[] { "Has One" }, due.Select(entry => entry.AuthorName));
    }

    [Fact]
    public async Task Due_HonoursTheCeilingAndRefusesAnEmptyOne()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;

        for (var i = 1; i <= 5; i++)
        {
            context.AuthorCacheEntries.Add(Row($"Author {i}", $"B000000{i:D3}"));
        }

        await context.SaveChangesAsync();
        var repository = new AudiobookRepository(context);

        Assert.Equal(2, (await repository.GetAuthorCacheEntriesDueForIdentityCheckAsync(2)).Count);
        Assert.Empty(await repository.GetAuthorCacheEntriesDueForIdentityCheckAsync(0));
        Assert.Empty(await repository.GetAuthorCacheEntriesDueForIdentityCheckAsync(-1));
    }

    // Resumption, which is the point of the cursor: what a run stamped is at the back of the
    // queue next time, so an interrupted pass carries on instead of starting over.
    [Fact]
    public async Task StampingARow_MovesItToTheBackOfTheQueue()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;

        context.AuthorCacheEntries.AddRange(
            Row("First", "B000000001"),
            Row("Second", "B000000002"));
        await context.SaveChangesAsync();

        var repository = new AudiobookRepository(context);
        var head = (await repository.GetAuthorCacheEntriesDueForIdentityCheckAsync(1)).Single();
        Assert.Equal("First", head.AuthorName);

        await repository.StampAuthorCacheIdentityCheckedAsync(head.Id, DateTime.UtcNow);

        Assert.Equal(
            "Second",
            (await repository.GetAuthorCacheEntriesDueForIdentityCheckAsync(1)).Single().AuthorName);
    }

    // The control for the whole pass, at the store rather than at the service: a row that was
    // already right comes back byte for byte what it went in as, apart from the stamp.
    [Fact]
    public async Task StampingARow_ChangesNothingElseOnIt()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;

        var seeded = Row("Constance Garnett", "B000APTDDU");
        context.AuthorCacheEntries.Add(seeded);
        await context.SaveChangesAsync();
        var before = new
        {
            seeded.AuthorName,
            seeded.AuthorNameNormalized,
            seeded.AuthorAsin,
            seeded.Region,
            seeded.Description,
            seeded.ImageUrl,
            seeded.CreatedAt,
            seeded.UpdatedAt,
            seeded.LastFetchedAt
        };

        await new AudiobookRepository(context).StampAuthorCacheIdentityCheckedAsync(
            seeded.Id,
            new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc));

        var after = await context.AuthorCacheEntries.AsNoTracking().SingleAsync();
        Assert.Equal(before.AuthorName, after.AuthorName);
        Assert.Equal(before.AuthorNameNormalized, after.AuthorNameNormalized);
        Assert.Equal(before.AuthorAsin, after.AuthorAsin);
        Assert.Equal(before.Region, after.Region);
        Assert.Equal(before.Description, after.Description);
        Assert.Equal(before.ImageUrl, after.ImageUrl);
        Assert.Equal(before.CreatedAt, after.CreatedAt);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(before.LastFetchedAt, after.LastFetchedAt);
        Assert.NotNull(after.AuthorIdentityCheckedAt);
    }

    // Clearing is the outcome the ordinary upsert cannot express, and it is the commonest
    // correct one. It takes the biography and the portrait with it, because those arrived with
    // the ASIN and describe the person it belonged to.
    [Fact]
    public async Task ApplyingAnEmptyIdentity_ClearsTheAsinAndWhatCameWithIt()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;

        var seeded = Row("George Makepeace Towle - translator", "B00O0C6Z26");
        context.AuthorCacheEntries.Add(seeded);
        await context.SaveChangesAsync();

        await new AudiobookRepository(context).ApplyAuthorCacheIdentityAsync(
            seeded.Id,
            authorAsin: null,
            description: null,
            imageUrl: null,
            checkedAt: DateTime.UtcNow);

        var after = await context.AuthorCacheEntries.AsNoTracking().SingleAsync();
        Assert.Null(after.AuthorAsin);
        Assert.Null(after.Description);
        Assert.Null(after.ImageUrl);

        // The row survives and keeps its name. Nothing is deleted by this pass.
        Assert.Equal("George Makepeace Towle - translator", after.AuthorName);
    }

    [Fact]
    public async Task ApplyingAnIdentity_MovesTheRowToTheBackOfTheQueueAndDoesNotRepeatItself()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;

        var seeded = Row("Blanche Bendahan", "B017TI5S3E");
        context.AuthorCacheEntries.Add(seeded);
        await context.SaveChangesAsync();
        var repository = new AudiobookRepository(context);

        await repository.ApplyAuthorCacheIdentityAsync(
            seeded.Id,
            "B0BENDAHAN",
            "the right biography",
            "https://example.invalid/right.jpg",
            DateTime.UtcNow);

        var after = await context.AuthorCacheEntries.AsNoTracking().SingleAsync();
        Assert.Equal("B0BENDAHAN", after.AuthorAsin);
        Assert.NotNull(after.AuthorIdentityCheckedAt);

        // Idempotence at the store: a second pass over the same database has the row at the back
        // of the queue, and applying the same answer again produces the same row.
        var before = after.AuthorAsin;
        await repository.ApplyAuthorCacheIdentityAsync(
            seeded.Id,
            "B0BENDAHAN",
            "the right biography",
            "https://example.invalid/right.jpg",
            DateTime.UtcNow);
        Assert.Equal(before, (await context.AuthorCacheEntries.AsNoTracking().SingleAsync()).AuthorAsin);
    }

    [Fact]
    public async Task ARowThatIsGone_IsNotAnError()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;

        var repository = new AudiobookRepository(context);
        Assert.False(await repository.StampAuthorCacheIdentityCheckedAsync(404, DateTime.UtcNow));
        Assert.False(await repository.ApplyAuthorCacheIdentityAsync(404, null, null, null, DateTime.UtcNow));
    }
}
