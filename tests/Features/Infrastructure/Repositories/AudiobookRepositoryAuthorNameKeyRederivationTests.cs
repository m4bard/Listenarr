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
[Trait("Name", "AudiobookRepositoryAuthorNameKeyRederivationTests")]
[Trait("Category", "Persistence")]
public sealed class AudiobookRepositoryAuthorNameKeyRederivationTests : BaseTests
{
    // The key the retired normalizer produced: letters, digits and whitespace survive, so
    // diacritics are kept and spaced-out initials are never merged.
    private static string LegacyKey(string value)
    {
        var cleaned = new string(value
            .Where(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))
            .ToArray());
        return string.Join(
            ' ',
            cleaned.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }

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

    [Theory]
    [InlineData("Émile Zola", "Emile Zola")]
    [InlineData("J. N. Chaney", "J.N. Chaney")]
    [InlineData("P P Corcoran", "PP Corcoran")]
    public async Task RederiveAuthorNameKeysAsync_LegacyKeyedRow_BecomesReachableByName(
        string storedName,
        string searchedName)
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.AuthorCacheEntries.Add(new AuthorCacheEntry
        {
            AuthorName = storedName,
            AuthorNameNormalized = LegacyKey(storedName),
            Region = "us"
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        // The pre-fix data shape genuinely has the bug: the reader cannot find this row.
        Assert.Null(await repository.GetCachedAuthorByNameAsync(searchedName, "us"));

        var result = await repository.RederiveAuthorNameKeysAsync();

        Assert.Equal(1, result.AuthorCacheEntriesCorrected);
        Assert.Equal(0, result.Skipped);
        var found = await repository.GetCachedAuthorByNameAsync(searchedName, "us");
        Assert.NotNull(found);
        Assert.Equal(storedName, found.AuthorName);
    }

    [Fact]
    public async Task RederiveAuthorNameKeysAsync_SecondPass_CorrectsNothing()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.AuthorCacheEntries.Add(new AuthorCacheEntry
        {
            AuthorName = "Émile Zola",
            AuthorNameNormalized = LegacyKey("Émile Zola"),
            Region = "us"
        });
        context.MonitoredAuthors.Add(new MonitoredAuthor
        {
            AuthorName = "J. N. Chaney",
            AuthorNameNormalized = LegacyKey("J. N. Chaney"),
            Region = "us",
            Language = "all"
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        var first = await repository.RederiveAuthorNameKeysAsync();
        var second = await repository.RederiveAuthorNameKeysAsync();

        Assert.Equal(1, first.AuthorCacheEntriesCorrected);
        Assert.Equal(1, first.MonitoredAuthorsCorrected);
        // Assert on the count, not the final state: a pass that rewrites the same value on every
        // boot looks identical from the outside to one that converged.
        Assert.Equal(0, second.AuthorCacheEntriesCorrected);
        Assert.Equal(0, second.MonitoredAuthorsCorrected);
        Assert.Equal(0, second.Skipped);
    }

    // Control for the idempotency assertion above. Without it, "the second pass corrected
    // nothing" and "the pass never does anything" are the same observable.
    [Fact]
    public async Task RederiveAuthorNameKeysAsync_AlreadyCorrectRows_CorrectNothing()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.AuthorCacheEntries.Add(new AuthorCacheEntry
        {
            AuthorName = "Andy Weir",
            AuthorNameNormalized = StringUtils.NormalizeAuthorName("Andy Weir"),
            Region = "us"
        });
        context.MonitoredAuthors.Add(new MonitoredAuthor
        {
            AuthorName = "Andy Weir",
            AuthorNameNormalized = StringUtils.NormalizeAuthorName("Andy Weir"),
            Region = "us",
            Language = "all"
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        var result = await repository.RederiveAuthorNameKeysAsync();

        Assert.Equal(0, result.AuthorCacheEntriesCorrected);
        Assert.Equal(0, result.MonitoredAuthorsCorrected);
        Assert.Equal(0, result.Skipped);
    }

    // Two rows that re-derive onto one key would violate the unique index on
    // (AuthorNameNormalized, Region) and fail the whole startup pass. The loser keeps the key it
    // already has, which is no worse than where it started.
    [Fact]
    public async Task RederiveAuthorNameKeysAsync_CollidingRederivation_SkipsRatherThanThrows()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.AuthorCacheEntries.AddRange(
            new AuthorCacheEntry
            {
                AuthorName = "Emile Zola",
                AuthorNameNormalized = StringUtils.NormalizeAuthorName("Emile Zola"),
                Region = "us",
                UpdatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            },
            new AuthorCacheEntry
            {
                AuthorName = "Émile Zola",
                AuthorNameNormalized = LegacyKey("Émile Zola"),
                Region = "us",
                UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        var result = await repository.RederiveAuthorNameKeysAsync();

        Assert.Equal(0, result.AuthorCacheEntriesCorrected);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(
            LegacyKey("Émile Zola"),
            await context.AuthorCacheEntries
                .Where(entry => entry.AuthorName == "Émile Zola")
                .Select(entry => entry.AuthorNameNormalized)
                .SingleAsync());
    }

    // The same normalized name in two regions is two legitimate rows. Re-derivation must not
    // treat them as a collision.
    [Fact]
    public async Task RederiveAuthorNameKeysAsync_SameNameDifferentRegions_CorrectsBoth()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.AuthorCacheEntries.AddRange(
            new AuthorCacheEntry
            {
                AuthorName = "Émile Zola",
                AuthorNameNormalized = LegacyKey("Émile Zola"),
                Region = "us"
            },
            new AuthorCacheEntry
            {
                AuthorName = "Émile Zola",
                AuthorNameNormalized = LegacyKey("Émile Zola"),
                Region = "fr"
            });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        var result = await repository.RederiveAuthorNameKeysAsync();

        Assert.Equal(2, result.AuthorCacheEntriesCorrected);
        Assert.Equal(0, result.Skipped);
        Assert.NotNull(await repository.GetCachedAuthorByNameAsync("Emile Zola", "us"));
        Assert.NotNull(await repository.GetCachedAuthorByNameAsync("Emile Zola", "fr"));
    }

    // MonitoredAuthors is unique on (name, region, language), so language is part of the slot.
    [Fact]
    public async Task RederiveAuthorNameKeysAsync_MonitoredAuthorsSameNameDifferentLanguage_CorrectsBoth()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.MonitoredAuthors.AddRange(
            new MonitoredAuthor
            {
                AuthorName = "J. N. Chaney",
                AuthorNameNormalized = LegacyKey("J. N. Chaney"),
                Region = "us",
                Language = "all"
            },
            new MonitoredAuthor
            {
                AuthorName = "J. N. Chaney",
                AuthorNameNormalized = LegacyKey("J. N. Chaney"),
                Region = "us",
                Language = "english"
            });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        var result = await repository.RederiveAuthorNameKeysAsync();

        Assert.Equal(2, result.MonitoredAuthorsCorrected);
        Assert.Equal(0, result.Skipped);
        var expected = StringUtils.NormalizeAuthorName("J.N. Chaney");
        Assert.Equal(
            2,
            await context.MonitoredAuthors
                .CountAsync(author => author.AuthorNameNormalized == expected));
    }

    // Two people who merely share initials must stay two rows. The normalizer is what keeps the
    // re-derivation from merging identities, and this pins that it does.
    [Fact]
    public async Task RederiveAuthorNameKeysAsync_DifferentPeopleSharingInitials_StayDistinct()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;
        context.AuthorCacheEntries.AddRange(
            new AuthorCacheEntry
            {
                AuthorName = "J. N. Chaney",
                AuthorNameNormalized = LegacyKey("J. N. Chaney"),
                Region = "us"
            },
            new AuthorCacheEntry
            {
                AuthorName = "J. N. Smith",
                AuthorNameNormalized = LegacyKey("J. N. Smith"),
                Region = "us"
            });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AudiobookRepository(context);

        var result = await repository.RederiveAuthorNameKeysAsync();

        Assert.Equal(2, result.AuthorCacheEntriesCorrected);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(
            2,
            await context.AuthorCacheEntries
                .Select(entry => entry.AuthorNameNormalized)
                .Distinct()
                .CountAsync());
    }
}
