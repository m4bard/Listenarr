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

[Trait("Area", "Library")]
[Trait("Name", "AudiobookRepositoryAuthorCanonicalizationTests")]
[Trait("Category", "Persistence")]
public sealed class AudiobookRepositoryAuthorCanonicalizationTests : BaseTests
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

    private static AuthorCacheEntry CachedAuthor(string name, string region = "us") => new()
    {
        AuthorName = name,
        AuthorNameNormalized = StringUtils.NormalizeAuthorName(name),
        AuthorAsin = "B000CACHE1",
        Region = region
    };

    private static Audiobook Book(string title, params string[] authors) => new()
    {
        Title = title,
        Authors = [.. authors],
        BasePath = $"/library/{title}",
        FilePath = $"/library/{title}/{title}.m4b",
        FileSize = 1234,
        ImageUrl = "/images/cover.jpg",
        AuthorAsins = ["B000BOOKAA"]
    };

    [Fact]
    public async Task CanonicalizeStoredAuthorNamesAsync_DriftedRows_AdoptTheCachedSpelling()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.AuthorCacheEntries.Add(CachedAuthor("Andy Weir"));
        context.Audiobooks.AddRange(
            Book("The Martian", "Andy Weir"),
            Book("Artemis", "Andy  Weir"),
            Book("Project Hail Mary", "andy weir"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        var updated = await repository.CanonicalizeStoredAuthorNamesAsync();

        Assert.Equal(2, updated);
        var spellings = await context.Audiobooks
            .AsNoTracking()
            .Select(audiobook => audiobook.Authors!)
            .ToListAsync();
        Assert.All(spellings, authors => Assert.Equal(["Andy Weir"], authors));
    }

    [Fact]
    public async Task CanonicalizeStoredAuthorNamesAsync_SecondPass_UpdatesNothing()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.AuthorCacheEntries.Add(CachedAuthor("Andy Weir"));
        context.Audiobooks.Add(Book("Artemis", "Andy  Weir"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        var first = await repository.CanonicalizeStoredAuthorNamesAsync();
        var second = await repository.CanonicalizeStoredAuthorNamesAsync();

        Assert.Equal(1, first);
        // The count, not the final state. A pass that rewrites the same value on every boot is
        // indistinguishable from a converged one if you only look at what is stored.
        Assert.Equal(0, second);
    }

    // The control for the idempotency assertion. Without it, "the second pass updated nothing"
    // and "the pass never does anything" are the same observable.
    [Fact]
    public async Task CanonicalizeStoredAuthorNamesAsync_CleanLibrary_UpdatesNothing()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.AuthorCacheEntries.Add(CachedAuthor("Andy Weir"));
        context.Audiobooks.AddRange(
            Book("The Martian", "Andy Weir"),
            Book("Artemis", "Andy Weir"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        Assert.Equal(0, await repository.CanonicalizeStoredAuthorNamesAsync());
    }

    // The guard that makes this safe to approve. Two people whose names share a surname and a
    // first initial are not one person, and the pass must not decide otherwise.
    [Theory]
    [InlineData("Andy Weir", "Anthony Weir")]
    [InlineData("J. N. Chaney", "J. N. Smith")]
    [InlineData("Arthur Conan Doyle", "Sir Arthur Conan Doyle")]
    [InlineData("Andy Weir", "Andy Weir, PhD")]
    public async Task CanonicalizeStoredAuthorNamesAsync_DistinctCredits_AreNeverMerged(
        string cachedName,
        string storedName)
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.AuthorCacheEntries.Add(CachedAuthor(cachedName));
        context.Audiobooks.Add(Book("A Distinct Credit", storedName));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        var updated = await repository.CanonicalizeStoredAuthorNamesAsync();

        Assert.Equal(0, updated);
        Assert.Equal(
            [storedName],
            await context.Audiobooks.AsNoTracking()
                .Select(audiobook => audiobook.Authors!)
                .SingleAsync());
    }

    [Fact]
    public async Task CanonicalizeStoredAuthorNamesAsync_AuthorWithNoCacheRow_IsLeftAlone()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.AuthorCacheEntries.Add(CachedAuthor("Andy Weir"));
        context.Audiobooks.Add(Book("Two Credits", "Unknown  Person", "Andy  Weir"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        var updated = await repository.CanonicalizeStoredAuthorNamesAsync();

        Assert.Equal(1, updated);
        // Name-first, never index-first: the corrected author is the one that matched, not the
        // one sharing its position with an ASIN.
        Assert.Equal(
            ["Unknown  Person", "Andy Weir"],
            await context.Audiobooks.AsNoTracking()
                .Select(audiobook => audiobook.Authors!)
                .SingleAsync());
    }

    // Directly guards the UpdateAsync hazard: that method restores BasePath, FilePath, FileSize
    // and ImageUrl on detached entities, and a whole-entity write from a stub would blank every
    // column this pass never looked at.
    [Fact]
    public async Task CanonicalizeStoredAuthorNamesAsync_TouchesNothingButAuthors()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.AuthorCacheEntries.Add(CachedAuthor("Andy Weir"));
        context.Audiobooks.Add(Book("Artemis", "Andy  Weir"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        Assert.Equal(1, await repository.CanonicalizeStoredAuthorNamesAsync());

        var saved = await context.Audiobooks.AsNoTracking().SingleAsync();
        Assert.Equal(["Andy Weir"], saved.Authors);
        Assert.Equal(["B000BOOKAA"], saved.AuthorAsins);
        Assert.Equal("/library/Artemis", saved.BasePath);
        Assert.Equal("/library/Artemis/Artemis.m4b", saved.FilePath);
        Assert.Equal(1234, saved.FileSize);
        Assert.Equal("/images/cover.jpg", saved.ImageUrl);
        Assert.Equal("Artemis", saved.Title);
    }

    [Fact]
    public async Task CanonicalizeStoredAuthorNamesAsync_AfterAFreshContext_StaysConverged()
    {
        var databasePath = Path.Join(
            FileService.GetTempDirectory("listenarr-canonicalization-restart"),
            "listenarr.db");
        var options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;

        await using (var seeded = new ListenArrDbContext(options))
        {
            await seeded.Database.EnsureCreatedAsync();
            seeded.AuthorCacheEntries.Add(CachedAuthor("Andy Weir"));
            seeded.Audiobooks.Add(Book("Artemis", "Andy  Weir"));
            await seeded.SaveChangesAsync();
            Assert.Equal(1, await new AudiobookRepository(seeded).CanonicalizeStoredAuthorNamesAsync());
        }

        await using (var reopened = new ListenArrDbContext(options))
        {
            Assert.Equal(
                ["Andy Weir"],
                await reopened.Audiobooks.AsNoTracking()
                    .Select(audiobook => audiobook.Authors!)
                    .SingleAsync());
            Assert.Equal(
                0,
                await new AudiobookRepository(reopened).CanonicalizeStoredAuthorNamesAsync());
        }
    }

    // AuthorCacheEntries is unique on (AuthorNameNormalized, Region), so one normalized name can
    // hold two legitimately different spellings in two regions. A book must resolve to its own
    // region's row, and it must resolve to the same one on every boot -- picking by anything that
    // varies between runs gives a pass that rewrites both books back and forth forever.
    [Fact]
    public async Task CanonicalizeStoredAuthorNamesAsync_TwoRegions_ResolvesEachBookToItsOwnAndDoesNotOscillate()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.AuthorCacheEntries.AddRange(
            CachedAuthor("Jules Verne"),
            CachedAuthor("Jules VERNE", "fr"));
        var american = Book("Around the World", "Jules  Verne");
        var french = Book("Le Tour du Monde", "Jules  Verne");
        context.Audiobooks.AddRange(american, french);
        await context.SaveChangesAsync();
        context.AudiobookExternalIdentifiers.AddRange(
            new AudiobookExternalIdentifier
            {
                AudiobookId = american.Id,
                Type = AudiobookExternalIdentifierType.Asin,
                ValueRaw = "B000REGUS1",
                ValueNormalized = "B000REGUS1",
                Region = "us",
                IsPrimary = true
            },
            new AudiobookExternalIdentifier
            {
                AudiobookId = french.Id,
                Type = AudiobookExternalIdentifierType.Asin,
                ValueRaw = "B000REGFR1",
                ValueNormalized = "B000REGFR1",
                Region = "fr",
                IsPrimary = true
            });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        var first = await repository.CanonicalizeStoredAuthorNamesAsync();
        var second = await repository.CanonicalizeStoredAuthorNamesAsync();

        Assert.Equal(2, first);
        Assert.Equal(0, second);
        Assert.Equal(
            ["Jules Verne"],
            await context.Audiobooks.AsNoTracking()
                .Where(audiobook => audiobook.Id == american.Id)
                .Select(audiobook => audiobook.Authors!)
                .SingleAsync());
        Assert.Equal(
            ["Jules VERNE"],
            await context.Audiobooks.AsNoTracking()
                .Where(audiobook => audiobook.Id == french.Id)
                .Select(audiobook => audiobook.Authors!)
                .SingleAsync());
    }

    // The join target is the cache row's normalized name, and a row written before that column's
    // normalizer was unified still holds a key the current reader never produces. Rather than
    // trust the stored column, the pass re-derives the key from AuthorName as it builds its map,
    // so it is correct whether or not RederiveAuthorNameKeysAsync has reached this database yet.
    // The startup order still puts the re-derivation first -- that is what makes the column agree
    // with GetCachedAuthorByNameAsync -- but this pass does not depend on it having succeeded.
    [Fact]
    public async Task CanonicalizeStoredAuthorNamesAsync_LegacyKeyedCacheRow_IsMatchedAnyway()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.AuthorCacheEntries.Add(new AuthorCacheEntry
        {
            AuthorName = "Émile Zola",
            // What the retired normalizer produced: diacritics kept, initials never merged.
            AuthorNameNormalized = "émile zola",
            Region = "us"
        });
        context.Audiobooks.Add(Book("Germinal", "Emile  Zola"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        var updated = await repository.CanonicalizeStoredAuthorNamesAsync();
        await repository.RederiveAuthorNameKeysAsync();
        var afterRederivation = await repository.CanonicalizeStoredAuthorNamesAsync();

        Assert.Equal(1, updated);
        Assert.Equal(0, afterRederivation);
        Assert.Equal(
            ["Émile Zola"],
            await context.Audiobooks.AsNoTracking()
                .Select(audiobook => audiobook.Authors!)
                .SingleAsync());
    }

    // Migrating a released schema forward and inserting pre-drift rows by raw SQL is the only
    // shape that exercises this against data like a real upgrade rather than data the test wrote
    // through the same model it is checking.
    [Fact]
    public async Task CanonicalizeStoredAuthorNamesAsync_RowsInsertedByRawSql_AreCorrected()
    {
        var databasePath = Path.Join(
            FileService.GetTempDirectory("listenarr-canonicalization-upgrade"),
            "listenarr.db");
        var options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;

        await using var context = new ListenArrDbContext(options);
        await context.Database.MigrateAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AuthorCacheEntries" (
                "AuthorName", "AuthorNameNormalized", "AuthorAsin", "Region",
                "CreatedAt", "UpdatedAt")
            VALUES ({"Andy Weir"}, {"andy weir"}, {"B000CACHE1"}, {"us"},
                {DateTime.UtcNow}, {DateTime.UtcNow});
            """);
        context.Audiobooks.Add(Book("Artemis", "placeholder"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        // Put the drifted spelling in as stored text rather than through the model, so the pass
        // meets the column exactly as an upgraded database presents it.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Audiobooks" SET "Authors" = {"[\"Andy  Weir\"]"};
            """);
        var repository = new AudiobookRepository(context);

        var updated = await repository.CanonicalizeStoredAuthorNamesAsync();

        Assert.Equal(1, updated);
        Assert.Equal(
            ["Andy Weir"],
            await context.Audiobooks.AsNoTracking()
                .Select(audiobook => audiobook.Authors!)
                .SingleAsync());
        Assert.Equal("/library/Artemis", await context.Audiobooks.AsNoTracking()
            .Select(audiobook => audiobook.BasePath)
            .SingleAsync());
    }
}
