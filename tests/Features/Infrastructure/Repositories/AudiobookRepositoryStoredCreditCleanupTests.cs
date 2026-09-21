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

// The credit rule cleans what arrives. This is the same rule applied to what is already stored,
// which on an install that predates the rule is most of it.
namespace Listenarr.Tests.Features.Infrastructure.Repositories;

[Trait("Area", "Library")]
[Trait("Name", "AudiobookRepositoryStoredCreditCleanupTests")]
[Trait("Category", "Persistence")]
public sealed class AudiobookRepositoryStoredCreditCleanupTests : BaseTests
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

    private static Audiobook Book(string title, params string[] authors) =>
        new()
        {
            Title = title,
            Authors = [.. authors]
        };

    [Fact]
    public async Task Apply_RemovesTheRoleAndKeepsThePerson()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;

        context.Audiobooks.Add(Book("Crime and Punishment", "Fyodor Dostoevsky", "Constance Garnett - translator"));
        await context.SaveChangesAsync();

        var result = await new AudiobookRepository(context).CleanRoleSuffixesFromStoredAuthorsAsync(10, apply: true);

        Assert.Single(result.Changes);
        var after = await context.Audiobooks.AsNoTracking().SingleAsync();
        Assert.Equal(new[] { "Fyodor Dostoevsky", "Constance Garnett" }, after.Authors);
    }

    // THE CONTROL. A byline with nothing wrong with it has to come back untouched, or every
    // assertion above is also satisfied by a method that rewrites the whole table.
    [Fact]
    public async Task Apply_LeavesACleanBylineExactlyAsItWas()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;

        context.Audiobooks.AddRange(
            Book("Good Omens", "Terry Pratchett", "Neil Gaiman"),
            Book("Alice", "Lewis Carroll (Illustrated)"),
            Book("The Complete Stories", "Hector Hugh Munro (Saki)"));
        await context.SaveChangesAsync();

        var result = await new AudiobookRepository(context).CleanRoleSuffixesFromStoredAuthorsAsync(10, apply: true);

        // Carroll's edition descriptor is removed, which is the rule working; the co-author pair
        // and the pen name are not touched, which is the rule not overshooting.
        Assert.Single(result.Changes);
        var books = await context.Audiobooks.AsNoTracking().OrderBy(book => book.Id).ToListAsync();
        Assert.Equal(new[] { "Terry Pratchett", "Neil Gaiman" }, books[0].Authors);
        Assert.Equal(new[] { "Lewis Carroll" }, books[1].Authors);
        Assert.Equal(new[] { "Hector Hugh Munro (Saki)" }, books[2].Authors);
    }

    [Fact]
    public async Task Examine_ReportsWhatItWouldDoAndChangesNothing()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;

        context.Audiobooks.Add(Book("Crime and Punishment", "Fyodor Dostoevsky", "Constance Garnett - translator"));
        await context.SaveChangesAsync();

        var repository = new AudiobookRepository(context);
        var result = await repository.CleanRoleSuffixesFromStoredAuthorsAsync(10, apply: false);

        Assert.Single(result.Changes);
        Assert.Equal(new[] { "Fyodor Dostoevsky", "Constance Garnett" }, result.Changes[0].After);

        // Read back through a context that has never seen this row, so a tracked entity the
        // preview had mutated in memory could not stand in for a stored one that it had not.
        await using var reopened = new ListenArrDbContext(
            new DbContextOptionsBuilder<ListenArrDbContext>().UseSqlite(connection).Options);
        var stored = await reopened.Audiobooks.AsNoTracking().SingleAsync();
        Assert.Equal(new[] { "Fyodor Dostoevsky", "Constance Garnett - translator" }, stored.Authors);
    }

    [Fact]
    public async Task Apply_IsIdempotent()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;

        context.Audiobooks.Add(Book("Crime and Punishment", "Fyodor Dostoevsky", "Constance Garnett - translator"));
        await context.SaveChangesAsync();

        var repository = new AudiobookRepository(context);
        Assert.Single((await repository.CleanRoleSuffixesFromStoredAuthorsAsync(10, apply: true)).Changes);

        // A cleaned book stops being a candidate, which is why the pass converges with no cursor
        // to remember where it stopped.
        Assert.Empty((await repository.CleanRoleSuffixesFromStoredAuthorsAsync(10, apply: true)).Changes);
    }

    [Fact]
    public async Task Apply_TakesNoMoreBooksThanItsCeilingAndTheRestSurviveForTheNextRun()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;

        for (var i = 1; i <= 5; i++)
        {
            context.Audiobooks.Add(Book($"Book {i}", "A Real Author", $"Contributor {i} - translator"));
        }

        await context.SaveChangesAsync();
        var repository = new AudiobookRepository(context);

        Assert.Equal(2, (await repository.CleanRoleSuffixesFromStoredAuthorsAsync(2, apply: true)).Changes.Count);
        Assert.Equal(2, (await repository.CleanRoleSuffixesFromStoredAuthorsAsync(2, apply: true)).Changes.Count);
        Assert.Single((await repository.CleanRoleSuffixesFromStoredAuthorsAsync(2, apply: true)).Changes);
        Assert.Empty((await repository.CleanRoleSuffixesFromStoredAuthorsAsync(2, apply: true)).Changes);

        var books = await context.Audiobooks.AsNoTracking().ToListAsync();
        Assert.All(books, book => Assert.DoesNotContain(book.Authors!, author => author.Contains(" - translator")));
    }

    // The scan and the write are two separate readings, so what is reported has to come from
    // the second one. Reporting the plan would name a before and an after that never happened
    // on any row somebody changed in between.
    [Fact]
    public async Task Apply_ReportsWhatItWroteRatherThanWhatItPlanned()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;

        var book = Book("Crime and Punishment", "Fyodor Dostoevsky", "Constance Garnett - translator");
        context.Audiobooks.Add(book);
        await context.SaveChangesAsync();

        // Somebody fixes it by hand between the scan and the write. The plan still says the row
        // needs cleaning; the row does not.
        await using var other = new ListenArrDbContext(
            new DbContextOptionsBuilder<ListenArrDbContext>().UseSqlite(connection).Options);
        var meanwhile = await other.Audiobooks.SingleAsync();
        meanwhile.Authors = ["Fyodor Dostoevsky", "Constance Garnett"];
        await other.SaveChangesAsync();

        await using var fresh = new ListenArrDbContext(
            new DbContextOptionsBuilder<ListenArrDbContext>().UseSqlite(connection).Options);
        var result = await new AudiobookRepository(fresh).CleanRoleSuffixesFromStoredAuthorsAsync(10, apply: true);

        Assert.Empty(result.Changes);
        Assert.Equal(
            new[] { "Fyodor Dostoevsky", "Constance Garnett" },
            (await fresh.Audiobooks.AsNoTracking().SingleAsync()).Authors);
    }

    [Fact]
    public async Task Apply_RefusesAnEmptyCeiling()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;

        context.Audiobooks.Add(Book("Crime and Punishment", "Fyodor Dostoevsky", "Constance Garnett - translator"));
        await context.SaveChangesAsync();

        var repository = new AudiobookRepository(context);
        Assert.Empty((await repository.CleanRoleSuffixesFromStoredAuthorsAsync(0, apply: true)).Changes);
        Assert.Equal(
            new[] { "Fyodor Dostoevsky", "Constance Garnett - translator" },
            (await context.Audiobooks.AsNoTracking().SingleAsync()).Authors);
    }

    // A book credited to nobody but a contributor is the case the rejected drop rule could not
    // help at all, and the reason the rule that shipped removes the role instead.
    [Fact]
    public async Task Apply_FixesABookCreditedToNobodyButItsTranslator()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;

        context.Audiobooks.Add(Book("The Art of War", "Lionel Giles - translator"));
        await context.SaveChangesAsync();

        await new AudiobookRepository(context).CleanRoleSuffixesFromStoredAuthorsAsync(10, apply: true);

        Assert.Equal(new[] { "Lionel Giles" }, (await context.Audiobooks.AsNoTracking().SingleAsync()).Authors);
    }

    [Fact]
    public async Task Apply_IgnoresABookWithNoAuthorsAtAll()
    {
        var (connection, context) = await OpenAsync();
        await using var _ = connection;
        await using var __ = context;

        context.Audiobooks.AddRange(
            new Audiobook { Title = "No Authors", Authors = null },
            new Audiobook { Title = "Empty Authors", Authors = [] });
        await context.SaveChangesAsync();

        var result = await new AudiobookRepository(context).CleanRoleSuffixesFromStoredAuthorsAsync(10, apply: true);

        Assert.Empty(result.Changes);
    }
}
