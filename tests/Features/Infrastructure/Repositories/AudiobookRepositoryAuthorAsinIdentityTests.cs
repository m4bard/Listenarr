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

namespace Listenarr.Tests.Features.Infrastructure.Repositories;

/// <summary>
/// The two local halves of author identity: reading an ASIN back out of stored library data,
/// and writing one onto a cached author row. Every ASIN here is a fixture literal.
/// </summary>
[Trait("Area", "Library")]
[Trait("Name", "AudiobookRepositoryAuthorAsinIdentityTests")]
[Trait("Category", "Persistence")]
public sealed class AudiobookRepositoryAuthorAsinIdentityTests : BaseTests
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

    private static AuthorCacheEntry CachedAuthor(string name, string? asin, string region = "us") => new()
    {
        AuthorName = name,
        AuthorNameNormalized = StringUtils.NormalizeAuthorName(name),
        AuthorAsin = asin,
        Region = region
    };

    private static AuthorCacheEntry IncomingAuthor(string name, string? asin, string region = "us") => new()
    {
        AuthorName = name,
        AuthorNameNormalized = StringUtils.NormalizeAuthorName(name),
        AuthorAsin = asin,
        Region = region
    };

    private static Audiobook Book(string title, string[] authors, string[] authorAsins) => new()
    {
        Title = title,
        Authors = [.. authors],
        AuthorAsins = [.. authorAsins],
        BasePath = $"/library/{title}",
        FilePath = $"/library/{title}/{title}.m4b",
        FileSize = 1234,
        ImageUrl = "/images/cover.jpg"
    };

    [Fact]
    public async Task GetAuthorAsinByNameAsync_MultiAuthorBook_AnswersWithWhicheverAsinIsFirst()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.Audiobooks.Add(Book(
            "A Co-Authored Book",
            ["Unknown Person", "Andy Weir"],
            ["FIXTUREAUT1"]));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        var asin = await repository.GetAuthorAsinByNameAsync("Unknown Person");

        // CHARACTERIZATION: AuthorAsins is a deduplicated set of successful lookups and is not
        // positionally parallel to Authors, so the first entry is returned for whichever name
        // happened to match. Here that hands one author the other author's id.
        Assert.Equal("FIXTUREAUT1", asin);
    }

    [Fact]
    public async Task GetAuthorAsinByNameAsync_SingleAuthorSingleAsinBook_AnswersWithThatAsin()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.Audiobooks.Add(Book("A Solo Book", ["Andy Weir"], ["FIXTUREAUT1"]));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        // CONTROL. Without this, "answers null" and "the function is broken" look the same.
        Assert.Equal("FIXTUREAUT1", await repository.GetAuthorAsinByNameAsync("Andy Weir"));
    }

    [Fact]
    public async Task GetAuthorAsinByNameAsync_SingleAuthorTwoAsins_AnswersWithWhicheverAsinIsFirst()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.Audiobooks.Add(Book(
            "A Book With Two Recorded Ids",
            ["Andy Weir"],
            ["FIXTUREAUT1", "FIXTUREAUT2"]));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        // CHARACTERIZATION: one author, two ids, and nothing recorded says which is theirs.
        Assert.Equal("FIXTUREAUT1", await repository.GetAuthorAsinByNameAsync("Andy Weir"));
    }

    [Fact]
    public async Task UpsertCachedAuthorAsync_WritingANewNameAgainstAnOwnedAsin_RenamesTheOwner()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.AuthorCacheEntries.Add(CachedAuthor("Author One", "FIXTURESHR1"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        await repository.UpsertCachedAuthorAsync(IncomingAuthor("Author Two", "FIXTURESHR1"));

        // CHARACTERIZATION: the row is resolved by ASIN before it is resolved by name, and then
        // its name is overwritten. Author One's identity, photo and biography are now filed under
        // Author Two, and nothing is logged.
        context.ChangeTracker.Clear();
        var rows = await context.AuthorCacheEntries.AsNoTracking().ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal("Author Two", row.AuthorName);
        Assert.Equal("FIXTURESHR1", row.AuthorAsin);
    }

    [Fact]
    public async Task UpsertCachedAuthorAsync_WhenTheIncomingNameAlreadyHasARow_ViolatesTheUniqueIndex()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.AuthorCacheEntries.AddRange(
            CachedAuthor("Author One", "FIXTURESHR1"),
            CachedAuthor("Author Two", null));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        // CHARACTERIZATION: the rename would produce two rows keyed (author two, us), so the
        // unique index rejects the write. The cache entry is lost and both callers swallow this
        // as a warning, which means the whole remote lookup repeats on every later request.
        await Assert.ThrowsAnyAsync<UniqueConstraintViolationException>(
            () => repository.UpsertCachedAuthorAsync(IncomingAuthor("Author Two", "FIXTURESHR1")));
    }

    [Fact]
    public async Task UpsertCachedAuthorAsync_TwoSpellingsOfOneAuthor_ConvergeOnOneRow()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.AuthorCacheEntries.Add(CachedAuthor("Andy Weir", "FIXTUREAUT1"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        await repository.UpsertCachedAuthorAsync(IncomingAuthor("andy  weir", "FIXTUREAUT1"));

        // CONTROL, and the reason the (AuthorAsin, Region) index is deliberately not unique: two
        // spellings of one author share an id legitimately. Nothing added here may start refusing
        // that, or it will have traded a rename for a duplicate.
        context.ChangeTracker.Clear();
        var rows = await context.AuthorCacheEntries.AsNoTracking().ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal("FIXTUREAUT1", row.AuthorAsin);
    }

    [Fact]
    public async Task UpsertCachedAuthorAsync_SameNameSameAsin_UpdatesInPlace()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.AuthorCacheEntries.Add(CachedAuthor("Andy Weir", "FIXTUREAUT1"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        var incoming = IncomingAuthor("Andy Weir", "FIXTUREAUT1");
        incoming.Description = "Fixture biography.";
        await repository.UpsertCachedAuthorAsync(incoming);

        // CONTROL for the ordinary path.
        context.ChangeTracker.Clear();
        var row = Assert.Single(await context.AuthorCacheEntries.AsNoTracking().ToListAsync());
        Assert.Equal("Fixture biography.", row.Description);
    }
}
